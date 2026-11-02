import PrayerKit

enum CollectionRootState: Equatable { case normal, locked, hidden }

struct CollectionRootSections {
    var user: [CardSection] = []
    var system: [CardSection] = []
}

/// The one place the Collections root reads a box's protection. It never reads cards or prayers, so a
/// locked root reveals neither a count nor a masked prayer.
enum CollectionProtection {
    static func rootState(of box: CardBoxRecord?, isSessionUnlocked: Bool) -> CollectionRootState {
        guard !isSessionUnlocked, let box, !box.isSystem, box.protectAllCards else { return .normal }
        switch box.cardProtectionMode {
        case .lockedVisible: return .locked
        case .hidden: return .hidden
        case .unprotected: return .normal
        }
    }

    static func rootSections(_ sections: [CardSection], isSessionUnlocked: Bool) -> CollectionRootSections {
        CollectionRootSections(
            user: sections.filter {
                $0.box?.isSystem != true
                    && rootState(of: $0.box, isSessionUnlocked: isSessionUnlocked) != .hidden
            },
            system: sections.filter { $0.box?.isSystem == true })
    }
}
