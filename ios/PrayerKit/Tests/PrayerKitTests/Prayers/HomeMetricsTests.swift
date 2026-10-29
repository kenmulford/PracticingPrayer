import Foundation
import Testing
import GRDB
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func withStore(_ body: (UserDefaults, AppPreferences) throws -> Void) rethrows {
    let name = "HomeMetricsTests.\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: name)!
    defer { defaults.removePersistentDomain(forName: name) }
    try body(defaults, AppPreferences(defaults: defaults))
}

private func withDatabase(_ file: String, _ body: (PrayerDatabase) throws -> Void) throws {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_home_\(UUID().uuidString).db").path
    try FileManager.default.copyItem(atPath: source.path, toPath: path)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    try body(database)
}

private func local(_ year: Int, _ month: Int, _ day: Int, _ hour: Int = 0, _ minute: Int = 0) -> Date {
    Calendar.current.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))!
}

private func ids(_ records: [PrayerRequestRecord]) -> [Int64] { records.map(\.id) }

@Suite struct HomeMetricsTests {
    @Test func recordPrayedInsertsOnePrayedRow() throws {
        try withDatabase("long-upgraded.db") { database in
            let now = local(2025, 2, 1, 8, 30)
            let id = try PrayerInteractionRecorder(database: database).recordPrayed(prayerId: 1, now: now)
            #expect(id == 4)
            let row = try database.dbQueue.read { db in
                try Row.fetchOne(db, sql: "SELECT * FROM PrayerInteraction WHERE Id = 4")
            }
            #expect(row?["PrayerId"] as Int64? == 1)
            #expect(row?["InteractionType"] as String? == "Prayed")
            #expect(row?["InteractionAt"] as Int64? == now.ticks)
            #expect(row?["CreatedAt"] as Int64? == now.ticks)
            #expect(row?["UpdatedAt"] as Int64? == now.ticks)
        }
    }

    @Test func interactionCountCountsRows() throws {
        try withDatabase("long-upgraded.db") { database in
            let r1 = try (database.interactionCount(prayerId: 5) == 1)
            #expect(r1)
            let r2 = try (database.interactionCount(prayerId: 1) == 0)
            #expect(r2)
        }
    }

    @Test func overdueUsesDefaultThreshold() throws {
        try withStore { _, preferences in
            try withDatabase("long-upgraded.db") { database in
                let metrics = try database.homeMetrics(
                    now: local(2025, 2, 15, 12), overdueDayThreshold: preferences.overdueDayThreshold,
                    isSessionUnlocked: true)
                #expect(ids(metrics.overdue) == [5, 7])
            }
        }
    }

    @Test func latestExactlyAtCutoffIsNotOverdue() throws {
        try withDatabase("long-upgraded.db") { database in
            let now = local(2025, 1, 31)
            let r3 = try (database.overduePrayers(now: now, overdueDayThreshold: 30, isSessionUnlocked: true).isEmpty)
            #expect(r3)
            let r4 = try (ids(database.overduePrayers(now: now.addingTimeInterval(1), overdueDayThreshold: 30, isSessionUnlocked: true)) == [5, 7])
            #expect(r4)
        }
    }

    @Test func overdueOrdersOldestLatestFirst() throws {
        try withDatabase("long-upgraded.db") { database in
            try PrayerInteractionRecorder(database: database).recordPrayed(prayerId: 5, now: local(2025, 1, 10))
            let r5 = try (ids(database.overduePrayers(now: local(2025, 3, 1), isSessionUnlocked: true)) == [7, 5])
            #expect(r5)
        }
    }

    @Test func activeCardsAndUnanswered() throws {
        try withDatabase("long-upgraded.db") { database in
            let metrics = try database.homeMetrics(now: local(2025, 2, 15), isSessionUnlocked: true)
            #expect(metrics.activeCards == 3)
            #expect(metrics.unanswered == 6)
        }
    }

    @Test func lastPrayedIsMaxInteraction() throws {
        try withDatabase("long-upgraded.db") { database in
            let r6 = try (database.lastPrayed() == (Date(ticks: 638712864000000000)))
            #expect(r6)
        }
    }

    @Test func answeredOnThisDayMatchesEarlierYear() throws {
        try withDatabase("long-upgraded.db") { database in
            let r7 = try (ids(database.answeredOnThisDay(now: local(2026, 1, 1, 9), isSessionUnlocked: true)) == [6])
            #expect(r7)
            let r8 = try (database.answeredOnThisDay(now: local(2025, 1, 1, 9), isSessionUnlocked: true).isEmpty)
            #expect(r8)
            let r9 = try (database.answeredOnThisDay(now: local(2026, 1, 2, 9), isSessionUnlocked: true).isEmpty)
            #expect(r9)
        }
    }

    @Test func emptyDatabaseReturnsZeros() throws {
        try withDatabase("fresh-install.db") { database in
            let metrics = try database.homeMetrics(now: local(2026, 1, 1), isSessionUnlocked: true)
            #expect(metrics == HomeMetrics(overdue: [], activeCards: 0, unanswered: 0, lastPrayed: nil, answeredOnThisDay: []))
            let r10 = try (database.interactionCount(prayerId: 1) == 0)
            #expect(r10)
        }
    }

    @Test func readFailureThrows() throws {
        try withDatabase("long-upgraded.db") { database in
            try database.dbQueue.write { db in try db.execute(sql: "DROP TABLE PrayerInteraction") }
            #expect(throws: DatabaseError.self) { try database.homeMetrics(now: local(2026, 1, 1), isSessionUnlocked: true) }
            #expect(throws: DatabaseError.self) { try database.interactionCount(prayerId: 1) }
        }
    }

    private func protectCard(ofPrayer id: Int64, in database: PrayerDatabase) throws {
        try database.dbQueue.write { db in
            try db.execute(
                sql: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = (SELECT PrayerCardId FROM PrayerRequest WHERE Id = ?)",
                arguments: [id])
        }
    }

    @Test func lockedAnsweredOnThisDayExcludesProtectedCard() throws {
        try withDatabase("long-upgraded.db") { database in
            try protectCard(ofPrayer: 6, in: database)
            let now = local(2026, 1, 1, 9)
            #expect(try ids(database.answeredOnThisDay(now: now, isSessionUnlocked: false)).isEmpty)
            #expect(try ids(database.answeredOnThisDay(now: now, isSessionUnlocked: true)) == [6])
        }
    }

    @Test func lockedOverdueExcludesProtectedCard() throws {
        try withDatabase("long-upgraded.db") { database in
            try protectCard(ofPrayer: 5, in: database)
            let now = local(2025, 2, 15, 12)
            #expect(try !ids(database.overduePrayers(now: now, isSessionUnlocked: false)).contains(5))
            #expect(try ids(database.overduePrayers(now: now, isSessionUnlocked: true)) == [5, 7])
        }
    }

    @Test func lockedHomeMetricsExcludeProtectedPrayers() throws {
        try withDatabase("long-upgraded.db") { database in
            try protectCard(ofPrayer: 5, in: database)
            let locked = try database.homeMetrics(now: local(2025, 2, 15, 12), isSessionUnlocked: false)
            let unlocked = try database.homeMetrics(now: local(2025, 2, 15, 12), isSessionUnlocked: true)
            #expect(!ids(locked.overdue).contains(5))
            #expect(locked.unanswered < unlocked.unanswered)
        }
    }
}
