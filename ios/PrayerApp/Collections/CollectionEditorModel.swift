import Accessibility
import Foundation
import Observation
import PrayerKit

@Observable
final class CollectionEditorModel {
    enum Alert: Equatable {
        case duplicateName(String)
        case failure(String)
    }

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let original: CardBoxRecord?
    @ObservationIgnored private let originalMode: CardProtectionMode

    var name: String
    var protectAllCards: Bool
    var protectionMode: CardProtectionMode
    var alert: Alert?

    init(database: PrayerDatabase, collection: CardBoxRecord?) {
        self.database = database
        self.original = collection
        let mode: CardProtectionMode = collection?.cardProtectionMode == .hidden ? .hidden : .lockedVisible
        self.originalMode = mode
        self.name = collection?.name ?? ""
        self.protectAllCards = collection?.protectAllCards ?? false
        self.protectionMode = mode
    }

    var isNew: Bool { original == nil }

    var canSave: Bool {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        return !trimmed.isEmpty && trimmed.count <= CollectionStore.maxNameLength
    }

    var isDirty: Bool { isNameDirty || isProtectionDirty }

    private var isNameDirty: Bool { name != (original?.name ?? "") }

    private var isProtectionDirty: Bool {
        protectAllCards != (original?.protectAllCards ?? false)
            || ((protectAllCards || (original?.protectAllCards ?? false)) && protectionMode != originalMode)
    }

    func save(now: Date = .now) -> Bool {
        guard canSave else { return false }
        let store = CollectionStore(database: database)
        do {
            let id: Int64
            if let original {
                id = original.id
                if isNameDirty { try store.rename(id: id, to: name, now: now) }
            } else {
                id = try store.create(name: name, now: now).id
            }
            if isNew ? protectAllCards : isProtectionDirty {
                try store.setProtection(
                    id: id, protectAllCards: protectAllCards,
                    mode: protectAllCards || isNew ? protectionMode : (original?.cardProtectionMode ?? protectionMode),
                    now: now)
            }
            AccessibilityNotification.Announcement(String(localized: "Collection saved")).post()
            return true
        } catch CollectionError.duplicateName {
            alert = .duplicateName(name.trimmingCharacters(in: .whitespacesAndNewlines))
            return false
        } catch {
            alert = .failure(error.localizedDescription)
            return false
        }
    }

    func delete(option: CollectionDeleteOption) -> Bool {
        guard let original else { return false }
        do {
            try CollectionStore(database: database).delete(id: original.id, option: option)
            return true
        } catch {
            alert = .failure(error.localizedDescription)
            return false
        }
    }
}
