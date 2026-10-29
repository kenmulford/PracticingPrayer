import Foundation
import GRDB

public struct QuickAddPrayer: Sendable, Equatable {
    public var title: String
    public var details: String?

    public init(title: String, details: String? = nil) {
        self.title = title
        self.details = details
    }
}

public enum QuickAddTarget: Sendable, Equatable {
    case newCard(title: String, boxId: Int64)
    case existingCard(id: Int64)
}

public struct QuickAddRequest: Sendable, Equatable {
    public var target: QuickAddTarget
    public var prayers: [QuickAddPrayer]

    public init(target: QuickAddTarget, prayers: [QuickAddPrayer]) {
        self.target = target
        self.prayers = prayers
    }
}

public struct QuickAddResult: Sendable, Equatable {
    public let cardId: Int64
    public let savedCount: Int
}

public struct QuickAddSaver: Sendable {
    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    public func save(_ request: QuickAddRequest, now: Date) throws -> QuickAddResult {
        let drafts = request.prayers.compactMap { prayer -> (title: String, details: String?)? in
            let title = prayer.title.trimmingCharacters(in: .whitespacesAndNewlines)
            if title.isEmpty { return nil }
            let details = prayer.details?.trimmingCharacters(in: .whitespacesAndNewlines)
            return (title, details?.isEmpty == false ? details : nil)
        }
        if drafts.isEmpty { throw PrayerStoreError.emptyTitle }
        return try database.dbQueue.write { db in
            let ticks = now.ticks
            let cardId: Int64
            switch request.target {
            case .newCard(let title, let boxId):
                let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
                if trimmed.isEmpty { throw CardMutationError.emptyTitle }
                if boxId != 0, try !CardBoxRecord.exists(db, key: boxId) {
                    throw CardMutationError.boxNotFound(boxId)
                }
                cardId = try CardMutations.insertRow(CardDraft(title: trimmed, boxId: boxId), ticks: ticks, db)
            case .existingCard(let id):
                guard try PrayerCardRecord.exists(db, key: id) else { throw PrayerStoreError.cardNotFound(id) }
                cardId = id
            }
            for draft in drafts {
                _ = try PrayerStore.insertRow(
                    PrayerDraft(prayerCardId: cardId, title: draft.title, details: draft.details),
                    ticks: ticks, db)
            }
            return QuickAddResult(cardId: cardId, savedCount: drafts.count)
        }
    }
}
