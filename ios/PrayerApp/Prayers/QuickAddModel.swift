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

    typealias Saver = @Sendable (QuickAddRequest, Date) throws -> Int

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let saver: Saver
    private let initialPrayers: [PrayerEntry]
    private let initialCardTitle: String
    @ObservationIgnored var onTargetChange: (() -> Void)?

    var addTo: AddTo = .newCard {
        didSet {
            guard addTo != oldValue else { return }
            switch addTo {
            case .existingCard:
                cardTitle = ""
                collectionId = 0
            case .newCard:
                cardTitle = initialCardTitle
                selectedCardId = nil
                selectedCardTitle = nil
            }
            onTargetChange?()
        }
    }
    var collectionId: Int64 = 0
    var cardTitle: String
    private(set) var selectedCardId: Int64?
    private(set) var selectedCardTitle: String?
    var prayers: [PrayerEntry]
    private(set) var collections: [CardBoxRecord] = []
    private(set) var isSaving = false
    var errorMessage: String?

    init(
        database: PrayerDatabase, prayers: [PrayerEntry]? = nil, cardTitle: String = "", saver: Saver? = nil
    ) {
        let seeded = prayers ?? [Self.emptyEntry()]
        self.database = database
        self.initialPrayers = seeded
        self.initialCardTitle = cardTitle
        self.prayers = seeded
        self.cardTitle = cardTitle
        self.saver =
            saver ?? { request, now in
                try QuickAddSaver(database: database).save(request, now: now).savedCount
            }
    }

    var canSave: Bool {
        guard !isSaving, prayers.contains(where: { !Self.isBlank($0.title) }) else { return false }
        switch addTo {
        case .newCard: return !Self.isBlank(cardTitle)
        case .existingCard: return selectedCardId != nil
        }
    }

    var isDirty: Bool {
        Self.signature(prayers) != Self.signature(initialPrayers)
            || cardTitle != initialCardTitle
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
        onTargetChange?()
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
        let saver = saver
        do {
            let savedCount = try await Task.detached { try saver(request, now) }.value
            let message =
                savedCount == 1
                ? String(localized: "Prayer saved")
                : String(localized: "\(savedCount) prayers saved")
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

    private static func signature(_ entries: [PrayerEntry]) -> [[String]] {
        entries.map {
            [
                $0.id.uuidString, $0.title.trimmingCharacters(in: .whitespacesAndNewlines),
                $0.details.trimmingCharacters(in: .whitespacesAndNewlines),
            ]
        }
    }

    private static func isBlank(_ text: String) -> Bool {
        text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}
