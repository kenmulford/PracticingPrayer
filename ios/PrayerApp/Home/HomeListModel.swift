import Foundation
import Observation
import PrayerKit

enum HomeListScope: Hashable { case overdue, unanswered, activeCards }

struct HomeListSection: Identifiable, Equatable {
    let id: Int64
    let title: String?
    let items: [PrayerListItem]
}

@Observable
final class HomeListModel {
    private let scope: HomeListScope
    private let database: PrayerDatabase
    private let overdueDayThreshold: Int
    private let isSessionUnlocked: Bool
    private let now: () -> Date

    private(set) var sections: [HomeListSection]?
    private(set) var loadFailed = false
    var pendingDelete: PrayerListItem?
    var errorMessage: String?

    init(
        scope: HomeListScope, database: PrayerDatabase, overdueDayThreshold: Int,
        isSessionUnlocked: Bool = false, now: @escaping () -> Date = { .now }
    ) {
        self.scope = scope
        self.database = database
        self.overdueDayThreshold = overdueDayThreshold
        self.isSessionUnlocked = isSessionUnlocked
        self.now = now
    }

    var isEmpty: Bool { sections?.allSatisfy { $0.items.isEmpty } ?? true }

    func load() {
        do {
            let active = try store.prayers(segment: .active, isSessionUnlocked: isSessionUnlocked)
            switch scope {
            case .overdue:
                let byId = Dictionary(uniqueKeysWithValues: active.map { ($0.record.id, $0) })
                let overdue = try database.overduePrayers(
                    now: now(), overdueDayThreshold: overdueDayThreshold, isSessionUnlocked: isSessionUnlocked)
                sections = [HomeListSection(id: 0, title: nil, items: overdue.compactMap { byId[$0.id] })]
            case .unanswered:
                sections = [HomeListSection(id: 0, title: nil, items: active)]
            case .activeCards:
                var order: [Int64] = []
                var grouped: [Int64: [PrayerListItem]] = [:]
                for item in active {
                    let cardId = item.record.prayerCardId
                    if grouped[cardId] == nil { order.append(cardId) }
                    grouped[cardId, default: []].append(item)
                }
                sections = order.map { id in
                    let items = grouped[id] ?? []
                    return HomeListSection(id: id, title: items.first?.cardTitle, items: items)
                }
            }
            loadFailed = false
        } catch {
            loadFailed = true
        }
    }

    func setAnswered(_ item: PrayerListItem, answered: Bool) {
        perform {
            if answered {
                try store.markAnswered(id: item.record.id, now: now())
            } else {
                try store.markActive(id: item.record.id, now: now())
            }
        }
    }

    func confirmDelete() {
        guard let item = pendingDelete else { return }
        pendingDelete = nil
        perform { try store.delete(id: item.record.id) }
    }

    private var store: PrayerStore { PrayerStore(database: database) }

    private func perform(_ change: () throws -> Void) {
        do {
            try change()
        } catch {
            errorMessage = error.localizedDescription
        }
        load()
    }
}
