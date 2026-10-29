import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func card(_ id: Int64, mode: CardProtectionMode? = .unprotected, boxId: Int64? = 0) -> PrayerCardRecord {
    PrayerCardRecord(
        id: id, title: "Card \(id)", canNotify: false, prayerFrequency: .weekly, isAnswered: false,
        isFavorite: false, isSystem: false, isImported: false, protectionMode: mode, boxId: boxId,
        preArchiveBoxId: nil, systemKey: nil, createdAtTicks: 1, updatedAtTicks: 1)
}

private func box(_ id: Int64, protectAll: Bool, mode: CardProtectionMode) -> CardBoxRecord {
    CardBoxRecord(
        id: id, name: "Box \(id)", isSystem: false, systemKey: nil, sortOrder: 0,
        protectAllCards: protectAll, cardProtectionMode: mode, createdAtTicks: 1, updatedAtTicks: 1)
}

private func prayer(_ id: Int64, card cardId: Int64) -> PrayerRequestRecord {
    PrayerRequestRecord(
        id: id, prayerCardId: cardId, title: "Prayer \(id)", details: nil, canNotify: false,
        prayerFrequency: .weekly, isAnswered: false, answeredAtTicks: nil, notifyHour: nil,
        notifyMinute: nil, notifyDayOfWeek: nil, notifyDayOfMonth: nil, notifyDateTicks: nil,
        isImported: nil, createdAtTicks: 1, updatedAtTicks: 1)
}

private func interaction(_ id: Int64, prayer prayerId: Int64) -> PrayerInteractionRecord {
    PrayerInteractionRecord(
        id: id, prayerId: prayerId, interactionType: "Prayed", interactionAtTicks: 1,
        createdAtTicks: 1, updatedAtTicks: 1)
}

private func junction(_ id: Int64, request: Int64?) -> PrayerCardTagRecord {
    PrayerCardTagRecord(id: id, prayerCardId: 0, prayerTagId: 1, prayerRequestId: request, createdAtTicks: 1)
}

private func split(
    cards: [PrayerCardRecord], boxes: [CardBoxRecord] = [], prayers: [PrayerRequestRecord] = [],
    interactions: [PrayerInteractionRecord] = [], junctions: [PrayerCardTagRecord] = []
) -> ConfidentialExportSplitter.SplitResult {
    ConfidentialExportSplitter.split(
        cards: cards, boxes: boxes, prayers: prayers, interactions: interactions, tagJunctions: junctions)
}

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

@Suite struct ConfidentialExportSplitterTests {
    @Test func ownModeIsConfidential() {
        let r = split(cards: [card(1, mode: .hidden), card(2)])
        #expect(r.confidentialCards.map(\.id) == [1])
        #expect(r.remainingCards.map(\.id) == [2])
    }

    @Test func boxProtectAllCardsInherits() {
        let r = split(
            cards: [card(1, boxId: 10), card(2, boxId: 11)],
            boxes: [box(10, protectAll: true, mode: .lockedVisible), box(11, protectAll: false, mode: .hidden)])
        #expect(r.confidentialCards.map(\.id) == [1])
        #expect(r.remainingCards.map(\.id) == [2])
    }

    @Test func unknownBoxIdUsesOwnMode() {
        let r = split(cards: [card(1, mode: .hidden, boxId: 99), card(2, boxId: 99)])
        #expect(r.confidentialCards.map(\.id) == [1])
        #expect(r.remainingCards.map(\.id) == [2])
    }

    @Test func nilBoxIdIsLoose() {
        let r = split(
            cards: [card(1, mode: .hidden, boxId: nil), card(2, mode: nil, boxId: nil)],
            boxes: [box(5, protectAll: true, mode: .hidden)])
        #expect(r.confidentialCards.map(\.id) == [1])
        #expect(r.remainingCards.map(\.id) == [2])
    }

    @Test func prayersInteractionsJunctionsFollow() {
        let r = split(
            cards: [card(1, mode: .hidden), card(2)],
            prayers: [prayer(100, card: 1), prayer(101, card: 2)],
            interactions: [interaction(500, prayer: 100), interaction(501, prayer: 101)],
            junctions: [junction(900, request: 100), junction(901, request: 101)])
        #expect(r.confidentialPrayers.map(\.id) == [100])
        #expect(r.remainingPrayers.map(\.id) == [101])
        #expect(r.confidentialInteractions.map(\.id) == [500])
        #expect(r.remainingInteractions.map(\.id) == [501])
        #expect(r.confidentialTagJunctions.map(\.id) == [900])
        #expect(r.remainingTagJunctions.map(\.id) == [901])
    }

    @Test func zeroOrNilRequestIdJunctionStaysInRemainder() {
        let r = split(
            cards: [card(1, mode: .hidden)], prayers: [prayer(100, card: 1)],
            junctions: [junction(1, request: 0), junction(2, request: nil)])
        #expect(r.confidentialTagJunctions.isEmpty)
        #expect(r.remainingTagJunctions.map(\.id) == [1, 2])
    }

    @Test func noConfidentialCardsReturnsEmptyLists() {
        let r = split(
            cards: [card(1)], prayers: [prayer(100, card: 1)],
            interactions: [interaction(500, prayer: 100)], junctions: [junction(900, request: 100)])
        #expect(r.confidentialCards.isEmpty)
        #expect(r.confidentialPrayers.isEmpty)
        #expect(r.confidentialInteractions.isEmpty)
        #expect(r.confidentialTagJunctions.isEmpty)
        #expect(r.remainingCards.count == 1)
    }

    private struct Rows: Equatable {
        var cards: [PrayerCardRecord]
        var prayers: [PrayerRequestRecord]
        var interactions: [PrayerInteractionRecord]
        var junctions: [PrayerCardTagRecord]
        var boxes: [CardBoxRecord]

        static func read(_ path: String) throws -> Rows {
            let database = try PrayerDatabase(path: path)
            defer { try? database.close() }
            return try database.dbQueue.read { db in
                let id = Column("Id")
                return Rows(
                    cards: try PrayerCardRecord.order(id).fetchAll(db),
                    prayers: try PrayerRequestRecord.order(id).fetchAll(db),
                    interactions: try PrayerInteractionRecord.order(id).fetchAll(db),
                    junctions: try PrayerCardTagRecord.order(id).fetchAll(db),
                    boxes: try CardBoxRecord.order(id).fetchAll(db))
            }
        }
    }

    private func scratchDirectory() throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("pk_scrub_\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    @Test func scrubbedCopyDropsConfidentialRows() throws {
        let dir = try scratchDirectory()
        defer { try? FileManager.default.removeItem(at: dir) }
        let source = dir.appendingPathComponent("source.db")
        let destination = dir.appendingPathComponent("scrubbed.db")
        try FileManager.default.copyItem(at: repoRoot().appendingPathComponent("contracts/db/long-upgraded.db"), to: source)
        let before = try Data(contentsOf: source)

        let rows = try Rows.read(source.path)
        var cards = rows.cards
        cards[0].protectionMode = .hidden
        let result = split(
            cards: cards, boxes: rows.boxes, prayers: rows.prayers,
            interactions: rows.interactions, junctions: rows.junctions)
        #expect(!result.confidentialCards.isEmpty)
        #expect(!result.confidentialPrayers.isEmpty)

        try ScrubbedDatabase.write(from: source, to: destination, removing: result)

        let scrubbed = try Rows.read(destination.path)
        #expect(Set(scrubbed.cards.map(\.id)).isDisjoint(with: result.confidentialCards.map(\.id)))
        #expect(Set(scrubbed.prayers.map(\.id)).isDisjoint(with: result.confidentialPrayers.map(\.id)))
        #expect(Set(scrubbed.interactions.map(\.id)).isDisjoint(with: result.confidentialInteractions.map(\.id)))
        #expect(Set(scrubbed.junctions.map(\.id)).isDisjoint(with: result.confidentialTagJunctions.map(\.id)))
        #expect(scrubbed.cards.count == rows.cards.count - result.confidentialCards.count)
        #expect(try Data(contentsOf: source) == before)
    }

    @Test func scrubbedCopyWithNoConfidentialKeepsEveryRow() throws {
        let dir = try scratchDirectory()
        defer { try? FileManager.default.removeItem(at: dir) }
        let source = dir.appendingPathComponent("source.db")
        let destination = dir.appendingPathComponent("scrubbed.db")
        try FileManager.default.copyItem(at: repoRoot().appendingPathComponent("contracts/db/long-upgraded.db"), to: source)

        let rows = try Rows.read(source.path)
        let result = split(
            cards: rows.cards.map { var c = $0; c.protectionMode = .unprotected; return c },
            boxes: rows.boxes.map { var b = $0; b.protectAllCards = false; return b },
            prayers: rows.prayers, interactions: rows.interactions, junctions: rows.junctions)
        #expect(result.confidentialCards.isEmpty)

        try ScrubbedDatabase.write(from: source, to: destination, removing: result)

        #expect(try Rows.read(destination.path) == rows)
    }
}
