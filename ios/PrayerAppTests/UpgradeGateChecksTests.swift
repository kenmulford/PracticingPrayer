#if DEBUG
import Foundation
import Testing
import PrayerKit
@testable import PrayerApp

@Suite struct UpgradeGateChecksTests {
    private let preferences = AppPreferences(defaults: UserDefaults(suiteName: "UpgradeGateChecksTests-\(UUID().uuidString)")!)

    private func seed() {
        preferences.autoModeIntervalSeconds = 45
        preferences.overdueDayThreshold = 14
        preferences.defaultNotifyHour = 7
        preferences.defaultNotifyMinute = 30
        preferences.collectionsBannerDismissed = true
        preferences.prayerTimeLandscape = false
        preferences.shareHandoffPrompted = true
        preferences.onboardingComplete = true
        preferences.onboardingStep = "Complete"
    }

    @Test func remindersParsesSpec() {
        let parsed = UpgradeGateChecks.reminders("7:2:7:30:3::;1:1:::::;2:1:::::;3:1:::::;4:1:::::")
        #expect(parsed.failures.isEmpty)
        #expect(parsed.rows.first == UpgradeGateChecks.Reminder(
            id: 7, frequency: 2, hour: 7, minute: 30, dayOfWeek: 3, dayOfMonth: nil, dateTicks: nil))
    }

    @Test func remindersFailOnUnparsableRowAndWrongCount() {
        let parsed = UpgradeGateChecks.reminders("7:2:7:30:3::;bad;1:1:::::")
        #expect(parsed.failures == ["reminder row unparsable: bad", "reminder count expected 5 actual 3"])
    }

    @Test func reminderFailuresNamesChangedField() {
        let expected = UpgradeGateChecks.reminders("7:2:7:30:3::;8:1:7:30:::").rows
        let actual = UpgradeGateChecks.reminders("7:2:9:30:3::").rows
        let failures = UpgradeGateChecks.reminderFailures(expected: expected, actual: actual)
        #expect(failures == ["reminder 7 expected 7:2:7:30:3:: actual 7:2:9:30:3::", "reminder 8 missing"])
    }

    @Test func settingFailuresEmptyWhenSeeded() {
        seed()
        #expect(UpgradeGateChecks.settingFailures(preferences).isEmpty)
    }

    @Test func settingFailuresNamesChangedKey() {
        seed()
        preferences.autoModeIntervalSeconds = 30
        #expect(UpgradeGateChecks.settingFailures(preferences) == ["AutoModeIntervalSeconds expected 45 actual 30"])
    }

    @Test func identifierDiffNilWhenEqual() {
        #expect(UpgradeGateChecks.identifierDiff(expected: ["5", "monthly_6"], actual: ["5", "monthly_6"]) == nil)
    }

    @Test func identifierDiffNamesMissingAndExtra() {
        #expect(UpgradeGateChecks.identifierDiff(expected: ["5", "monthly_6"], actual: ["5", "9"])
            == "pending identifiers missing [monthly_6] extra [9]")
    }
}
#endif
