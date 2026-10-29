/// Splits a snapshot into the confidential subtree and its scrubbed remainder, as the MAUI splitter.
public enum ConfidentialExportSplitter {
    public struct SplitResult: Equatable, Sendable {
        public var confidentialCards: [PrayerCardRecord]
        public var remainingCards: [PrayerCardRecord]
        public var confidentialPrayers: [PrayerRequestRecord]
        public var remainingPrayers: [PrayerRequestRecord]
        public var confidentialInteractions: [PrayerInteractionRecord]
        public var remainingInteractions: [PrayerInteractionRecord]
        public var confidentialTagJunctions: [PrayerCardTagRecord]
        public var remainingTagJunctions: [PrayerCardTagRecord]
    }

    public static func split(
        cards: [PrayerCardRecord], boxes: [CardBoxRecord], prayers: [PrayerRequestRecord],
        interactions: [PrayerInteractionRecord], tagJunctions: [PrayerCardTagRecord]
    ) -> SplitResult {
        let boxesById = Dictionary(boxes.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        let confidentialCards = cards.filter { isConfidential($0, boxesById) }
        let cardIds = Set(confidentialCards.map(\.id))
        let confidentialPrayers = prayers.filter { cardIds.contains($0.prayerCardId) }
        let prayerIds = Set(confidentialPrayers.map(\.id))
        let confidentialInteractions = interactions.filter { prayerIds.contains($0.prayerId) }
        // A zero or nil PrayerRequestId is a legacy row no prayer owns, so it stays in the remainder.
        let confidentialJunctions = tagJunctions.filter {
            guard let requestId = $0.prayerRequestId, requestId != 0 else { return false }
            return prayerIds.contains(requestId)
        }
        let interactionIds = Set(confidentialInteractions.map(\.id))
        let junctionIds = Set(confidentialJunctions.map(\.id))

        return SplitResult(
            confidentialCards: confidentialCards,
            remainingCards: cards.filter { !cardIds.contains($0.id) },
            confidentialPrayers: confidentialPrayers,
            remainingPrayers: prayers.filter { !cardIds.contains($0.prayerCardId) },
            confidentialInteractions: confidentialInteractions,
            remainingInteractions: interactions.filter { !interactionIds.contains($0.id) },
            confidentialTagJunctions: confidentialJunctions,
            remainingTagJunctions: tagJunctions.filter { !junctionIds.contains($0.id) })
    }

    private static func isConfidential(_ card: PrayerCardRecord, _ boxesById: [Int64: CardBoxRecord]) -> Bool {
        ProtectionPolicy.effectiveMode(of: card, box: boxesById[card.boxId ?? 0]) != .unprotected
    }
}
