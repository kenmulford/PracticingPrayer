import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withDatabase(
    archivedFolderId: Int = 0,
    _ body: (PrayerDatabase, AppPreferences) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("CardRowActionModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "CardRowActionModelTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = archivedFolderId
    try await body(database, preferences)
}

private func model(
    favorite: Bool = false, archived: Bool = false, system: Bool = false, masked: Bool = false, count: Int = 0
) -> CardRowActionModel {
    CardRowActionModel(
        title: "Card", isFavorite: favorite, isArchived: archived, isSystem: system, isMasked: masked,
        activePrayerCount: count)
}

@MainActor
@Suite struct CardRowActionModelTests {
    @Test func activeCardActions() {
        let m = model()
        #expect(m.menuActions == [.pray, .share, .favorite, .edit, .moveTo, .archive, .delete])
        #expect(m.leadingActions == [.favorite])
        #expect(m.trailingActions == [.delete, .archive])
    }

    @Test func favoritedArchivedCardActions() {
        let m = model(favorite: true, archived: true)
        #expect(m.menuActions == [.pray, .share, .unfavorite, .edit, .moveTo, .unarchive, .delete])
        #expect(m.leadingActions == [.unfavorite])
        #expect(m.trailingActions == [.delete, .unarchive])
    }

    @Test func prayDisabledWithoutActivePrayers() {
        #expect(!model(count: 0).canPray)
        #expect(model(count: 1).canPray)
    }

    @Test func systemCardHasNoActions() {
        let m = model(system: true)
        #expect(m.menuActions.isEmpty && m.leadingActions.isEmpty && m.trailingActions.isEmpty)
    }

    @Test func maskedCardHasNoActions() {
        let m = model(masked: true)
        #expect(m.menuActions.isEmpty && m.leadingActions.isEmpty && m.trailingActions.isEmpty)
    }

    @Test func deleteMessageCounts() {
        #expect(model(count: 0).deleteMessage == "Delete “Card”?")
        #expect(model(count: 1).deleteMessage == "Delete “Card” and its 1 prayer request?")
        #expect(model(count: 2).deleteMessage == "Delete “Card” and its 2 prayer requests?")
    }

    @Test func performFavoritePersists() async throws {
        try await withDatabase { database, preferences in
            #expect(CardRowActionModel.perform(.favorite, cardId: 2, database: database, preferences: preferences) == nil)
            let fresh = CardDetailModel(cardId: 2, database: database, preferences: preferences)
            fresh.load()
            #expect(fresh.card?.isFavorite == true)
        }
    }

    @Test func archiveThenUnarchiveRestoresBox() async throws {
        try await withDatabase(archivedFolderId: 2) { database, preferences in
            _ = CardRowActionModel.perform(.archive, cardId: 2, database: database, preferences: preferences)
            let archived = CardDetailModel(cardId: 2, database: database, preferences: preferences)
            archived.load()
            #expect(archived.card?.boxId == 2)
            _ = CardRowActionModel.perform(.unarchive, cardId: 2, database: database, preferences: preferences)
            let restored = CardDetailModel(cardId: 2, database: database, preferences: preferences)
            restored.load()
            #expect(restored.isArchived == false)
            #expect(restored.collectionName == "Loose Cards")
        }
    }

    @Test func performFailureReturnsMessage() async throws {
        try await withDatabase { database, preferences in
            #expect(CardRowActionModel.perform(.delete, cardId: 999_999, database: database, preferences: preferences) != nil)
        }
    }

    @Test func previewTitlesAreActiveOnly() async throws {
        try await withDatabase { database, preferences in
            #expect(CardRowActionModel.previewTitles(cardId: 2, database: database, preferences: preferences).count == 1)
        }
    }
}
