import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(
    cardId: Int64 = 2, archivedFolderId: Int = 0,
    _ body: (CardDetailModel, PrayerDatabase) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("CardDetailModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "CardDetailModelTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = archivedFolderId
    let model = CardDetailModel(cardId: cardId, database: database, preferences: preferences)
    model.load()
    try await body(model, database)
}

@MainActor
@Suite struct CardDetailModelTests {
    @Test func loadSplitsSections() async throws {
        try await withModel { model, _ in
            #expect(model.activePrayers.count == 1)
            #expect(model.answeredPrayers.count == 1)
            #expect(model.collectionName == "Loose Cards")
        }
    }

    @Test func canPrayIgnoresSearch() async throws {
        try await withModel { model, _ in
            model.searchText = "zzz"
            model.load()
            #expect(model.canPray)
        }
    }

    @Test func searchScopesToCard() async throws {
        try await withModel { model, _ in
            model.searchText = "zzz"
            model.load()
            #expect(model.activePrayers.isEmpty)
            #expect(model.answeredPrayers.isEmpty)
        }
    }

    @Test func toggleFavoriteFlips() async throws {
        try await withModel { model, _ in
            #expect(model.card?.isFavorite == false)
            model.toggleFavorite()
            #expect(model.card?.isFavorite == true)
        }
    }

    @Test func moveTargetsAreUserCollectionsAToZ() async throws {
        try await withModel { model, database in
            let store = CollectionStore(database: database)
            _ = try store.create(name: "Zeta", now: .now)
            let alpha = try store.create(name: "Alpha", now: .now)
            model.load()
            #expect(model.moveTargets.map(\.name) == ["Alpha", "Zeta"])
            model.move(toBox: alpha.id)
            #expect(model.collectionName == "Alpha")
        }
    }

    @Test func archiveThenUnarchive() async throws {
        try await withModel(archivedFolderId: 2) { model, _ in
            #expect(model.archive())
            #expect(model.isArchived)
            model.unarchive()
            #expect(!model.isArchived)
        }
    }

    @Test func failedArchiveKeepsCard() async throws {
        try await withModel(archivedFolderId: 0) { model, _ in
            let before = model.card?.boxId
            #expect(!model.archive())
            #expect(model.errorMessage != nil)
            #expect(model.card?.boxId == before)
        }
    }

    @Test func deleteRemovesCardAndPrayers() async throws {
        try await withModel { model, database in
            #expect(model.prayerCount == 2)
            #expect(model.delete())
            let remaining = try PrayerStore(database: database).prayers(cardId: 2)
            #expect(remaining.isEmpty)
        }
    }

    @Test func markAnsweredMovesPrayer() async throws {
        try await withModel { model, _ in
            let id = try #require(model.activePrayers.first?.id)
            model.markAnswered(prayerId: id)
            #expect(model.activePrayers.isEmpty)
            #expect(model.answeredPrayers.contains { $0.id == id })
        }
    }

    @Test func systemCardFlag() async throws {
        try await withModel(cardId: 4) { model, _ in
            #expect(model.isSystem)
        }
    }
}
