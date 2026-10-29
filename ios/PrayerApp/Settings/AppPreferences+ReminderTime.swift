import Foundation
import PrayerKit

extension AppPreferences {
    var defaultReminderTime: Date {
        get {
            Calendar.current.date(bySettingHour: defaultNotifyHour, minute: defaultNotifyMinute, second: 0, of: .now) ?? .now
        }
        nonmutating set {
            let parts = Calendar.current.dateComponents([.hour, .minute], from: newValue)
            defaultNotifyHour = parts.hour ?? 9
            defaultNotifyMinute = parts.minute ?? 0
        }
    }
}
