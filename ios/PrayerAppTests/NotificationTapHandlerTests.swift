import Foundation
import GRDB
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withDatabase(_ body: (PrayerDatabase) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("NotificationTapHandlerTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    try await body(database)
}

@MainActor
private func notifiedTagCount(_ database: PrayerDatabase, prayerId: Int64) throws -> Int {
    try database.dbQueue.read { db in
        try Int.fetchOne(
            db,
            sql: """
                SELECT COUNT(*) FROM PrayerCardTag t JOIN PrayerTag g ON g.Id = t.PrayerTagId
                WHERE t.PrayerRequestId = ? AND LOWER(g.Name) = LOWER(?)
                """,
            arguments: [prayerId, TagStore.recentlyNotifiedName]) ?? 0
    }
}

@MainActor
@Suite struct NotificationTapHandlerTests {
    @Test func missingTagYieldsNoPrayers() async throws {
        try await withDatabase { database in
            let tags = TagStore(database: database)
            try database.dbQueue.write { db in
                try db.execute(sql: "DELETE FROM PrayerTag WHERE LOWER(Name) = LOWER(?)",
                               arguments: [TagStore.recentlyNotifiedName])
            }
            #expect(try tags.recentlyNotifiedTagId() == nil)
            #expect(NotificationTapHandler.recentlyNotifiedPrayers(database: database).isEmpty)
        }
    }

    @Test func tagWithNoPrayersYieldsNone() async throws {
        try await withDatabase { database in
            try TagStore(database: database).seedSystemTags(now: .now)
            #expect(try TagStore(database: database).recentlyNotifiedTagId() != nil)
            #expect(NotificationTapHandler.recentlyNotifiedPrayers(database: database).isEmpty)
        }
    }

    @Test func taggedPrayersAreReturned() async throws {
        try await withDatabase { database in
            let tags = TagStore(database: database)
            try tags.seedSystemTags(now: .now)
            let tagId = try #require(try tags.recentlyNotifiedTagId())
            let store = PrayerStore(database: database)
            let a = try store.create(PrayerDraft(prayerCardId: 2, title: "tapA", tagIds: [tagId]), now: .now)
            let b = try store.create(PrayerDraft(prayerCardId: 2, title: "tapB", tagIds: [tagId]), now: .now)
            _ = try store.create(PrayerDraft(prayerCardId: 2, title: "tapUntagged", tagIds: []), now: .now)
            let ids = NotificationTapHandler.recentlyNotifiedPrayers(database: database).map(\.record.id)
            #expect(Set(ids) == [a, b])
            let expected = try store.prayers(segment: .active, tagIds: [tagId], isSessionUnlocked: false)
                .map(\.record.id)
            #expect(ids == expected)
        }
    }

    @Test func nextClearsTagAfterPrayedWrite() async throws {
        try await withDatabase { database in
            let tags = TagStore(database: database)
            try tags.seedSystemTags(now: .now)
            let tagId = try #require(try tags.recentlyNotifiedTagId())
            let store = PrayerStore(database: database)
            let a = try store.create(PrayerDraft(prayerCardId: 2, title: "clearA", tagIds: [tagId]), now: .now)
            let b = try store.create(PrayerDraft(prayerCardId: 2, title: "clearB", tagIds: [tagId]), now: .now)
            let items = NotificationTapHandler.recentlyNotifiedPrayers(database: database)
                .filter { [a, b].contains($0.record.id) }
            let session = try #require(PrayerSession(items: items, database: database))
            let first = items[0].record.id
            let second = items[1].record.id
            session.next()
            try #expect(database.interactionCount(prayerId: first) == 1)
            try #expect(notifiedTagCount(database, prayerId: first) == 0)
            try #expect(notifiedTagCount(database, prayerId: second) == 1)
        }
    }
}
