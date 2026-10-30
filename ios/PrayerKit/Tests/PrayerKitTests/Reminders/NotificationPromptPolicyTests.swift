import Testing
import UserNotifications
@testable import PrayerKit

struct NotificationPromptPolicyTests {
    private func decide(
        status: UNAuthorizationStatus = .notDetermined, allow: Bool = true, hasReminder: Bool = true, clear: Bool = true
    ) -> Bool {
        NotificationPromptPolicy.shouldRequest(
            status: status, allowNotifications: allow, hasReminder: hasReminder, isPresentationClear: clear)
    }

    @Test func requestsWhenUndeterminedEnabledWithReminderAndClear() {
        #expect(decide())
    }

    @Test(arguments: [UNAuthorizationStatus.authorized, .denied, .provisional])
    func skipsOnceTheUserHasAnswered(status: UNAuthorizationStatus) {
        #expect(!decide(status: status))
    }

    @Test func skipsWhenNotificationsAreOffInPreferences() {
        #expect(!decide(allow: false))
    }

    @Test func skipsWithoutAnyReminder() {
        #expect(!decide(hasReminder: false))
    }

    @Test func waitsWhileAnotherPresentationIsUp() {
        #expect(!decide(clear: false))
    }

    @Test func readsRemindersOnlyWhenTheCheapChecksPass() {
        var reads = 0
        let counted: () -> Bool = { reads += 1; return true }
        _ = NotificationPromptPolicy.shouldRequest(
            status: .authorized, allowNotifications: true, hasReminder: counted(), isPresentationClear: true)
        _ = NotificationPromptPolicy.shouldRequest(
            status: .notDetermined, allowNotifications: false, hasReminder: counted(), isPresentationClear: true)
        #expect(reads == 0)
        _ = NotificationPromptPolicy.shouldRequest(
            status: .notDetermined, allowNotifications: true, hasReminder: counted(), isPresentationClear: true)
        #expect(reads == 1)
    }
}
