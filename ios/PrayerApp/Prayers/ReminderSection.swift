import PrayerKit
import SwiftUI
import UserNotifications

struct ReminderAuthorization {
    var status: () async -> UNAuthorizationStatus
    var request: () async -> Bool

    static let system = ReminderAuthorization(
        status: { await NotificationAuthorization.status() },
        request: { await NotificationAuthorization.request() })
}

struct ReminderSection: View {
    @Bindable var model: PrayerEditorModel
    @Environment(\.openURL) private var openURL

    var body: some View {
        Section {
            Toggle(
                "Remind Me",
                isOn: Binding(
                    get: { model.remindMe },
                    set: { on in Task { await model.setRemindMe(on) } })
            )
            .disabled(isBusy)
            .accessibilityIdentifier("PrayerEditor_RemindMe")
            if model.remindMe {
                Picker("Repeat", selection: $model.frequency) {
                    Text("Never").tag(PrayerFrequency.oneTime)
                    Text("Daily").tag(PrayerFrequency.daily)
                    Text("Weekly").tag(PrayerFrequency.weekly)
                    Text("Monthly").tag(PrayerFrequency.monthly)
                    Text("Yearly").tag(PrayerFrequency.yearly)
                }
                .pickerStyle(.menu)
                .disabled(isBusy)
                .accessibilityIdentifier("PrayerEditor_Repeat")
                fields
            }
            if model.authorizationDenied {
                Text("Notifications are turned off in Settings.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                Button("Open Settings") {
                    if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
                }
                .accessibilityIdentifier("PrayerEditor_OpenSettings")
            }
        } header: {
            Text("Reminder").accessibilityAddTraits(.isHeader)
        }
    }

    private var isBusy: Bool { model.isSaving || model.isAuthorizing }

    @ViewBuilder
    private var fields: some View {
        switch model.frequency {
        case .oneTime:
            DatePicker(
                "Date", selection: $model.reminderDate, in: model.minimumReminderDate..., displayedComponents: .date
            )
            .disabled(isBusy)
            .accessibilityIdentifier("PrayerEditor_ReminderDate")
        case .yearly:
            DatePicker("Date", selection: $model.reminderDate, displayedComponents: .date)
                .disabled(isBusy)
                .accessibilityIdentifier("PrayerEditor_ReminderDate")
        case .weekly:
            Picker("Day", selection: $model.dayOfWeek) {
                ForEach(Array(Calendar.current.weekdaySymbols.enumerated()), id: \.offset) { index, name in
                    Text(verbatim: name).tag(index)
                }
            }
            .disabled(isBusy)
            .accessibilityIdentifier("PrayerEditor_ReminderDay")
        case .monthly:
            Picker("Day of Month", selection: $model.dayOfMonth) {
                ForEach(1...31, id: \.self) { day in
                    Text(day, format: .number).tag(day)
                }
            }
            .disabled(isBusy)
            .accessibilityIdentifier("PrayerEditor_ReminderDayOfMonth")
        case .daily:
            EmptyView()
        }
        DatePicker("Time", selection: $model.reminderTime, displayedComponents: .hourAndMinute)
            .disabled(isBusy)
            .accessibilityIdentifier("PrayerEditor_ReminderTime")
    }
}
