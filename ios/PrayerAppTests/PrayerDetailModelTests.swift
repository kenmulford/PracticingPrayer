import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(
    title: String, tagNames: [String] = [], interactions: Int = 0,
    _ body: (PrayerDetailModel, PrayerDatabase, Int64) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("PrayerDetailModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    var tagIds: [Int64] = []
    for name in tagNames {
        tagIds.append(try TagStore(database: database).createTag(name: name, color: "#336699", now: .now).id)
    }
    let store = PrayerStore(database: database)
    let id = try store.create(PrayerDraft(prayerCardId: 2, title: title, tagIds: tagIds), now: .now)
    for _ in 0..<interactions {
        _ = try PrayerInteractionRecorder(database: database).recordPrayed(prayerId: id, now: .now)
    }
    let model = PrayerDetailModel(prayerId: id, database: database)
    model.load()
    try await body(model, database, id)
}

@MainActor
@Suite struct PrayerDetailModelTests {
    @Test func loadShowsPrayer() async throws {
        try await withModel(title: "loadShowsPrayer") { model, database, _ in
            let cardTitle = try database.cardList(isSessionUnlocked: false).sections
                .flatMap(\.cards).first { $0.card.id == 2 }?.card.title
            #expect(model.phase == .loaded)
            #expect(model.prayer?.title == "loadShowsPrayer")
            #expect(model.cardTitle == cardTitle)
        }
    }

    @Test func missingIdIsNotFound() async throws {
        try await withModel(title: "missingIdIsNotFound") { _, database, _ in
            let model = PrayerDetailModel(prayerId: 999_999, database: database)
            model.load()
            #expect(model.phase == .notFound)
            #expect(model.prayer == nil)
        }
    }

    @Test func prayedSummaryHiddenAtZero() async throws {
        try await withModel(title: "prayedSummaryHiddenAtZero") { model, _, _ in
            #expect(model.prayedSummary == nil)
        }
    }

    @Test func prayedSummarySingular() async throws {
        try await withModel(title: "prayedSummarySingular", interactions: 1) { model, _, _ in
            #expect(model.prayedSummary?.hasPrefix("1 time since ") == true)
        }
    }

    @Test func prayedSummaryPlural() async throws {
        try await withModel(title: "prayedSummaryPlural", interactions: 3) { model, _, _ in
            #expect(model.prayedSummary?.hasPrefix("3 times since ") == true)
        }
    }

    @Test func markAnsweredStampsToday() async throws {
        try await withModel(title: "markAnsweredStampsToday") { model, _, _ in
            let now = Date.now
            model.markAnswered(now: now)
            #expect(model.prayer?.isAnswered == true)
            #expect(model.prayer?.answeredAtTicks == Calendar.current.startOfDay(for: now).ticks)
        }
    }

    @Test func markActiveClearsAnswered() async throws {
        try await withModel(title: "markActiveClearsAnswered") { model, _, _ in
            model.markAnswered()
            model.markActive()
            #expect(model.prayer?.answeredAtTicks == nil)
            #expect(model.prayer?.isAnswered == false)
        }
    }

    @Test func deleteRemovesPrayer() async throws {
        try await withModel(title: "deleteRemovesPrayer") { model, _, _ in
            #expect(model.delete())
            model.load()
            #expect(model.phase == .notFound)
        }
    }

    @Test func mutationOnMissingPrayerSetsError() async throws {
        try await withModel(title: "mutationOnMissingPrayerSetsError") { model, database, id in
            try PrayerStore(database: database).delete(id: id)
            model.markAnswered()
            #expect(model.errorMessage != nil)
            #expect(!model.isMutating)
        }
    }

    @Test func tagsLoadForPrayer() async throws {
        try await withModel(title: "tagsLoadForPrayer", tagNames: ["tagsLoadForPrayer"]) { model, _, _ in
            #expect(model.tags.map(\.name) == ["tagsLoadForPrayer"])
        }
    }

    @Test func tagTextContrast() {
        #expect(PrayerDetailModel.prefersDarkText(onTagHex: "#FFFF00"))
        #expect(!PrayerDetailModel.prefersDarkText(onTagHex: "#000080"))
    }
}
