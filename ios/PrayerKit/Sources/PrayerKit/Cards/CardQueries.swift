import Foundation
import GRDB

public struct CardListFilter: Equatable, Sendable {
    public var searchText: String
    public var tagIds: Set<Int64>

    public init(searchText: String = "", tagIds: Set<Int64> = []) {
        self.searchText = searchText
        self.tagIds = tagIds
    }
}

public struct CardListItem: Equatable, Sendable {
    public let card: PrayerCardRecord
    public let activePrayerCount: Int?
    public let isMasked: Bool
}

public struct CardSection: Equatable, Sendable {
    /// 0 is Loose Cards.
    public let boxId: Int64
    public let box: CardBoxRecord?
    public let cards: [CardListItem]
}

public struct CardList: Equatable, Sendable {
    public let sections: [CardSection]
    public let totalMatchCount: Int
}

private func ordered(_ lhs: String, _ rhs: String) -> ComparisonResult {
    lhs.localizedCaseInsensitiveCompare(rhs)
}

public extension PrayerDatabase {
    func cardList(filter: CardListFilter = CardListFilter(), isSessionUnlocked: Bool) throws -> CardList {
        let (boxes, cards, requests, tagRows) = try dbQueue.read { db in
            (
                try CardBoxRecord.fetchAll(db),
                try PrayerCardRecord.fetchAll(db),
                try PrayerRequestRecord.fetchAll(db),
                try PrayerCardTagRecord.fetchAll(db)
            )
        }

        let activeRequests = requests.filter { !$0.isAnswered }
        var activeCounts: [Int64: Int] = [:]
        var requestToCard: [Int64: Int64] = [:]
        for request in activeRequests {
            activeCounts[request.prayerCardId, default: 0] += 1
            requestToCard[request.id] = request.prayerCardId
        }

        var cardTagIds: [Int64: Set<Int64>] = [:]
        for row in tagRows {
            guard let requestId = row.prayerRequestId, requestId > 0,
                  let cardId = requestToCard[requestId] else { continue }
            cardTagIds[cardId, default: []].insert(row.prayerTagId)
        }

        let query = filter.searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        let boxesById = Dictionary(boxes.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        func isBlocked(_ card: PrayerCardRecord) -> Bool {
            ProtectionPolicy.isAccessBlocked(card, box: boxesById[card.boxId ?? 0], isSessionUnlocked: isSessionUnlocked)
        }
        let narrowed = !query.isEmpty || !filter.tagIds.isEmpty
        let visible = isSessionUnlocked ? cards : cards.filter { card in
            narrowed
                ? !isBlocked(card)
                : !ProtectionPolicy.isHiddenWhileLocked(
                    card, box: boxesById[card.boxId ?? 0], isSessionUnlocked: isSessionUnlocked)
        }
        let items = Dictionary(grouping: visible, by: { $0.boxId ?? 0 }).mapValues { group in
            group.sorted { a, b in
                if a.isFavorite != b.isFavorite { return a.isFavorite }
                let byTitle = ordered(a.title, b.title)
                return byTitle == .orderedSame ? a.id < b.id : byTitle == .orderedAscending
            }
        }

        func section(boxId: Int64, box: CardBoxRecord?) -> CardSection {
            let matching = (items[boxId] ?? []).filter { card in
                if !query.isEmpty, card.title.range(of: query, options: .caseInsensitive) == nil {
                    return false
                }
                if !filter.tagIds.isEmpty,
                   filter.tagIds.isDisjoint(with: cardTagIds[card.id] ?? []) {
                    return false
                }
                return true
            }
            return CardSection(
                boxId: boxId,
                box: box,
                cards: matching.map { card in
                    guard isBlocked(card) else {
                        return CardListItem(card: card, activePrayerCount: activeCounts[card.id] ?? 0, isMasked: false)
                    }
                    var masked = card
                    masked.title = ProtectionPolicy.maskedTitle
                    return CardListItem(card: masked, activePrayerCount: nil, isMasked: true)
                }
            )
        }

        var sections: [CardSection] = []
        if !(items[0] ?? []).isEmpty {
            sections.append(section(boxId: 0, box: nil))
        }
        let userBoxes = boxes.filter { !$0.isSystem }.sorted { a, b in
            let byName = ordered(a.name, b.name)
            return byName == .orderedSame ? a.id < b.id : byName == .orderedAscending
        }
        for box in userBoxes {
            sections.append(section(boxId: box.id, box: box))
        }
        let byId = boxes.sorted { $0.id < $1.id }
        if let system = byId.first(where: { $0.systemKey == "system" }),
           !(items[system.id] ?? []).isEmpty {
            sections.append(section(boxId: system.id, box: system))
        }
        if let archived = byId.first(where: { $0.systemKey == "archived" }) {
            sections.append(section(boxId: archived.id, box: archived))
        }

        return CardList(sections: sections, totalMatchCount: sections.reduce(0) { $0 + $1.cards.count })
    }
}
