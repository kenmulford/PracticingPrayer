public enum ProtectionError: Error, Equatable { case ownerVerificationRequired }

/// Port of the 1.7.2 effective-protection rule (`PrayerCard.GetEffectiveProtectionMode`, `ProtectionPolicy.IsAccessBlocked`).
public enum ProtectionPolicy {
    public static let maskedTitle = "Protected"

    public static func effectiveMode(of card: PrayerCardRecord, box: CardBoxRecord?) -> CardProtectionMode {
        if let own = card.protectionMode, own != .unprotected { return own }
        if let box, box.protectAllCards { return box.cardProtectionMode }
        return .unprotected
    }

    /// Lowering is the change that exposes cards, so it needs a fresh owner check.
    public static func isLowering(from old: CardProtectionMode, to new: CardProtectionMode) -> Bool {
        rank(new) < rank(old)
    }

    private static func rank(_ mode: CardProtectionMode) -> Int {
        switch mode {
        case .unprotected: 0
        case .lockedVisible: 1
        case .hidden: 2
        }
    }

    public static func isAccessBlocked(_ card: PrayerCardRecord, box: CardBoxRecord?, isSessionUnlocked: Bool) -> Bool {
        effectiveMode(of: card, box: box) != .unprotected && !isSessionUnlocked
    }

    public static func isHiddenWhileLocked(_ card: PrayerCardRecord, box: CardBoxRecord?, isSessionUnlocked: Bool) -> Bool {
        !isSessionUnlocked && effectiveMode(of: card, box: box) == .hidden
    }

    public static func blockedCardIds(
        cards: [PrayerCardRecord], boxes: [CardBoxRecord], isSessionUnlocked: Bool
    ) -> Set<Int64> {
        if isSessionUnlocked { return [] }
        let boxesById = Dictionary(boxes.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        return Set(cards.filter {
            isAccessBlocked($0, box: boxesById[$0.boxId ?? 0], isSessionUnlocked: isSessionUnlocked)
        }.map(\.id))
    }
}
