#if DEBUG
import Foundation
import PrayerKit

/// Pure parsing and comparison for the upgrade gate, split from the view so it is unit-testable.
nonisolated enum UpgradeGateChecks {
    struct Reminder: Equatable {
        let id: Int64
        let frequency: Int64
        let hour: Int?
        let minute: Int?
        let dayOfWeek: Int?
        let dayOfMonth: Int?
        let dateTicks: Int64?

        var spec: String {
            [String(id), String(frequency), hour.map(String.init), minute.map(String.init),
             dayOfWeek.map(String.init), dayOfMonth.map(String.init), dateTicks.map(String.init)]
                .map { $0 ?? "" }.joined(separator: ":")
        }
    }

    static let remindersArgument = "-upgradeGateReminders"

    static func counts(_ argument: String) -> [String: Int] {
        var result: [String: Int] = [:]
        for pair in argument.split(separator: ",") {
            let parts = pair.split(separator: "=")
            if parts.count == 2, let value = Int(parts[1]) { result[String(parts[0])] = value }
        }
        return result
    }

    static let expectedReminderCount = 5

    /// Rows are `Id:Frequency:Hour:Minute:DayOfWeek:DayOfMonth:DateTicks` joined with `;`; an empty field is nil.
    /// An unparsable row or a count other than `expectedReminderCount` is a failure, so a mangled argument cannot shrink the check.
    static func reminders(_ argument: String) -> (rows: [Reminder], failures: [String]) {
        var rows: [Reminder] = []
        var failures: [String] = []
        for row in argument.split(separator: ";") {
            let fields = row.split(separator: ":", omittingEmptySubsequences: false)
            guard fields.count == 7, let id = Int64(fields[0]), let frequency = Int64(fields[1]) else {
                failures.append("reminder row unparsable: \(row)")
                continue
            }
            rows.append(Reminder(
                id: id, frequency: frequency, hour: Int(fields[2]), minute: Int(fields[3]),
                dayOfWeek: Int(fields[4]), dayOfMonth: Int(fields[5]), dateTicks: Int64(fields[6])))
        }
        if rows.count + failures.count != expectedReminderCount {
            failures.append("reminder count expected \(expectedReminderCount) actual \(rows.count + failures.count)")
        }
        return (rows, failures)
    }

    static func reminder(_ record: PrayerRequestRecord) -> Reminder {
        Reminder(
            id: record.id, frequency: record.prayerFrequency.rawValue, hour: record.notifyHour,
            minute: record.notifyMinute, dayOfWeek: record.notifyDayOfWeek,
            dayOfMonth: record.notifyDayOfMonth, dateTicks: record.notifyDateTicks)
    }

    static func reminderFailures(expected: [Reminder], actual: [Reminder]) -> [String] {
        expected.compactMap { want in
            guard let have = actual.first(where: { $0.id == want.id }) else { return "reminder \(want.id) missing" }
            return have == want ? nil : "reminder \(want.id) expected \(want.spec) actual \(have.spec)"
        }
    }

    /// The nine values `TestDataSeed.SeedUpgradeGatePreferencesAsync` writes on the 1.7.2 install.
    static func settingFailures(_ preferences: AppPreferences) -> [String] {
        var failures: [String] = []
        func check<T: Equatable>(_ key: String, _ expected: T, _ actual: T) {
            if expected != actual { failures.append("\(key) expected \(expected) actual \(actual)") }
        }
        check("AutoModeIntervalSeconds", 45, preferences.autoModeIntervalSeconds)
        check("OverdueDayThreshold", 14, preferences.overdueDayThreshold)
        check("DefaultNotifyHour", 7, preferences.defaultNotifyHour)
        check("DefaultNotifyMinute", 30, preferences.defaultNotifyMinute)
        check("CollectionsBannerDismissed", true, preferences.collectionsBannerDismissed)
        check("PrayerTimeLandscape", false, preferences.prayerTimeLandscape)
        check("ShareHandoffPrompted", true, preferences.shareHandoffPrompted)
        check("OnboardingComplete", true, preferences.onboardingComplete)
        check("OnboardingStep", "Complete", preferences.onboardingStep)
        return failures
    }

    static func identifierDiff(expected: Set<String>, actual: Set<String>) -> String? {
        let missing = expected.subtracting(actual).sorted().joined(separator: ",")
        let extra = actual.subtracting(expected).sorted().joined(separator: ",")
        guard !missing.isEmpty || !extra.isEmpty else { return nil }
        return "pending identifiers missing [\(missing)] extra [\(extra)]"
    }

    /// Read-only: the identifiers `ReminderScheduler.reconcile` would leave pending.
    static func derivedIdentifiers(database: PrayerDatabase, preferences: AppPreferences) async throws -> Set<String> {
        try await ReminderScheduler(database: database, preferences: preferences, center: SystemReminderCenter())
            .pendingIdentifiers(now: Date())
    }
}
#endif
