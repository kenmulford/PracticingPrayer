import Foundation
import PrayerKit

nonisolated enum PrayerTimeScope: Hashable, Sendable {
    case all, cards(Set<Int64>), tags(Set<Int64>), collection(Int64), prayers([Int64])

    static func activePrayers(in items: [PrayerListItem]) -> PrayerTimeScope? {
        let ids = items.filter { !$0.record.isAnswered }.map(\.record.id)
        return ids.isEmpty ? nil : .prayers(ids)
    }
}
