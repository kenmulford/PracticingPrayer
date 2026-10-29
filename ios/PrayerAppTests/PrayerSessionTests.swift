import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withSession(
    titles: [String],
    _ body: (PrayerSession, PrayerDatabase, [Int64]) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("PrayerSessionTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let store = PrayerStore(database: database)
    var ids: [Int64] = []
    for title in titles {
        ids.append(try store.create(PrayerDraft(prayerCardId: 2, title: title, tagIds: []), now: .now))
    }
    let items = try store.prayers(segment: .active, isSessionUnlocked: false)
        .filter { ids.contains($0.record.id) }
        .sorted { ids.firstIndex(of: $0.record.id)! < ids.firstIndex(of: $1.record.id)! }
    let session = try #require(PrayerSession(items: items, database: database))
    try await body(session, database, ids)
}

private struct WriteFailure: Error {}

@MainActor
@Suite struct PrayerSessionTests {
    @Test func emptyListIsNil() async throws {
        try await withSession(titles: ["emptyListIsNil"]) { _, database, _ in
            #expect(PrayerSession(items: [], database: database) == nil)
        }
    }

    @Test func nextRecordsPrayedForLeftPage() async throws {
        try await withSession(titles: ["nextA", "nextB"]) { session, database, ids in
            session.next()
            try #expect(database.interactionCount(prayerId: ids[0]) == 1)
            try #expect(database.interactionCount(prayerId: ids[1]) == 0)
            #expect(session.currentIndex == 1)
        }
    }

    @Test func nextOnLastCompletes() async throws {
        try await withSession(titles: ["lastA", "lastB"]) { session, database, ids in
            session.next()
            session.next()
            #expect(session.isComplete)
            try #expect(database.interactionCount(prayerId: ids[1]) == 1)
            session.next()
            try #expect(database.interactionCount(prayerId: ids[0]) == 1)
            try #expect(database.interactionCount(prayerId: ids[1]) == 1)
            #expect(session.currentIndex == 2)
        }
    }

    @Test func previousWritesNothing() async throws {
        try await withSession(titles: ["prevA", "prevB"]) { session, database, ids in
            session.next()
            session.previous()
            #expect(session.currentIndex == 0)
            try #expect(database.interactionCount(prayerId: ids[1]) == 0)
            session.previous()
            #expect(session.currentIndex == 0)
        }
    }

    @Test func swipeMoveMatchesButtons() async throws {
        try await withSession(titles: ["swipeA", "swipeB"]) { session, database, ids in
            session.move(to: 1)
            try #expect(database.interactionCount(prayerId: ids[0]) == 1)
            session.move(to: 0)
            try #expect(database.interactionCount(prayerId: ids[1]) == 0)
            #expect(session.currentIndex == 0)
        }
    }

    @Test func markAnsweredAnswersAndAdvances() async throws {
        try await withSession(titles: ["answerA", "answerB"]) { session, database, ids in
            let first = session.items[0]
            session.markAnswered()
            let answered = try PrayerStore(database: database)
                .prayers(segment: .answered, isSessionUnlocked: false)
            #expect(answered.contains { $0.record.id == ids[0] })
            try #expect(database.interactionCount(prayerId: ids[0]) == 1)
            #expect(session.currentIndex == 1)
            #expect(session.isAnswered(first))
        }
    }

    @Test func failedWritesStillAdvance() async throws {
        let items = try await {
            var result: [PrayerListItem] = []
            try await withSession(titles: ["failA", "failB"]) { session, _, _ in result = session.items }
            return result
        }()
        let session = try #require(PrayerSession(
            items: items, recordPrayed: { _ in throw WriteFailure() }, markAnswered: { _ in throw WriteFailure() }))
        session.next()
        session.markAnswered()
        #expect(session.currentIndex == 2)
    }

    @Test func autoIntervalNormalizes() {
        for value in [30, 60, 120] { #expect(PrayerSession.autoInterval(stored: value) == value) }
        for value in [0, 45, 300] { #expect(PrayerSession.autoInterval(stored: value) == 30) }
    }
}
