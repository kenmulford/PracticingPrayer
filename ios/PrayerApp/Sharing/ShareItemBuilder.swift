import Foundation
import PrayerKit

enum ShareItemBuilder {
    private static let footer = "(Shared via Practicing Prayer)"

    static func prayer(_ prayer: PrayerRequestRecord) throws -> ShareItem {
        switch try ShareCodec.encode(request: prayer) {
        case .link(let link):
            var body = "\(link)\n\n\(TextNormalization.normalizeQuotes(prayer.title))"
            if let details = prayer.details, !details.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                body += "\n\(TextNormalization.normalizeQuotes(details))"
            }
            return ShareItem(title: prayer.title, content: .text("\(body)\n\n\(footer)"))
        case .file(let data):
            return ShareItem(title: prayer.title, content: .file(PrayerCardFile(title: prayer.title, data: data)))
        }
    }

    static func card(_ card: PrayerCardRecord, prayers: [PrayerRequestRecord]) throws -> ShareItem? {
        let shared = prayers.filter {
            !$0.isAnswered && !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        guard !shared.isEmpty else { return nil }
        switch try ShareCodec.encode(card: card, prayers: shared) {
        case .link(let link):
            let count = shared.count == 1 ? "1 request" : "\(shared.count) requests"
            let body = "\(link)\n\n\(TextNormalization.normalizeQuotes(card.title))\n+ \(count)\n\n\(footer)"
            return ShareItem(title: card.title, content: .text(body))
        case .file(let data):
            return ShareItem(title: card.title, content: .file(PrayerCardFile(title: card.title, data: data)))
        }
    }

    static func card(_ card: PrayerCardRecord, database: PrayerDatabase) throws -> ShareItem? {
        let prayers = try PrayerStore(database: database).prayers(cardId: card.id).map(\.record)
        return try self.card(card, prayers: prayers)
    }

    static func isShareable(_ card: PrayerCardRecord, box: CardBoxRecord?) -> Bool {
        ProtectionPolicy.effectiveMode(of: card, box: box) == .unprotected
    }

    static func isShareable(cardId: Int64, database: PrayerDatabase) throws -> Bool {
        try database.effectiveProtectionMode(ofCard: cardId) == .unprotected
    }

    /// What a row's Share menu item needs: hidden when the card is protected, disabled when the share
    /// content cannot be built, so a failure never leaves a live-looking item that does nothing.
    struct RowShare {
        var isShown = false
        var item: ShareItem?

        static func card(_ card: PrayerCardRecord, database: PrayerDatabase) -> RowShare {
            guard (try? isShareable(cardId: card.id, database: database)) == true else { return RowShare() }
            return RowShare(isShown: true, item: try? ShareItemBuilder.card(card, database: database))
        }

        static func prayer(_ prayer: PrayerRequestRecord, database: PrayerDatabase) -> RowShare {
            guard (try? isShareable(cardId: prayer.prayerCardId, database: database)) == true else { return RowShare() }
            return RowShare(isShown: true, item: try? ShareItemBuilder.prayer(prayer))
        }
    }

    nonisolated static func sanitizedFileName(_ name: String) -> String {
        let cleaned = String(name.unicodeScalars.filter {
            $0 != "/" && $0 != ":" && !CharacterSet.controlCharacters.contains($0)
        }).trimmingCharacters(in: .whitespacesAndNewlines)
        return cleaned.isEmpty ? "shared-prayer" : cleaned
    }
}
