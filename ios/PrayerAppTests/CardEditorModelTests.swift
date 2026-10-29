import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withEditor(
    cardId: Int64? = 2, archivedFolderId: Int = 2,
    _ body: (CardEditorModel, PrayerDatabase) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("CardEditorModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "CardEditorModelTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = archivedFolderId
    let model = CardEditorModel(card: try cardRecord(cardId, database), database: database, preferences: preferences)
    model.load()
    try await body(model, database)
}

private func cardRecord(_ id: Int64?, _ database: PrayerDatabase) throws -> PrayerCardRecord? {
    guard let id else { return nil }
    return try allCards(database).first { $0.id == id }
}

private func allCards(_ database: PrayerDatabase) throws -> [PrayerCardRecord] {
    try database.cardList(isSessionUnlocked: true).sections.flatMap { $0.cards.map(\.card) }
}

@MainActor
@Suite struct CardEditorModelTests {
    @Test func newDefaults() async throws {
        try await withEditor(cardId: nil) { model, _ in
            #expect(model.isNew)
            #expect(model.boxId == 0)
            #expect(model.protectionMode == .unprotected)
        }
    }

    @Test func createSavesChosenValues() async throws {
        try await withEditor(cardId: nil) { model, database in
            let box = try CollectionStore(database: database).create(name: "Editor Box", now: .now)
            model.load()
            model.title = "Created"
            model.boxId = box.id
            model.protectionMode = .hidden
            #expect(model.save())
            let created = try #require(try allCards(database).first { $0.title == "Created" })
            #expect(created.boxId == box.id)
            #expect(created.protectionMode == .hidden)
        }
    }

    @Test func saveAndAddAnotherKeepsCollection() async throws {
        try await withEditor(cardId: nil) { model, database in
            let box = try CollectionStore(database: database).create(name: "Editor Keep", now: .now)
            model.load()
            let before = try allCards(database).count
            model.title = "Again"
            model.boxId = box.id
            model.protectionMode = .lockedVisible
            #expect(model.saveAndAddAnother())
            #expect(model.title == "")
            #expect(model.protectionMode == .unprotected)
            #expect(model.boxId == box.id)
            let after = try allCards(database).count
            #expect(after == before + 1)
        }
    }

    @Test func blankTitleCannotSave() async throws {
        try await withEditor(cardId: nil) { model, _ in
            model.title = "   "
            #expect(!model.canSave)
        }
    }

    @Test func collectionsExcludeSystemAndArchived() async throws {
        try await withEditor(cardId: nil) { model, database in
            let store = CollectionStore(database: database)
            _ = try store.create(name: "Zeta", now: .now)
            _ = try store.create(name: "Alpha", now: .now)
            model.load()
            let names = model.collections.map(\.name)
            #expect(names == names.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending })
            #expect(names.contains("Alpha") && names.contains("Zeta"))
            #expect(!model.collections.contains { $0.id == 2 || $0.isSystem })
        }
    }

    @Test func editUnchangedWritesNothing() async throws {
        try await withEditor { model, database in
            let before = try #require(try cardRecord(2, database)).updatedAtTicks
            #expect(model.save(now: Date(timeIntervalSinceNow: 3600)))
            let after = try #require(try cardRecord(2, database)).updatedAtTicks
            #expect(after == before)
        }
    }

    @Test func editKeepsLegacyFields() async throws {
        try await withEditor(cardId: nil) { _, database in
            let preferences = AppPreferences(defaults: UserDefaults(suiteName: "CardEditorModelTests-\(UUID().uuidString)")!)
            preferences.archivedFolderId = 2
            let store = CollectionStore(database: database)
            let box = try store.create(name: "Legacy Box", now: .now)
            let mutations = CardMutations(database: database, preferences: preferences)
            let made = try mutations.create(
                CardDraft(title: "Legacy", boxId: box.id, protectionMode: .hidden), now: .now)
            try mutations.archive(id: made.id, now: .now)
            let stored = try #require(try cardRecord(made.id, database))
            #expect(stored.preArchiveBoxId == box.id)
            let model = CardEditorModel(card: stored, database: database, preferences: preferences)
            model.load()
            model.title = "Legacy Renamed"
            #expect(model.save())
            let after = try #require(try cardRecord(made.id, database))
            #expect(after.title == "Legacy Renamed")
            #expect(after.preArchiveBoxId == box.id)
            #expect(after.protectionMode == .hidden)
            #expect(after.boxId == 2)
        }
    }

    @Test func archiveThenUnarchive() async throws {
        try await withEditor { model, database in
            #expect(model.archive())
            #expect(model.isArchived)
            let archived = try #require(try cardRecord(2, database))
            #expect(archived.boxId == 2)
            #expect(model.unarchive())
            #expect(!model.isArchived)
            #expect(model.boxId == 0)
        }
    }

    @Test func prayerCountCountsAllPrayers() async throws {
        try await withEditor { model, _ in
            #expect(model.prayerCount == 2)
        }
    }

    @Test func failedSaveKeepsValues() async throws {
        try await withEditor(archivedFolderId: 0) { model, _ in
            let title = model.title
            #expect(!model.archive())
            #expect(model.alert != nil)
            #expect(model.title == title)
        }
    }
}
