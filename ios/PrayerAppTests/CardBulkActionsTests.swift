import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withActions(_ body: (CardBulkActions, PrayerDatabase) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("CardBulkActionsTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "CardBulkActionsTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = 2
    try await body(CardBulkActions(database: database, preferences: preferences), database)
}

@MainActor
private func boxId(of cardId: Int64, in database: PrayerDatabase) throws -> Int64? {
    let list = try database.cardList(isSessionUnlocked: false)
    return list.sections.lazy.flatMap(\.cards).first { $0.card.id == cardId }?.card.boxId
}

@MainActor
@Suite struct CardBulkActionsTests {
    @Test func moveMovesEverySelectedCard() async throws {
        try await withActions { actions, _ in
            let result = actions.move([2, 4], toBox: 0)
            #expect(result.succeeded == [2, 4])
            #expect(result.errorMessage == nil)
        }
    }

    @Test func archiveMovesCardsToArchived() async throws {
        try await withActions { actions, database in
            let result = actions.archive([2, 3])
            #expect(result.succeeded == [2, 3])
            let boxes = try [2, 3].map { try boxId(of: Int64($0), in: database) }
            #expect(boxes == [2, 2])
        }
    }

    @Test func deleteStopsAtFirstFailure() async throws {
        try await withActions { actions, database in
            let result = actions.delete([2, 999])
            #expect(result.succeeded == [2])
            #expect(result.errorMessage != nil)
            let remaining = try database.cardList(isSessionUnlocked: false).sections.flatMap(\.cards).map(\.card.id)
            #expect(remaining.contains(3))
            #expect(!remaining.contains(2))
        }
    }
}
