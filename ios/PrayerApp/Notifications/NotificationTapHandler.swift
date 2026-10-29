import Observation
import PrayerKit
import UserNotifications

@Observable
final class NotificationTapHandler: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotificationTapHandler()

    var hasPendingTap = false

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse
    ) async {
        await MainActor.run { hasPendingTap = true }
    }

    static func recentlyNotifiedPrayers(database: PrayerDatabase) -> [PrayerListItem] {
        guard let id = try? TagStore(database: database).recentlyNotifiedTagId() else { return [] }
        return (try? PrayerStore(database: database)
            .prayers(segment: .active, tagIds: [id], isSessionUnlocked: false)) ?? []
    }
}
