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
        let list = try database.cardList(isSessionUnlocked: true)
        for section in list.sections {
            if let item = section.cards.first(where: { $0.card.id == cardId }) {
                return isShareable(item.card, box: section.box)
            }
        }
        return false
    }

    nonisolated static func sanitizedFileName(_ name: String) -> String {
        let cleaned = String(name.unicodeScalars.filter {
            $0 != "/" && $0 != ":" && !CharacterSet.controlCharacters.contains($0)
        }).trimmingCharacters(in: .whitespacesAndNewlines)
        return cleaned.isEmpty ? "shared-prayer" : cleaned
    }
}
