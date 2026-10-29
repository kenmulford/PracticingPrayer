import Foundation
import PrayerKit

struct CardBulkResult: Equatable {
    let succeeded: Set<Int64>
    let errorMessage: String?
}

struct CardBulkActions {
    let database: PrayerDatabase
    let preferences: AppPreferences

    private var mutations: CardMutations { CardMutations(database: database, preferences: preferences) }

    func move(_ ids: Set<Int64>, toBox boxId: Int64, now: Date = .now) -> CardBulkResult {
        run(ids) { try mutations.move(id: $0, toBox: boxId, now: now) }
    }

    func archive(_ ids: Set<Int64>, now: Date = .now) -> CardBulkResult {
        run(ids) { try mutations.archive(id: $0, now: now) }
    }

    func delete(_ ids: Set<Int64>) -> CardBulkResult {
        run(ids) { try mutations.delete(id: $0) }
    }

    private func run(_ ids: Set<Int64>, _ work: (Int64) throws -> Void) -> CardBulkResult {
        var succeeded: Set<Int64> = []
        for id in ids.sorted() {
            do {
                try work(id)
                succeeded.insert(id)
            } catch {
                return CardBulkResult(succeeded: succeeded, errorMessage: error.localizedDescription)
            }
        }
        return CardBulkResult(succeeded: succeeded, errorMessage: nil)
    }
}
