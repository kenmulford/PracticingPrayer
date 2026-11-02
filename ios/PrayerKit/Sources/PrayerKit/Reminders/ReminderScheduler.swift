import Foundation
import GRDB
import UserNotifications
import os

public protocol ReminderCenter: Sendable {
    func removeAllPendingRequests() async
    func add(_ request: ReminderRequest) async throws
}

public struct SystemReminderCenter: ReminderCenter {
    public init() {}

    public func removeAllPendingRequests() async {
        UNUserNotificationCenter.current().removeAllPendingNotificationRequests()
    }

    public func add(_ request: ReminderRequest) async throws {
        let content = UNMutableNotificationContent()
        content.title = request.title
        content.body = request.body
        content.sound = .default
        let trigger = UNCalendarNotificationTrigger(dateMatching: request.dateComponents, repeats: request.repeats)
        try await UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: request.identifier, content: content, trigger: trigger))
    }
}

/// Rebuilds the pending local notifications from the database; safe to run on every launch.
public struct ReminderScheduler {
    public static let protectedBody = "Time to pray"

    private let database: PrayerDatabase
    private let preferences: AppPreferences
    private let center: any ReminderCenter
    private let calendar: Calendar
    private let log: (String) -> Void

    public init(
        database: PrayerDatabase, preferences: AppPreferences, center: any ReminderCenter,
        calendar: Calendar = .current, log: @escaping (String) -> Void = ReminderScheduler.logError
    ) {
        self.database = database
        self.preferences = preferences
        self.center = center
        self.calendar = calendar
        self.log = log
    }

    public func reconcile(now: Date) async throws {
        // Runs before the AllowNotifications check so MAUI-era requests never survive the first native launch.
        if !preferences.nativeRemindersMigrated {
            await center.removeAllPendingRequests()
            preferences.nativeRemindersMigrated = true
        }

        // Reminders are not UI, so protected prayers still schedule, with a generic body so the title never shows on the lock screen.
        let records = try PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: true).map(\.record)

        do {
            try recomputeRecentlyNotified(records, now: now)
        } catch {
            log("Recently Notified recompute failed: \(error)")
        }

        guard preferences.allowNotifications else { return }

        let requests = try await scheduledRequests(records, now: now)
        await center.removeAllPendingRequests()
        for request in requests {
            do {
                try await center.add(request)
            } catch {
                log("Scheduling \(request.identifier) failed: \(error)")
            }
        }
    }

    /// Identifiers `reconcile` would leave pending, without touching the center, preferences or database.
    public func pendingIdentifiers(now: Date) async throws -> Set<String> {
        guard preferences.allowNotifications else { return [] }
        let records = try PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: true).map(\.record)
        return Set(try await scheduledRequests(records, now: now).map(\.identifier))
    }

    private func scheduledRequests(_ records: [PrayerRequestRecord], now: Date) async throws -> [ReminderRequest] {
        let protectedCardIds = try await database.dbQueue.read { db in
            ProtectionPolicy.blockedCardIds(
                cards: try PrayerCardRecord.fetchAll(db), boxes: try CardBoxRecord.fetchAll(db), isSessionUnlocked: false)
        }

        var requests: [ReminderRequest] = []
        for record in records where record.canNotify {
            do {
                let hour = record.notifyHour ?? preferences.defaultNotifyHour
                let minute = record.notifyMinute ?? preferences.defaultNotifyMinute
                guard var request = try ReminderTrigger.request(
                    for: record, hour: hour, minute: minute, now: now, calendar: calendar)
                else { continue }
                if protectedCardIds.contains(record.prayerCardId) { request.body = ReminderScheduler.protectedBody }
                requests.append(request)
            } catch {
                log("Scheduling prayer \(record.id) failed: \(error)")
            }
        }
        return requests
    }

    public static func logError(_ message: String) {
        Logger(subsystem: "PrayerKit", category: "Reminders").error("\(message, privacy: .public)")
    }

    private func recomputeRecentlyNotified(_ records: [PrayerRequestRecord], now: Date) throws {
        let ids = try ReminderTrigger.recentlyNotifiedIds(
            records, defaultHour: preferences.defaultNotifyHour, defaultMinute: preferences.defaultNotifyMinute,
            now: now, calendar: calendar)
        try database.dbQueue.write { db in
            guard let tagId = try Int64.fetchOne(
                db, sql: "SELECT Id FROM PrayerTag WHERE LOWER(Name) = LOWER(?)",
                arguments: [TagStore.recentlyNotifiedName])
            else { return }
            try db.execute(sql: "DELETE FROM PrayerCardTag WHERE PrayerTagId = ?", arguments: [tagId])
            for id in ids {
                try db.execute(
                    sql: "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt) VALUES (0, ?, ?, ?)",
                    arguments: [tagId, id, now.ticks])
            }
        }
    }
}
