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
    }

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let prayerId: Int64?
    @ObservationIgnored private let defaultAnsweredOn: Date
    @ObservationIgnored private var snapshot: Snapshot
    @ObservationIgnored private var originalAnswered = false
    @ObservationIgnored private var createdId: Int64?

    var title: String
    var details: String
    var isAnswered: Bool
    var answeredOn: Date
    var tagIds: Set<Int64>
    var alert: String?

    private(set) var cardId: Int64?
    private(set) var cardTitle: String
    private(set) var allTags: [PrayerTagRecord] = []
    private(set) var isSaving = false

    init(prayerId: Int64?, cardId: Int64?, cardTitle: String?, database: PrayerDatabase, today: Date = .now) {
        self.database = database
        self.prayerId = prayerId
        self.defaultAnsweredOn = Calendar.current.startOfDay(for: today)
        self.cardId = cardId
        self.cardTitle = cardTitle ?? ""
        self.title = ""
        self.details = ""
        self.isAnswered = false
        self.answeredOn = defaultAnsweredOn
        self.tagIds = []
        self.snapshot = Snapshot(
            cardId: cardId, title: "", details: "", isAnswered: false, answeredOn: defaultAnsweredOn, tagIds: [])
    }

    var isNew: Bool { prayerId == nil }

    var canSave: Bool { !trimmedTitle.isEmpty && cardId != nil && !isSaving }

    var isDirty: Bool { current != snapshot }

    var selectedTags: [PrayerTagRecord] { allTags.filter { tagIds.contains($0.id) } }

    private var trimmedTitle: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    private var current: Snapshot {
        Snapshot(
            cardId: cardId, title: title, details: details, isAnswered: isAnswered, answeredOn: answeredOn,
            tagIds: tagIds)
    }

    private var draft: PrayerDraft? {
        guard let cardId else { return nil }
        let trimmedDetails = details.trimmingCharacters(in: .whitespacesAndNewlines)
        return PrayerDraft(
            prayerCardId: cardId, title: trimmedTitle, details: trimmedDetails.isEmpty ? nil : details,
            tagIds: tagIds.sorted())
    }

    func load() {
        do {
            allTags = try TagStore(database: database).tags().map(\.tag)
            if let prayerId {
                let items = try PrayerStore(database: database).prayers(segment: .all, isSessionUnlocked: true)
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
                }
            }
            snapshot = current
        } catch {
            alert = error.localizedDescription
        }
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

    func save(now: Date = .now) -> Bool {
        guard canSave else { return false }
        return perform {
            try write(now: now)
            announce("Prayer saved")
        }
    }

    func saveAndAddAnother(now: Date = .now) -> Bool {
        guard isNew, canSave else { return false }
        return perform {
            try write(now: now)
            announce("Prayer saved")
            createdId = nil
            title = ""
            details = ""
            isAnswered = false
            answeredOn = defaultAnsweredOn
            tagIds = []
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

    private func write(now: Date) throws {
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
    }

    private func announce(_ message: String.LocalizationValue) {
        AccessibilityNotification.Announcement(String(localized: message)).post()
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
