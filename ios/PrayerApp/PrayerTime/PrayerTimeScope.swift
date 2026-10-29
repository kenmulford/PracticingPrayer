import Foundation

nonisolated enum PrayerTimeScope: Hashable, Sendable { case all, cards(Set<Int64>), tags(Set<Int64>), collection(Int64) }

enum ScopeLoadState<Value> {
    case loading
    case loaded(Value)
    case failed
}
