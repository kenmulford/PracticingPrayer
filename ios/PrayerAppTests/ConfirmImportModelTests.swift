import Foundation
import GRDB
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

private let payload = SharePayload.card(
    title: "Shared",
    requests: [SharedPrayer(title: "Sample Prayer Entry 1", notes: nil), SharedPrayer(title: "New One", notes: nil)])

@MainActor
private func withModel(
    _ payload: SharePayload = payload,
    _ body: (ConfirmImportModel, PrayerDatabase) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("ConfirmImportModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let local = DateComponents(calendar: .current, year: 2026, month: 3, day: 5, hour: 12)
    let now = try #require(local.date)
    let model = ConfirmImportModel(
        database: database, payload: payload, now: now, locale: Locale(identifier: "en_US_POSIX"))
    try await body(model, database)
}

private func titles(_ entries: [QuickAddModel.PrayerEntry]) -> [String] { entries.map(\.title) }

@MainActor
@Suite struct ConfirmImportModelTests {
    @Test func seedsEntriesAndDefaultTitle() async throws {
        try await withModel { model, _ in
            #expect(model.form.cardTitle == "Imported Mar 5")
            #expect(titles(model.form.prayers) == ["Sample Prayer Entry 1", "New One"])
            #expect(!model.form.isDirty)
        }
    }

    @Test func existingCardMovesDuplicates() async throws {
        try await withModel { model, _ in
            model.form.addTo = .existingCard
            model.form.selectCard(id: 1, title: "General")
            #expect(titles(model.alreadyOnCard) == ["Sample Prayer Entry 1"])
            #expect(titles(model.form.prayers) == ["New One"])
        }
    }

    @Test func addBackMovesEntry() async throws {
        try await withModel { model, _ in
            model.form.addTo = .existingCard
            model.form.selectCard(id: 1, title: "General")
            model.addBack(id: model.alreadyOnCard[0].id)
            #expect(model.alreadyOnCard.isEmpty)
            #expect(model.form.prayers.count == 2)
        }
    }

    @Test func allDuplicatesDisablesSave() async throws {
        let only = SharePayload.request(SharedPrayer(title: "Sample Prayer Entry 1", notes: nil))
        try await withModel(only) { model, _ in
            model.form.addTo = .existingCard
            model.form.selectCard(id: 1, title: "General")
            #expect(model.form.prayers.isEmpty)
            #expect(!model.form.canSave)
        }
    }

    @Test func emptyPayloadDisablesSave() async throws {
        try await withModel(.card(title: "Empty", requests: [])) { model, _ in
            #expect(model.form.prayers.isEmpty)
            #expect(!model.form.canSave)
        }
    }

    @Test func switchingBackRestoresTitleAndEdits() async throws {
        try await withModel { model, _ in
            model.form.prayers[0].title = "Edited Prayer"
            model.form.addTo = .existingCard
            model.form.selectCard(id: 1, title: "General")
            model.form.addTo = .newCard
            #expect(model.form.cardTitle == "Imported Mar 5")
            #expect(titles(model.form.prayers) == ["Edited Prayer", "New One"])
            #expect(model.alreadyOnCard.isEmpty)
        }
    }

    @Test func saveExistingWritesOnlyToImport() async throws {
        try await withModel { model, database in
            let before = try await database.dbQueue.read {
                try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM PrayerRequest WHERE PrayerCardId = 1") ?? 0
            }
            model.form.addTo = .existingCard
            model.form.selectCard(id: 1, title: "General")
            let saved = await model.form.save()
            #expect(saved)
            let after = try await database.dbQueue.read {
                try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM PrayerRequest WHERE PrayerCardId = 1") ?? 0
            }
            #expect(after == before + 1)
        }
    }

    @Test func failedSaveKeepsEntries() async throws {
        try await withModel { model, _ in
            model.form.addTo = .existingCard
            model.form.selectCard(id: 999, title: "Missing")
            let saved = await model.form.save()
            #expect(!saved)
            #expect(model.form.errorMessage != nil)
            #expect(model.form.prayers.count + model.alreadyOnCard.count == 2)
        }
    }
}
