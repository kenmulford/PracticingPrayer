import Accessibility
import Foundation
import Observation
import PrayerKit

@Observable
final class PrayerEditorModel {
    enum Outcome { case saved, deleted }

    private struct Snapshot: Equatable {
        var cardId: Int64?
        var title: String
        var details: String
        var isAnswered: Bool
        var answeredOn: Date
        var tagIds: Set<Int64>
        var reminder: ReminderState
    }

    private struct ReminderState: Equatable {
        var remindMe: Bool
        var frequency: PrayerFrequency
        var time: Date
        var date: Date
        var dayOfWeek: Int
        var dayOfMonth: Int
    }

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let prayerId: Int64?
    @ObservationIgnored private let defaultAnsweredOn: Date
    @ObservationIgnored private let preferences: AppPreferences
    @ObservationIgnored private let reminders: any ReminderReconciling
    @ObservationIgnored private let authorization: ReminderAuthorization
    @ObservationIgnored private let calendar = Calendar.current
    @ObservationIgnored private var snapshot: Snapshot
    @ObservationIgnored private var originalAnswered = false
    @ObservationIgnored private var createdId: Int64?

    var title: String
    var details: String
    var isAnswered: Bool
    var answeredOn: Date
    var tagIds: Set<Int64>
    var alert: String?
    var remindMe = false
    var frequency: PrayerFrequency = .oneTime
    var reminderTime: Date
    var reminderDate: Date
    var dayOfWeek: Int
    var dayOfMonth: Int

    private(set) var cardId: Int64?
    private(set) var cardTitle: String
    private(set) var allTags: [PrayerTagRecord] = []
    private(set) var isSaving = false
    private(set) var minimumReminderDate: Date
    private(set) var isAuthorizing = false
    private(set) var authorizationDenied = false

    init(
        prayerId: Int64?, cardId: Int64?, cardTitle: String?, database: PrayerDatabase,
        preferences: AppPreferences, reminders: any ReminderReconciling,
        authorization: ReminderAuthorization = .system, today: Date = .now
    ) {
        self.database = database
        self.prayerId = prayerId
        self.preferences = preferences
        self.reminders = reminders
        self.authorization = authorization
        let start = Calendar.current.startOfDay(for: today)
        let weekday = Calendar.current.component(.weekday, from: today) - 1
        let day = Calendar.current.component(.day, from: today)
        let time = preferences.defaultReminderTime
        self.defaultAnsweredOn = start
        self.minimumReminderDate = start
        self.reminderTime = time
        self.reminderDate = start
        self.dayOfWeek = weekday
        self.dayOfMonth = day
        self.cardId = cardId
        self.cardTitle = cardTitle ?? ""
        self.title = ""
        self.details = ""
        self.isAnswered = false
        self.answeredOn = start
        self.tagIds = []
        self.snapshot = Snapshot(
            cardId: cardId, title: "", details: "", isAnswered: false, answeredOn: start, tagIds: [],
            reminder: ReminderState(
                remindMe: false, frequency: .oneTime, time: time, date: start, dayOfWeek: weekday, dayOfMonth: day))
    }

    var isNew: Bool { prayerId == nil }

    var canSave: Bool { !trimmedTitle.isEmpty && cardId != nil && !isSaving }

    var isDirty: Bool { current != snapshot }

    var selectedTags: [PrayerTagRecord] { allTags.filter { tagIds.contains($0.id) } }

    private var trimmedTitle: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    private var current: Snapshot {
        Snapshot(
            cardId: cardId, title: title, details: details, isAnswered: isAnswered, answeredOn: answeredOn,
            tagIds: tagIds, reminder: reminderState)
    }

    private var reminderState: ReminderState {
        ReminderState(
            remindMe: remindMe, frequency: frequency, time: reminderTime, date: reminderDate, dayOfWeek: dayOfWeek,
            dayOfMonth: dayOfMonth)
    }

    private var reminderDraft: PrayerReminder {
        let time = calendar.dateComponents([.hour, .minute], from: reminderTime)
        let usesDate = frequency == .oneTime || frequency == .yearly
        return PrayerReminder(
            canNotify: remindMe, frequency: frequency, hour: time.hour ?? 9, minute: time.minute ?? 0,
            dayOfWeek: frequency == .weekly ? dayOfWeek : nil,
            dayOfMonth: frequency == .monthly ? dayOfMonth : nil,
            dateTicks: usesDate ? calendar.startOfDay(for: reminderDate).ticks : nil)
    }

    private func resetReminder() {
        remindMe = false
        frequency = .oneTime
        reminderTime = preferences.defaultReminderTime
        reminderDate = defaultAnsweredOn
        dayOfWeek = calendar.component(.weekday, from: defaultAnsweredOn) - 1
        dayOfMonth = calendar.component(.day, from: defaultAnsweredOn)
        minimumReminderDate = defaultAnsweredOn
    }

    private var draft: PrayerDraft? {
        guard let cardId else { return nil }
        let trimmedDetails = details.trimmingCharacters(in: .whitespacesAndNewlines)
        return PrayerDraft(
            prayerCardId: cardId, title: trimmedTitle, details: trimmedDetails.isEmpty ? nil : details,
            tagIds: tagIds.sorted(), reminder: reminderDraft)
    }

    func load() {
        do {
            allTags = try TagStore(database: database).tags().map(\.tag)
            if let prayerId {
                let items = try PrayerStore(database: database).prayers(segment: .all, isSessionUnlocked: false)
                if let item = items.first(where: { $0.record.id == prayerId }) {
                    let record = item.record
                    cardId = record.prayerCardId
                    cardTitle = item.cardTitle
                    title = record.title
                    details = record.details ?? ""
                    isAnswered = record.isAnswered
                    originalAnswered = record.isAnswered
                    if let ticks = record.answeredAtTicks, let date = try? Date(ticks: ticks) {
                        answeredOn = date
                    }
                    tagIds = Set(item.tagIds)
                    loadReminder(record)
                }
            }
            snapshot = current
        } catch {
            alert = error.localizedDescription
        }
    }

    private func loadReminder(_ record: PrayerRequestRecord) {
        guard record.canNotify else { return }
        remindMe = true
        frequency = record.prayerFrequency
        if let hour = record.notifyHour, let minute = record.notifyMinute,
            let time = calendar.date(bySettingHour: hour, minute: minute, second: 0, of: defaultAnsweredOn)
        {
            reminderTime = time
        }
        if let day = record.notifyDayOfWeek, (0...6).contains(day) { dayOfWeek = day }
        if let day = record.notifyDayOfMonth, (1...31).contains(day) { dayOfMonth = day }
        if let ticks = record.notifyDateTicks, let stored = try? Date(ticks: ticks) {
            reminderDate = calendar.startOfDay(for: stored)
            if record.prayerFrequency == .oneTime, reminderDate < minimumReminderDate {
                minimumReminderDate = reminderDate
            }
        }
    }

    func setRemindMe(_ on: Bool) async {
        guard on else {
            remindMe = false
            await refreshAuthorization()
            return
        }
        if preferences.allowNotifications {
            switch await authorization.status() {
            case .notDetermined:
                isAuthorizing = true
                remindMe = await authorization.request()
                isAuthorizing = false
            case .denied:
                remindMe = false
            default:
                remindMe = true
            }
        } else {
            remindMe = true
        }
        await refreshAuthorization()
    }

    func refreshAuthorization() async {
        authorizationDenied = await authorization.status() == .denied
    }

    func loadTags() {
        do {
            allTags = try TagStore(database: database).tags().map(\.tag)
        } catch {
            alert = error.localizedDescription
        }
    }

    func selectCard(id: Int64, title: String) {
        cardId = id
        cardTitle = title
    }

    func save(now: Date = .now) async -> Bool {
        guard canSave else { return false }
        let reminderChanged = reminderState != snapshot.reminder
        return await performAsync {
            try await write(now: now)
            announceSaved(reminderChanged: reminderChanged)
        }
    }

    func saveAndAddAnother(now: Date = .now) async -> Bool {
        guard isNew, canSave else { return false }
        let reminderChanged = reminderState != snapshot.reminder
        return await performAsync {
            try await write(now: now)
            announceSaved(reminderChanged: reminderChanged)
            createdId = nil
            title = ""
            details = ""
            isAnswered = false
            answeredOn = defaultAnsweredOn
            tagIds = []
            resetReminder()
            snapshot = current
        }
    }

    func delete() -> Bool {
        guard let prayerId else { return false }
        return perform {
            try PrayerStore(database: database).delete(id: prayerId)
            announce("Prayer deleted")
        }
    }

    private func write(now: Date) async throws {
        guard let draft else { return }
        let store = PrayerStore(database: database)
        if let prayerId {
            try store.update(id: prayerId, with: draft, now: now)
            if isAnswered {
                try store.markAnswered(id: prayerId, on: answeredOn, now: now)
            } else if originalAnswered {
                try store.markActive(id: prayerId, now: now)
            }
        } else {
            let id = try createdId ?? store.create(draft, now: now)
            createdId = id
            if isAnswered { try store.markAnswered(id: id, on: answeredOn, now: now) }
        }
        try await reminders.reconcile(now: now)
    }

    private func announceSaved(reminderChanged: Bool) {
        if !reminderChanged {
            announce("Prayer saved")
        } else if remindMe {
            announce("Prayer saved. Reminder on.")
        } else {
            announce("Prayer saved. Reminder off.")
        }
    }

    private func announce(_ message: String.LocalizationValue) {
        AccessibilityNotification.Announcement(String(localized: message)).post()
    }

    private func performAsync(_ work: () async throws -> Void) async -> Bool {
        isSaving = true
        defer { isSaving = false }
        do {
            try await work()
            return true
        } catch {
            alert = error.localizedDescription
            return false
        }
    }

    private func perform(_ work: () throws -> Void) -> Bool {
        isSaving = true
        defer { isSaving = false }
        do {
            try work()
            return true
        } catch {
            alert = error.localizedDescription
            return false
        }
    }
}
