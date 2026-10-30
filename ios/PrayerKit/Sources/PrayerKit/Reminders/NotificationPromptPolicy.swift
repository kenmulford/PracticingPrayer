import UserNotifications

/// Decides whether the app asks for notification permission by itself. An in-place upgrade from 1.7.2 can leave
/// reminders in the database and the permission undetermined; `isPresentationClear` keeps the system alert from
/// landing on top of onboarding, an import sheet or the Prayer Time alert.
public enum NotificationPromptPolicy {
    public static func shouldRequest(
        status: UNAuthorizationStatus, allowNotifications: Bool, hasReminder: @autoclosure () -> Bool,
        isPresentationClear: Bool
    ) -> Bool {
        // hasReminder reads the prayer list, so it runs only after the cheap checks pass.
        status == .notDetermined && allowNotifications && isPresentationClear && hasReminder()
    }

    /// Same prayer set the scheduler reads, so a reminder counts only when `reconcile` would schedule it.
    public static func hasReminder(in database: PrayerDatabase) -> Bool {
        let prayers = (try? PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: true)) ?? []
        return prayers.contains { $0.record.canNotify }
    }
}
