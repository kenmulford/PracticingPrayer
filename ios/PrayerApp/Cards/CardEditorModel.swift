import Accessibility
import Foundation
import Observation
import PrayerKit

@Observable
final class CardEditorModel {
    enum Outcome { case saved, archived, unarchived, deleted }

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let preferences: AppPreferences
    @ObservationIgnored private var original: PrayerCardRecord?
    @ObservationIgnored private var originalBoxId: Int64
    @ObservationIgnored private let originalMode: CardProtectionMode
    @ObservationIgnored private var boxesById: [Int64: CardBoxRecord] = [:]

    var title: String
    var boxId: Int64
    var protectionMode: CardProtectionMode
    var alert: String?

    private(set) var collections: [CardBoxRecord] = []
    private(set) var storedSystemBox: CardBoxRecord?
    private(set) var prayerCount = 0

    init(card: PrayerCardRecord?, database: PrayerDatabase, preferences: AppPreferences) {
        self.database = database
        self.preferences = preferences
        self.original = card
        self.originalBoxId = card?.boxId ?? 0
        self.originalMode = card?.protectionMode ?? .unprotected
        self.title = card?.title ?? ""
        self.boxId = card?.boxId ?? 0
        self.protectionMode = card?.protectionMode ?? .unprotected
    }

    var isNew: Bool { original == nil }

    private var archivedFolderId: Int64 { Int64(preferences.archivedFolderId) }

    var isArchived: Bool { archivedFolderId != 0 && boxId == archivedFolderId }

    var canSave: Bool { !trimmedTitle.isEmpty }

    var isDirty: Bool {
        title != (original?.title ?? "") || boxId != originalBoxId || protectionMode != originalMode
    }

    /// Saving that lowers the effective protection needs a fresh device-owner check.
    var requiresOwnerVerification: Bool {
        guard let original else { return false }
        var edited = original
        edited.boxId = boxId
        edited.protectionMode = protectionMode
        return ProtectionPolicy.isLowering(
            from: ProtectionPolicy.effectiveMode(of: original, box: boxesById[originalBoxId]),
            to: ProtectionPolicy.effectiveMode(of: edited, box: boxesById[boxId]))
    }

    private var trimmedTitle: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    private var mutations: CardMutations { CardMutations(database: database, preferences: preferences) }

    func load() {
        do {
            let all = try CollectionStore(database: database).collections()
            boxesById = Dictionary(all.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
            collections = all
                .filter { !$0.isSystem && $0.id != archivedFolderId }
                .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            storedSystemBox = all.first { $0.id == originalBoxId && ($0.isSystem || $0.id == archivedFolderId) }
            if let original {
                prayerCount = try PrayerStore(database: database).prayers(cardId: original.id).count
            }
        } catch {
            alert = error.localizedDescription
        }
    }

    func save(now: Date = .now, ownerVerified: Bool = false) -> Bool {
        guard canSave else { return false }
        return perform {
            if let original {
                if isDirty {
                    try mutations.update(
                        id: original.id,
                        with: CardDraft(title: trimmedTitle, boxId: boxId, protectionMode: protectionMode), now: now,
                        ownerVerified: ownerVerified)
                }
            } else {
                _ = try mutations.create(
                    CardDraft(title: trimmedTitle, boxId: boxId, protectionMode: protectionMode), now: now)
            }
            AccessibilityNotification.Announcement(String(localized: "Card saved")).post()
        }
    }

    func saveAndAddAnother(now: Date = .now) -> Bool {
        guard isNew, canSave else { return false }
        return perform {
            _ = try mutations.create(
                CardDraft(title: trimmedTitle, boxId: boxId, protectionMode: protectionMode), now: now)
            AccessibilityNotification.Announcement(String(localized: "Card saved")).post()
            title = ""
            protectionMode = .unprotected
        }
    }

    func archive(now: Date = .now) -> Bool {
        guard let original else { return false }
        return perform {
            try mutations.archive(id: original.id, now: now)
            try refreshStoredBox(id: original.id)
        }
    }

    func unarchive(now: Date = .now) -> Bool {
        guard let original else { return false }
        return perform {
            try mutations.unarchive(id: original.id, now: now)
            try refreshStoredBox(id: original.id)
        }
    }

    func delete() -> Bool {
        guard let original else { return false }
        return perform { try mutations.delete(id: original.id) }
    }

    private func refreshStoredBox(id: Int64) throws {
        let list = try database.cardList(isSessionUnlocked: true)
        guard let record = list.sections.lazy.compactMap({ $0.cards.first { $0.card.id == id }?.card }).first
        else { return }
        original = record
        originalBoxId = record.boxId ?? 0
        boxId = originalBoxId
    }

    private func perform(_ work: () throws -> Void) -> Bool {
        do {
            try work()
            return true
        } catch {
            alert = error.localizedDescription
            return false
        }
    }
}
