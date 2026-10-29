import PrayerKit
import SwiftUI
import UIKit
import UserNotifications

struct AppSettingsView: View {
    private let preferences: AppPreferences

    @State private var allowNotifications: Bool
    @State private var reminderTime: Date
    @State private var overdueDays: Int
    @State private var allowLandscape: Bool
    @State private var authorization: UNAuthorizationStatus = .notDetermined

    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.openURL) private var openURL

    init(preferences: AppPreferences) {
        self.preferences = preferences
        self.allowNotifications = preferences.allowNotifications
        self.reminderTime = preferences.defaultReminderTime
        self.overdueDays = preferences.overdueDayThreshold
        self.allowLandscape = preferences.prayerTimeLandscape
    }

    var body: some View {
        Form {
            Section {
                Toggle("Allow Notifications", isOn: $allowNotifications)
                    .accessibilityIdentifier("appSettings.allowNotifications")
                if allowNotifications {
                    DatePicker("Default Reminder Time", selection: $reminderTime, displayedComponents: .hourAndMinute)
                        .accessibilityIdentifier("appSettings.reminderTime")
                }
            } header: {
                Text("Notifications").accessibilityAddTraits(.isHeader)
            } footer: {
                notificationsFooter
            }

            Section {
                Stepper(value: $overdueDays, in: 1...365) {
                    Text("Overdue After \(overdueDays) Days").monospacedDigit()
                }
            } header: {
                Text("Overdue").accessibilityAddTraits(.isHeader)
            } footer: {
                Text("Prayers not prayed within this many days appear in your overdue list on the home page.")
            }

            Section {
                Toggle("Allow Landscape", isOn: $allowLandscape)
                    .accessibilityIdentifier("appSettings.allowLandscape")
            } header: {
                Text("Prayer Time").accessibilityAddTraits(.isHeader)
            }

            #if DEBUG
            Section {
                DeveloperSettingsRows()
            } header: {
                Text("Developer").accessibilityAddTraits(.isHeader)
            }
            #endif
        }
        .scrollContentBackground(.hidden)
        .frame(maxWidth: 700)
        .frame(maxWidth: .infinity)
        .background(Color(.systemGroupedBackground))
        .navigationTitle("App Settings")
        .navigationBarTitleDisplayMode(.inline)
        .task { authorization = await NotificationAuthorization.status() }
        .onChange(of: scenePhase) { _, phase in
            guard phase == .active else { return }
            Task { authorization = await NotificationAuthorization.status() }
        }
        .onChange(of: allowNotifications) { _, isOn in
            if isOn {
                Task {
                    let granted = await NotificationAuthorization.request()
                    authorization = await NotificationAuthorization.status()
                    preferences.allowNotifications = granted
                    if !granted { allowNotifications = false }
                }
            } else {
                preferences.allowNotifications = false
                NotificationAuthorization.clearAll()
            }
        }
        .onChange(of: reminderTime) { _, time in preferences.defaultReminderTime = time }
        .onChange(of: overdueDays) { _, days in preferences.overdueDayThreshold = days }
        .onChange(of: allowLandscape) { _, isOn in preferences.prayerTimeLandscape = isOn }
    }

    @ViewBuilder
    private var notificationsFooter: some View {
        if authorization == .denied {
            VStack(alignment: .leading, spacing: 8) {
                Text("Notifications are turned off in Settings.")
                Button("Open Settings") {
                    if let url = URL(string: UIApplication.openNotificationSettingsURLString) {
                        openURL(url)
                    }
                }
                .accessibilityIdentifier("appSettings.openSettings")
            }
        } else if allowNotifications {
            Text("New reminders start at this time.")
        }
    }
}
