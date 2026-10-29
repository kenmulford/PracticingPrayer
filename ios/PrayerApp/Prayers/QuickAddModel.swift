import Accessibility
import Foundation
import Observation
import PrayerKit

@Observable
final class QuickAddModel {
    enum AddTo: Hashable, CaseIterable { case newCard, existingCard }

    struct PrayerEntry: Identifiable, Equatable {
        let id: UUID
        var title: String
        var details: String
    }

    @ObservationIgnored private let database: PrayerDatabase

    var addTo: AddTo = .newCard {
        didSet {
            guard addTo != oldValue else { return }
            switch addTo {
            case .existingCard:
                cardTitle = ""
                collectionId = 0
            case .newCard:
                selectedCardId = nil
                selectedCardTitle = nil
            }
        }
    }
    var collectionId: Int64 = 0
    var cardTitle = ""
    private(set) var selectedCardId: Int64?
    private(set) var selectedCardTitle: String?
    var prayers = [QuickAddModel.emptyEntry()]
    private(set) var collections: [CardBoxRecord] = []
    private(set) var isSaving = false
    var errorMessage: String?

    init(database: PrayerDatabase) {
        self.database = database
    }

    var canSave: Bool {
        guard !isSaving, prayers.contains(where: { !Self.isBlank($0.title) }) else { return false }
        switch addTo {
        case .newCard: return !Self.isBlank(cardTitle)
        case .existingCard: return selectedCardId != nil
        }
    }

    var isDirty: Bool {
        prayers.count > 1
            || prayers.contains { !Self.isBlank($0.title) || !Self.isBlank($0.details) }
            || !Self.isBlank(cardTitle)
            || selectedCardId != nil
    }

    func loadCollections() {
        collections = ((try? CollectionStore(database: database).collections()) ?? []).filter { !$0.isSystem }
    }

    func addPrayer() {
        prayers.append(Self.emptyEntry())
    }

    func removePrayer(id: UUID) {
        guard prayers.count > 1 else { return }
        prayers.removeAll { $0.id == id }
    }

    func selectCard(id: Int64, title: String) {
        selectedCardId = id
        selectedCardTitle = title
    }

    func save(now: Date = .now) async -> Bool {
        guard canSave else { return false }
        isSaving = true
        defer { isSaving = false }
        errorMessage = nil
        let target: QuickAddTarget
        switch addTo {
        case .newCard: target = .newCard(title: cardTitle, boxId: collectionId)
        case .existingCard: target = .existingCard(id: selectedCardId ?? 0)
        }
        let request = QuickAddRequest(
            target: target,
            prayers: prayers.map { QuickAddPrayer(title: $0.title, details: $0.details) })
        let saver = QuickAddSaver(database: database)
        do {
            let result = try await Task.detached { try saver.save(request, now: now) }.value
            let message =
                result.savedCount == 1
                ? String(localized: "Prayer saved")
                : String(localized: "\(result.savedCount) prayers saved")
            AccessibilityNotification.Announcement(message).post()
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }

    private static func emptyEntry() -> PrayerEntry {
        PrayerEntry(id: UUID(), title: "", details: "")
    }

    private static func isBlank(_ text: String) -> Bool {
        text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}
