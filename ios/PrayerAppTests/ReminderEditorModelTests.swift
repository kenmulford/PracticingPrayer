import Foundation
import PrayerKit
import Testing
import UserNotifications
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

final class RecordingReconciler: ReminderReconciling {
    var count = 0
    var error: (any Error)?

    func reconcile(now: Date) async throws {
        count += 1
        if let error { throw error }
    }
}

private struct ReconcileFailure: Error {}

private final class AuthorizationProbe {
    var status: UNAuthorizationStatus
    var grants: Bool
    var requests = 0

    init(status: UNAuthorizationStatus, grants: Bool = true) {
        self.status = status
        self.grants = grants
    }

    var authorization: ReminderAuthorization {
        ReminderAuthorization(
            status: { self.status },
            request: {
                self.requests += 1
                return self.grants
            })
    }
}

private struct Harness {
    let database: PrayerDatabase
    let preferences: AppPreferences
    let reconciler: RecordingReconciler
    let probe: AuthorizationProbe

    func editor(prayerId: Int64? = nil, cardId: Int64? = 2) -> PrayerEditorModel {
        let model = PrayerEditorModel(
            prayerId: prayerId, cardId: cardId, cardTitle: "Card", database: database,
            preferences: preferences, reminders: reconciler, authorization: probe.authorization)
        model.load()
        return model
    }

    func record(titled title: String) throws -> PrayerRequestRecord {
        try #require(
            PrayerStore(database: database).prayers(cardId: 2).first { $0.record.title == title }?.record)
    }
}

@MainActor
private func withHarness(
    status: UNAuthorizationStatus = .authorized, grants: Bool = true, allowNotifications: Bool = true,
    _ body: (Harness) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("ReminderEditorModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    let suite = "ReminderEditorModelTests-\(UUID().uuidString)"
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
        UserDefaults().removePersistentDomain(forName: suite)
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let preferences = AppPreferences(defaults: try #require(UserDefaults(suiteName: suite)))
    preferences.allowNotifications = allowNotifications
    try await body(
        Harness(
            database: database, preferences: preferences, reconciler: RecordingReconciler(),
            probe: AuthorizationProbe(status: status, grants: grants)))
}

private func parts(_ date: Date) -> [Int] {
    let c = Calendar.current.dateComponents([.hour, .minute], from: date)
    return [c.hour ?? -1, c.minute ?? -1]
}

private func time(_ hour: Int, _ minute: Int) -> Date {
    Calendar.current.date(bySettingHour: hour, minute: minute, second: 0, of: .now) ?? .now
}

@MainActor
@Suite struct ReminderEditorModelTests {
    @Test func firstEnableRequestsAndShowsNever() async throws {
        try await withHarness(status: .notDetermined) { h in
            let model = h.editor()
            await model.setRemindMe(true)
            #expect(model.remindMe)
            #expect(model.frequency == .oneTime)
            #expect(h.probe.requests == 1)
        }
    }

    @Test func declinedPromptRevertsOff() async throws {
        try await withHarness(status: .notDetermined, grants: false) { h in
            let model = h.editor()
            await model.setRemindMe(true)
            #expect(!model.remindMe)
            #expect(!model.isAuthorizing)
        }
    }

    @Test func deniedStaysOffAndFlags() async throws {
        try await withHarness(status: .denied) { h in
            let model = h.editor()
            await model.setRemindMe(true)
            #expect(!model.remindMe)
            #expect(model.authorizationDenied)
            #expect(h.probe.requests == 0)
        }
    }

    @Test func allowNotificationsOffSkipsRequest() async throws {
        try await withHarness(status: .notDetermined, allowNotifications: false) { h in
            let model = h.editor()
            await model.setRemindMe(true)
            #expect(model.remindMe)
            #expect(h.probe.requests == 0)
        }
    }

    @Test func newTimeUsesDefault() async throws {
        try await withHarness { h in
            h.preferences.defaultNotifyHour = 18
            h.preferences.defaultNotifyMinute = 30
            #expect(parts(h.editor().reminderTime) == [18, 30])
        }
    }

    @Test func existingLoadsStoredTime() async throws {
        try await withHarness { h in
            let reminder = PrayerReminder(
                canNotify: true, frequency: .weekly, hour: 7, minute: 15, dayOfWeek: 2, dayOfMonth: nil, dateTicks: nil)
            let id = try PrayerStore(database: h.database).create(
                PrayerDraft(prayerCardId: 2, title: "existingLoadsStoredTime", reminder: reminder), now: .now)
            let model = h.editor(prayerId: id, cardId: nil)
            #expect(model.remindMe)
            #expect(model.frequency == .weekly)
            #expect(model.dayOfWeek == 2)
            #expect(parts(model.reminderTime) == [7, 15])
        }
    }

    @Test func saveWeeklyPersistsAndReconciles() async throws {
        try await withHarness { h in
            let model = h.editor()
            model.title = "saveWeeklyPersistsAndReconciles"
            model.remindMe = true
            model.frequency = .weekly
            model.dayOfWeek = 0
            model.reminderTime = time(6, 45)
            #expect(await model.save())
            let record = try h.record(titled: "saveWeeklyPersistsAndReconciles")
            #expect(record.canNotify)
            #expect(record.prayerFrequency == .weekly)
            #expect(record.notifyDayOfWeek == 0)
            #expect(record.notifyHour == 6)
            #expect(record.notifyMinute == 45)
            #expect(h.reconciler.count == 1)
        }
    }

    @Test func remindOffSavesCanNotifyFalse() async throws {
        try await withHarness { h in
            let reminder = PrayerReminder(canNotify: true, frequency: .daily, hour: 8, minute: 0)
            let id = try PrayerStore(database: h.database).create(
                PrayerDraft(prayerCardId: 2, title: "remindOffSavesCanNotifyFalse", reminder: reminder), now: .now)
            let model = h.editor(prayerId: id, cardId: nil)
            model.remindMe = false
            #expect(await model.save())
            #expect(try !h.record(titled: "remindOffSavesCanNotifyFalse").canNotify)
        }
    }

    @Test func pastOneTimeDateIsMinimum() async throws {
        try await withHarness { h in
            let past = Calendar.current.startOfDay(
                for: Calendar.current.date(from: DateComponents(year: 2020, month: 1, day: 2)) ?? .now)
            let reminder = PrayerReminder(
                canNotify: true, frequency: .oneTime, hour: 9, minute: 0, dateTicks: past.ticks)
            let id = try PrayerStore(database: h.database).create(
                PrayerDraft(prayerCardId: 2, title: "pastOneTimeDateIsMinimum", reminder: reminder), now: .now)
            let model = h.editor(prayerId: id, cardId: nil)
            #expect(model.minimumReminderDate == past)
            #expect(model.reminderDate == past)
        }
    }

    @Test func yearlyStoresDate() async throws {
        try await withHarness { h in
            let model = h.editor()
            let date = try #require(Calendar.current.date(from: DateComponents(year: 2001, month: 3, day: 5, hour: 14)))
            model.title = "yearlyStoresDate"
            model.remindMe = true
            model.frequency = .yearly
            model.reminderDate = date
            #expect(await model.save())
            let record = try h.record(titled: "yearlyStoresDate")
            #expect(record.prayerFrequency == .yearly)
            #expect(record.notifyDateTicks == Calendar.current.startOfDay(for: date).ticks)
        }
    }

    @Test func reconcileFailureKeepsEntries() async throws {
        try await withHarness { h in
            h.reconciler.error = ReconcileFailure()
            let model = h.editor()
            model.title = "reconcileFailureKeepsEntries"
            model.remindMe = true
            #expect(!(await model.save()))
            #expect(model.alert != nil)
            #expect(model.title == "reconcileFailureKeepsEntries")
        }
    }

    @Test func answeredSaveReconciles() async throws {
        try await withHarness { h in
            let model = h.editor()
            model.title = "answeredSaveReconciles"
            model.isAnswered = true
            #expect(await model.save())
            #expect(h.reconciler.count == 1)
        }
    }

    @Test func saveAndAddAnotherResetsReminder() async throws {
        try await withHarness { h in
            h.preferences.defaultNotifyHour = 9
            h.preferences.defaultNotifyMinute = 0
            let model = h.editor()
            model.title = "saveAndAddAnotherResetsReminder"
            model.remindMe = true
            model.frequency = .monthly
            model.reminderTime = time(21, 10)
            #expect(await model.saveAndAddAnother())
            #expect(!model.remindMe)
            #expect(model.frequency == .oneTime)
            #expect(parts(model.reminderTime) == [9, 0])
            #expect(!model.isDirty)
        }
    }

    @Test func reminderChangeIsDirty() async throws {
        try await withHarness { h in
            let model = h.editor()
            #expect(!model.isDirty)
            model.remindMe = true
            #expect(model.isDirty)
        }
    }
}
