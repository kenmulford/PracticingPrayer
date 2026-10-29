import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func tempCopy(of source: URL) throws -> String {
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_quickadd_\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: source, to: dest)
    return dest.path
}

private func removeTemp(_ path: String) {
    for suffix in ["", "-journal", "-wal", "-shm"] {
        try? FileManager.default.removeItem(atPath: path + suffix)
    }
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase) throws -> T) throws -> T {
    let path = try tempCopy(of: repoRoot().appendingPathComponent("contracts/db/\(file)"))
    defer { removeTemp(path) }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try body(database)
}

private func count(_ database: PrayerDatabase, _ table: String, where clause: String = "1") throws -> Int {
    try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM \(table) WHERE \(clause)") ?? 0 }
}

@Suite struct QuickAddSaverTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func newCardSavesCardAndTitledPrayers() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .newCard(title: " Family ", boxId: 0),
                prayers: [
                    QuickAddPrayer(title: "A", details: " note "),
                    QuickAddPrayer(title: "  "),
                    QuickAddPrayer(title: "B", details: "   "),
                ])
            let result = try QuickAddSaver(database: database).save(request, now: now)
            #expect(result.savedCount == 2)
            try #expect(count(database, "PrayerCard") == 5)
            try #expect(count(database, "PrayerRequest") == 9)
            let card = try #require(
                try database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: result.cardId) })
            #expect(card.title == "Family")
            let details = try database.dbQueue.read {
                try String?.fetchAll(
                    $0, sql: "SELECT Details FROM PrayerRequest WHERE PrayerCardId = ? ORDER BY Id",
                    arguments: [result.cardId])
            }
            #expect(details == ["note", nil])
        }
    }

    @Test func existingCardSavesPrayers() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(target: .existingCard(id: 1), prayers: [QuickAddPrayer(title: "One")])
            let result = try QuickAddSaver(database: database).save(request, now: now)
            #expect(result.cardId == 1)
            try #expect(count(database, "PrayerCard") == 4)
            try #expect(count(database, "PrayerRequest") == 8)
        }
    }

    @Test func prayerInsertFailureLeavesNoCard() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write {
                try $0.execute(sql: """
                    CREATE TRIGGER boom BEFORE INSERT ON PrayerRequest WHEN NEW.Title = 'Boom'
                    BEGIN SELECT RAISE(ABORT, 'boom'); END
                    """)
            }
            let request = QuickAddRequest(
                target: .newCard(title: "Family", boxId: 0),
                prayers: [QuickAddPrayer(title: "A"), QuickAddPrayer(title: "Boom")])
            #expect(throws: (any Error).self) { try QuickAddSaver(database: database).save(request, now: now) }
            try #expect(count(database, "PrayerCard") == 4)
            try #expect(count(database, "PrayerRequest") == 7)
        }
    }

    @Test func blankCardTitleThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .newCard(title: "  ", boxId: 0), prayers: [QuickAddPrayer(title: "A")])
            #expect(throws: CardMutationError.emptyTitle) {
                try QuickAddSaver(database: database).save(request, now: now)
            }
            try #expect(count(database, "PrayerRequest") == 7)
        }
    }

    @Test func missingBoxThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .newCard(title: "X", boxId: 999), prayers: [QuickAddPrayer(title: "A")])
            #expect(throws: CardMutationError.boxNotFound(999)) {
                try QuickAddSaver(database: database).save(request, now: now)
            }
            try #expect(count(database, "PrayerCard") == 4)
        }
    }

    @Test func missingCardThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .existingCard(id: 999), prayers: [QuickAddPrayer(title: "A")])
            #expect(throws: PrayerStoreError.cardNotFound(999)) {
                try QuickAddSaver(database: database).save(request, now: now)
            }
        }
    }

    @Test func noTitledPrayerThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .newCard(title: "X", boxId: 0), prayers: [QuickAddPrayer(title: " ")])
            #expect(throws: PrayerStoreError.emptyTitle) {
                try QuickAddSaver(database: database).save(request, now: now)
            }
            try #expect(count(database, "PrayerCard") == 4)
            try #expect(count(database, "PrayerRequest") == 7)
        }
    }

    @Test func savedPrayersAreNotImported() throws {
        try withCopy("long-upgraded.db") { database in
            let request = QuickAddRequest(
                target: .newCard(title: "X", boxId: 0), prayers: [QuickAddPrayer(title: "A")])
            let result = try QuickAddSaver(database: database).save(request, now: now)
            #expect(
                try count(database, "PrayerRequest", where: "PrayerCardId = \(result.cardId) AND IsImported = 0") == 1)
            try #expect(count(database, "PrayerCard", where: "Id = \(result.cardId) AND IsImported = 0") == 1)
        }
    }
}
