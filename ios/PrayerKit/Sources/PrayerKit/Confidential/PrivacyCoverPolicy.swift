import Foundation
import GRDB

public enum PrivacyCoverPolicy {
    public static func isCovered(isSceneActive: Bool, hasProtectedCards: Bool) -> Bool {
        !isSceneActive && hasProtectedCards
    }

    public static func hasProtectedCards(cards: [PrayerCardRecord], boxes: [CardBoxRecord]) -> Bool {
        !ProtectionPolicy.blockedCardIds(cards: cards, boxes: boxes, isSessionUnlocked: false).isEmpty
    }

    /// Fails closed: an unreadable database is treated as holding protected cards.
    public static func hasProtectedCards(in database: PrayerDatabase) -> Bool {
        do {
            let (cards, boxes) = try database.dbQueue.read { db in
                (try PrayerCardRecord.fetchAll(db), try CardBoxRecord.fetchAll(db))
            }
            return hasProtectedCards(cards: cards, boxes: boxes)
        } catch {
            return true
        }
    }
}
