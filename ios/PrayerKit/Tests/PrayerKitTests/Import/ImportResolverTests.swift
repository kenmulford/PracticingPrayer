import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func contractDbURL(_ file: String) -> URL {
    repoRoot().appendingPathComponent("contracts/db/\(file)")
}

private func tempCopy(of source: URL) throws -> String {
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_import_\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: source, to: dest)
    return dest.path
}

private func removeTemp(_ path: String) {
    for suffix in ["", "-journal", "-wal", "-shm"] {
        try? FileManager.default.removeItem(atPath: path + suffix)
    }
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase) throws -> T) throws -> T {
    let path = try tempCopy(of: contractDbURL(file))
    defer { removeTemp(path) }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try body(database)
}

private func count(_ database: PrayerDatabase, _ table: String, where clause: String = "1") throws -> Int {
    try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM \(table) WHERE \(clause)") ?? 0 }
}

private func titles(_ prayers: [SharedPrayer]) -> [String] { prayers.map(\.title) }

@Suite struct ImportResolverTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    let posix = Locale(identifier: "en_US_POSIX")

    private func resolver(_ database: PrayerDatabase) -> ImportResolver {
        ImportResolver(database: database, locale: posix)
    }

    @Test func newCardCreatesImportedLooseCardWithDefaultTitle() throws {
        try withCopy("long-upgraded.db") { database in
            let payload = SharePayload.card(
                title: "Ignored",
                requests: [SharedPrayer(title: "One", notes: nil), SharedPrayer(title: "Two", notes: "n")])
            let sut = resolver(database)
            let local = DateComponents(calendar: .current, year: 2026, month: 3, day: 5, hour: 12)
            let date = try #require(local.date)
            let result = try sut.save(sut.resolve(payload, to: .newCard), now: date)
            #expect(result.savedCount == 2)
            let card = try #require(
                try database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: result.cardId) })
            #expect(card.title == "Imported Mar 5")
            #expect(card.isImported == true)
            #expect(card.boxId == 0)
            let c1 = try count(database, "PrayerRequest", where: "PrayerCardId = \(result.cardId) AND IsImported = 1")
            #expect(c1 == 2)
        }
    }

    @Test func newCardUsesBoxId() throws {
        try withCopy("long-upgraded.db") { database in
            let sut = resolver(database)
            let payload = SharePayload.request(SharedPrayer(title: "One", notes: nil))
            let result = try sut.save(sut.resolve(payload, to: .newCard), boxId: 2, now: now)
            let card = try #require(
                try database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: result.cardId) })
            #expect(card.boxId == 2)
        }
    }

    @Test func newCardRunsNoMatching() throws {
        try withCopy("long-upgraded.db") { database in
            let payload = SharePayload.request(SharedPrayer(title: "Sample Prayer Entry 1", notes: nil))
            let resolution = resolver(database).resolve(payload, to: .newCard)
            #expect(resolution.toImport.count == 1)
            #expect(resolution.alreadyOnCard.isEmpty)
        }
    }

    @Test func existingCardSplitsDuplicates() throws {
        try withCopy("long-upgraded.db") { database in
            let resolution = resolver(database).resolve(splitPayload, to: .existingCard(id: 1))
            #expect(titles(resolution.alreadyOnCard) == ["sample  prayer ENTRY 1"])
            #expect(titles(resolution.toImport) == ["New One"])
        }
    }

    @Test func matchNormalizesQuotes() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write {
                try $0.execute(sql: "UPDATE PrayerRequest SET Title = ? WHERE Id = 1", arguments: ["Mom\u{2019}s surgery"])
            }
            let payload = SharePayload.request(SharedPrayer(title: "Mom's surgery", notes: nil))
            let resolution = resolver(database).resolve(payload, to: .existingCard(id: 1))
            #expect(titles(resolution.alreadyOnCard) == ["Mom's surgery"])
        }
    }

    @Test func existingCardSavesOnlyToImport() throws {
        try withCopy("long-upgraded.db") { database in
            let sut = resolver(database)
            let result = try sut.save(sut.resolve(splitPayload, to: .existingCard(id: 1)), now: now)
            #expect(result.cardId == 1)
            #expect(result.savedCount == 1)
            let c2 = try count(database, "PrayerRequest", where: "PrayerCardId = 1")
            #expect(c2 == 5)
            let c3 = try count(database, "PrayerRequest", where: "Title = 'New One' AND IsImported = 1")
            #expect(c3 == 1)
        }
    }

    @Test func movedDuplicateIsSaved() throws {
        try withCopy("long-upgraded.db") { database in
            let sut = resolver(database)
            var resolution = sut.resolve(splitPayload, to: .existingCard(id: 1))
            resolution.moveToImport(at: 0)
            _ = try sut.save(resolution, now: now)
            let c4 = try count(database, "PrayerRequest", where: "PrayerCardId = 1")
            #expect(c4 == 6)
        }
    }

    @Test func allDuplicatesRejectsSave() throws {
        try withCopy("long-upgraded.db") { database in
            let payload = SharePayload.card(
                title: "X",
                requests: [
                    SharedPrayer(title: "Fixture Prayer 1", notes: nil),
                    SharedPrayer(title: "Fixture Prayer 2", notes: nil),
                ])
            let sut = resolver(database)
            let resolution = sut.resolve(payload, to: .existingCard(id: 2))
            #expect(resolution.isAllDuplicates)
            #expect(throws: ImportResolverError.nothingToSave) { try sut.save(resolution, now: now) }
            let c5 = try count(database, "PrayerRequest")
            #expect(c5 == 7)
        }
    }

    @Test func emptyPayloadRejectsSave() throws {
        try withCopy("long-upgraded.db") { database in
            let sut = resolver(database)
            let resolution = sut.resolve(.card(title: "X", requests: []), to: .newCard)
            #expect(resolution.isEmpty)
            #expect(throws: ImportResolverError.nothingToSave) { try sut.save(resolution, now: now) }
            let c6 = try count(database, "PrayerCard")
            #expect(c6 == 4)
        }
    }

    @Test func lookupFailureImportsEverything() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write { try $0.execute(sql: "DROP TABLE PrayerCardTag") }
            let payload = SharePayload.request(SharedPrayer(title: "Sample Prayer Entry 1", notes: nil))
            let resolution = resolver(database).resolve(payload, to: .existingCard(id: 1))
            #expect(resolution.lookupError == .lookupFailed)
            #expect(resolution.toImport.count == 1)
            let c7 = try count(database, "PrayerRequest")
            #expect(c7 == 7)
        }
    }

    @Test func writeFailureRollsBack() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write {
                try $0.execute(
                    sql: """
                        CREATE TRIGGER boom BEFORE INSERT ON PrayerRequest WHEN NEW.Title = 'Boom'
                        BEGIN SELECT RAISE(ABORT, 'boom'); END
                        """)
            }
            let payload = SharePayload.card(
                title: "X", requests: [SharedPrayer(title: "A", notes: nil), SharedPrayer(title: "Boom", notes: nil)])
            let sut = resolver(database)
            #expect(throws: ImportResolverError.writeFailed(savedCount: 0)) {
                try sut.save(sut.resolve(payload, to: .newCard), now: now)
            }
            let c8 = try count(database, "PrayerCard")
            #expect(c8 == 4)
            let c9 = try count(database, "PrayerRequest")
            #expect(c9 == 7)
        }
    }

    @Test func missingExistingCardFailsWrite() throws {
        try withCopy("long-upgraded.db") { database in
            let sut = resolver(database)
            let resolution = sut.resolve(
                .request(SharedPrayer(title: "A", notes: nil)), to: .existingCard(id: 999))
            #expect(throws: ImportResolverError.writeFailed(savedCount: 0)) { try sut.save(resolution, now: now) }
            let c10 = try count(database, "PrayerRequest")
            #expect(c10 == 7)
        }
    }

    @Test func blankTitlesDropped() throws {
        try withCopy("long-upgraded.db") { database in
            let payload = SharePayload.card(
                title: "X", requests: [SharedPrayer(title: "A", notes: nil), SharedPrayer(title: "   ", notes: nil)])
            let sut = resolver(database)
            let result = try sut.save(sut.resolve(payload, to: .newCard), now: now)
            #expect(result.savedCount == 1)
        }
    }

    @Test func savedTextNormalized() throws {
        try withCopy("long-upgraded.db") { database in
            let payload = SharePayload.request(SharedPrayer(title: "  \u{201C}Hi\u{201D}  ", notes: "  "))
            let sut = resolver(database)
            let result = try sut.save(sut.resolve(payload, to: .existingCard(id: 1)), now: now)
            #expect(result.savedCount == 1)
            let row = try database.dbQueue.read {
                try Row.fetchOne($0, sql: "SELECT Title, Details FROM PrayerRequest ORDER BY Id DESC LIMIT 1")
            }
            #expect(row?["Title"] as String? == "\"Hi\"")
            #expect(row?["Details"] as String? == nil)
        }
    }

    private var splitPayload: SharePayload {
        .card(
            title: "X",
            requests: [
                SharedPrayer(title: "sample  prayer ENTRY 1", notes: "other"),
                SharedPrayer(title: "New One", notes: nil),
            ])
    }
}
