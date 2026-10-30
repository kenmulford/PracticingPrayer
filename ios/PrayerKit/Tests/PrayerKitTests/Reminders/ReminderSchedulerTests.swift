import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase) async throws -> T) async throws -> T {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_reminders_\(UUID().uuidString).db").path
    try FileManager.default.copyItem(at: source, to: URL(fileURLWithPath: path))
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try await body(database)
}

private func withPreferences<T>(_ body: (AppPreferences) async throws -> T) async rethrows -> T {
    let name = "ReminderSchedulerTests.\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: name)!
    defer { defaults.removePersistentDomain(forName: name) }
    return try await body(AppPreferences(defaults: defaults))
}

private final class FakeReminderCenter: ReminderCenter, @unchecked Sendable {
    private let lock = NSLock()
    private var storage: [ReminderRequest] = []
    var failingIdentifiers: Set<String> = []

    var pending: [ReminderRequest] { lock.withLock { storage } }

    func seed(_ request: ReminderRequest) { lock.withLock { storage.append(request) } }

    func removeAllPendingRequests() async { lock.withLock { storage.removeAll() } }

    func add(_ request: ReminderRequest) async throws {
        struct Rejected: Error {}
        if failingIdentifiers.contains(request.identifier) { throw Rejected() }
        lock.withLock { storage.removeAll { $0.identifier == request.identifier }; storage.append(request) }
    }
}

private let calendar: Calendar = {
    var c = Calendar(identifier: .gregorian)
    c.timeZone = .current
    return c
}()

private func date(_ y: Int, _ m: Int, _ d: Int, _ h: Int = 0, _ min: Int = 0) -> Date {
    calendar.date(from: DateComponents(year: y, month: m, day: d, hour: h, minute: min))!
}

private func record(
    id: Int64 = 1, title: String = "Heal", canNotify: Bool = true, frequency: PrayerFrequency,
    answered: Bool = false, hour: Int? = nil, minute: Int? = nil, dayOfWeek: Int? = nil,
    dayOfMonth: Int? = nil, notifyDate: Date? = nil, createdAt: Date = date(2026, 9, 1, 8)
) -> PrayerRequestRecord {
    PrayerRequestRecord(
        id: id, prayerCardId: 1, title: title, details: nil, canNotify: canNotify,
        prayerFrequency: frequency, isAnswered: answered, answeredAtTicks: nil,
        notifyHour: hour, notifyMinute: minute, notifyDayOfWeek: dayOfWeek,
        notifyDayOfMonth: dayOfMonth, notifyDateTicks: notifyDate?.ticks, isImported: false,
        createdAtTicks: createdAt.ticks, updatedAtTicks: createdAt.ticks)
}

private func request(_ r: PrayerRequestRecord, hour: Int = 9, minute: Int = 0, now: Date) throws -> ReminderRequest? {
    try ReminderTrigger.request(for: r, hour: hour, minute: minute, now: now, calendar: calendar)
}

private func insertNotifying(_ database: PrayerDatabase, count: Int) throws {
    let ticks = date(2025, 1, 1).ticks
    try database.dbQueue.write { db in
        for n in 1...count {
            try db.execute(
                sql: """
                    INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency,
                        NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate,
                        IsImported, IsAnswered, AnsweredAt, CreatedAt, UpdatedAt)
                    VALUES (1, ?, NULL, 1, 1, 9, 0, -1, -1, NULL, 0, 0, NULL, ?, ?)
                    """,
                arguments: ["Prayer \(n)", ticks, ticks])
        }
    }
}

private func tagRows(_ database: PrayerDatabase, tag: Int64) throws -> [Int64] {
    try database.dbQueue.read {
        try Int64.fetchAll($0, sql: "SELECT PrayerRequestId FROM PrayerCardTag WHERE PrayerTagId = ? ORDER BY PrayerRequestId", arguments: [tag])
    }
}

private func scheduler(
    _ database: PrayerDatabase, _ preferences: AppPreferences, _ center: FakeReminderCenter
) -> ReminderScheduler {
    ReminderScheduler(database: database, preferences: preferences, center: center, calendar: calendar, log: { _ in })
}

@Suite struct ReminderSchedulerTests {
    @Test func oneTimeFiresAtNotifyDatePlusTime() throws {
        let r = record(frequency: .oneTime, hour: 7, minute: 30, notifyDate: date(2026, 11, 5))
        let result = try #require(try request(r, hour: 7, minute: 30, now: date(2026, 10, 28)))
        #expect(result.dateComponents == DateComponents(year: 2026, month: 11, day: 5, hour: 7, minute: 30))
        #expect(result.repeats == false)
        #expect(result.identifier == "1")
        #expect(result.title == "Practicing Prayer")
        #expect(result.body == "Heal")
    }

    @Test func oneTimePastFireSchedulesNothing() throws {
        let r = record(frequency: .oneTime, notifyDate: date(2026, 10, 1))
        #expect(try request(r, now: date(2026, 10, 28)) == nil)
    }

    @Test func dailyAndWeeklyRepeatOnCalendar() throws {
        let daily = try #require(try request(record(frequency: .daily), hour: 8, minute: 15, now: date(2026, 10, 28)))
        #expect(daily.dateComponents == DateComponents(hour: 8, minute: 15))
        #expect(daily.repeats)
        let weekly = try #require(try request(record(frequency: .weekly, dayOfWeek: 3), now: date(2026, 10, 28)))
        #expect(weekly.dateComponents == DateComponents(hour: 9, minute: 0, weekday: 4))
        #expect(weekly.repeats)
    }

    @Test func weeklyWithoutDayUsesNextOccurrence() throws {
        // 2026-10-27 is a Tuesday; 09:00 has passed at 10:00, so the next occurrence is Wednesday.
        let result = try #require(try request(record(frequency: .weekly, dayOfWeek: -1), now: date(2026, 10, 27, 10)))
        #expect(result.dateComponents == DateComponents(hour: 9, minute: 0, weekday: 4))
        let before = try #require(try request(record(frequency: .weekly, dayOfWeek: nil), now: date(2026, 10, 27, 8)))
        #expect(before.dateComponents.weekday == 3)
    }

    @Test func monthlyWithoutDayUsesCreatedAtDay() throws {
        let r = record(id: 7, frequency: .monthly, dayOfMonth: 0, createdAt: date(2026, 9, 17, 8))
        let result = try #require(try request(r, now: date(2026, 10, 28)))
        #expect(result.identifier == "monthly_7")
        #expect(result.dateComponents == DateComponents(day: 17, hour: 9, minute: 0))
        #expect(result.repeats)
        let explicit = try #require(try request(record(frequency: .monthly, dayOfMonth: 5), now: date(2026, 10, 28)))
        #expect(explicit.dateComponents.day == 5)
    }

    @Test func yearlyFiresAtNextAnniversary() throws {
        let r = record(frequency: .yearly, notifyDate: date(2020, 6, 10))
        let result = try #require(try request(r, now: date(2026, 7, 1)))
        #expect(result.dateComponents == DateComponents(year: 2027, month: 6, day: 10, hour: 9, minute: 0))
        #expect(result.repeats == false)
    }

    @Test func yearlyFeb29MapsToFeb28() throws {
        let r = record(frequency: .yearly, notifyDate: date(2024, 2, 29))
        let result = try #require(try request(r, now: date(2026, 1, 1)))
        #expect(result.dateComponents == DateComponents(year: 2026, month: 2, day: 28, hour: 9, minute: 0))
    }

    @Test func yearlyWithoutDateAnchorsOnFirstFire() throws {
        // Created after 09:00 on Mar 10, so the first fire is Mar 11 and the anniversary follows it.
        let r = record(frequency: .yearly, createdAt: date(2026, 3, 10, 20))
        let result = try #require(try request(r, now: date(2026, 3, 12)))
        #expect(result.dateComponents == DateComponents(year: 2027, month: 3, day: 11, hour: 9, minute: 0))
    }

    @Test func reconcileOnLongUpgradedSchedulesMonthly7() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                let center = FakeReminderCenter()
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                let pending = center.pending
                #expect(pending.count == 1)
                let only = try #require(pending.first)
                #expect(only.identifier == "monthly_7")
                #expect(only.dateComponents == DateComponents(day: 1, hour: 9, minute: 0))
                #expect(only.repeats)
            }
        }
    }

    @Test func pendingIdentifiersMatchReconcileWithoutSideEffects() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                let center = FakeReminderCenter()
                let s = scheduler(database, preferences, center)
                let now = date(2025, 3, 1, 12)
                let derived = try await s.pendingIdentifiers(now: now)
                #expect(preferences.nativeRemindersMigrated == false)
                #expect(center.pending.isEmpty)
                try await s.reconcile(now: now)
                #expect(derived == Set(center.pending.map(\.identifier)))
                #expect(derived == ["monthly_7"])
            }
        }
    }

    @Test func reconcileTwiceYieldsIdenticalPendingSet() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                let center = FakeReminderCenter()
                let s = scheduler(database, preferences, center)
                try await s.reconcile(now: date(2025, 3, 1, 12))
                let first = center.pending
                try await s.reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending == first)
            }
        }
    }

    @Test func reconcileWipesLegacyPendingFirst() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                let center = FakeReminderCenter()
                center.seed(ReminderRequest(identifier: "legacy", title: "t", body: "b", dateComponents: DateComponents(hour: 1), repeats: true))
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending.contains { $0.identifier == "legacy" } == false)
            }
        }
    }

    @Test func firstLaunchWipeRunsWhenNotificationsDisallowed() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                preferences.allowNotifications = false
                let center = FakeReminderCenter()
                center.seed(ReminderRequest(identifier: "legacy", title: "t", body: "b", dateComponents: DateComponents(hour: 1), repeats: true))
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending.isEmpty)
                #expect(preferences.nativeRemindersMigrated)
            }
        }
    }

    @Test func disallowedStillRecomputesTag() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                preferences.allowNotifications = false
                try await scheduler(database, preferences, FakeReminderCenter()).reconcile(now: date(2025, 3, 1, 12))
                #expect(try tagRows(database, tag: 6) == [7])
            }
        }
    }

    @Test func recentlyNotifiedOutsideWindowClears() async throws {
        try await withCopy("long-upgraded.db") { database in
            try await withPreferences { preferences in
                let s = scheduler(database, preferences, FakeReminderCenter())
                try await s.reconcile(now: date(2025, 3, 1, 12))
                #expect(try tagRows(database, tag: 6) == [7])
                try await s.reconcile(now: date(2025, 3, 5, 12))
                #expect(try tagRows(database, tag: 6).isEmpty)
            }
        }
    }

    @Test func oneFailureDoesNotStopOthers() async throws {
        try await withCopy("fresh-install.db") { database in
            try await withPreferences { preferences in
                try insertNotifying(database, count: 2)
                let ids = try await database.dbQueue.read { try Int64.fetchAll($0, sql: "SELECT Id FROM PrayerRequest ORDER BY Id") }
                let center = FakeReminderCenter()
                center.failingIdentifiers = ["\(ids[0])"]
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending.map(\.identifier) == ["\(ids[1])"])
            }
        }
    }

    @Test func noNotifyingPrayersLeavesNothing() async throws {
        try await withCopy("fresh-install.db") { database in
            try await withPreferences { preferences in
                try await database.dbQueue.write {
                    try $0.execute(
                        sql: "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt) VALUES (0, 1, 99, ?)",
                        arguments: [date(2025, 1, 1).ticks])
                }
                let center = FakeReminderCenter()
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending.isEmpty)
                #expect(try tagRows(database, tag: 1).isEmpty)
            }
        }
    }

    @Test func answeredAndNonNotifyingNeverTagged() throws {
        let created = date(2025, 1, 1)
        let records = [
            record(id: 1, frequency: .daily, createdAt: created),
            record(id: 2, canNotify: false, frequency: .daily, createdAt: created),
            record(id: 3, frequency: .daily, answered: true, createdAt: created),
        ]
        let ids = try ReminderTrigger.recentlyNotifiedIds(
            records, defaultHour: 9, defaultMinute: 0, now: date(2025, 3, 1, 12), calendar: calendar)
        #expect(ids == [1])
    }

    @Test func missingTimeUsesPreferenceDefault() async throws {
        try await withCopy("fresh-install.db") { database in
            try await withPreferences { preferences in
                preferences.defaultNotifyHour = 6
                preferences.defaultNotifyMinute = 45
                try insertNotifying(database, count: 1)
                try await database.dbQueue.write {
                    try $0.execute(sql: "UPDATE PrayerRequest SET NotifyHour = NULL, NotifyMinute = NULL")
                }
                let center = FakeReminderCenter()
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                #expect(center.pending.first?.dateComponents == DateComponents(hour: 6, minute: 45))
            }
        }
    }

    private func scheduledBody(setup: String? = nil) async throws -> String? {
        try await withCopy("fresh-install.db") { database in
            try await withPreferences { preferences in
                try insertNotifying(database, count: 1)
                if let setup { try await database.dbQueue.write { try $0.execute(sql: setup) } }
                let center = FakeReminderCenter()
                try await scheduler(database, preferences, center).reconcile(now: date(2025, 3, 1, 12))
                return center.pending.first?.body
            }
        }
    }

    @Test func lockedCardUsesGenericBody() async throws {
        let body = try await scheduledBody(setup: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 1")
        #expect(body == ReminderScheduler.protectedBody)
    }

    @Test func hiddenCardUsesGenericBody() async throws {
        let body = try await scheduledBody(setup: "UPDATE PrayerCard SET ProtectionMode = 2 WHERE Id = 1")
        #expect(body == ReminderScheduler.protectedBody)
    }

    @Test func unprotectedCardInProtectAllBoxUsesGenericBody() async throws {
        let body = try await scheduledBody(
            setup: "UPDATE CardBox SET ProtectAllCards = 1, CardProtectionMode = 1 WHERE Id = 1")
        #expect(body == ReminderScheduler.protectedBody)
    }

    @Test func unprotectedCardKeepsTitleBody() async throws {
        let body = try await scheduledBody()
        #expect(body == "Prayer 1")
    }
}
