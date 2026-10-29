import Foundation
import Testing
import PrayerKit
@testable import PrayerApp

@Suite struct AppSettingsTests {
    private let preferences = AppPreferences(defaults: UserDefaults(suiteName: "AppSettingsTests-\(UUID().uuidString)")!)

    @Test func reminderTimeWritesHourAndMinute() {
        let time = Calendar.current.date(bySettingHour: 7, minute: 30, second: 0, of: .now)!
        preferences.defaultReminderTime = time
        #expect(preferences.defaultNotifyHour == 7)
        #expect(preferences.defaultNotifyMinute == 30)
    }

    @Test func reminderTimeReadsStoredHourAndMinute() {
        preferences.defaultNotifyHour = 21
        preferences.defaultNotifyMinute = 45
        let parts = Calendar.current.dateComponents([.hour, .minute], from: preferences.defaultReminderTime)
        #expect(parts.hour == 21)
        #expect(parts.minute == 45)
    }
}
