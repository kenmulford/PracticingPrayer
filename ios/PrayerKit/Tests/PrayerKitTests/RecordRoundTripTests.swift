import Foundation
import GRDB
import Testing
@testable import PrayerKit

private let tableNames = [
    "PrayerCard", "PrayerRequest", "PrayerTag", "PrayerCardTag",
    "PrayerInteraction", "UserColor", "CardBox",
]

private struct Fixture: CustomTestStringConvertible, Sendable {
    let file: String
    let counts: [Int]
    var testDescription: String { file }

    static let fresh = Fixture(file: "fresh-install.db", counts: [1, 0, 1, 0, 0, 8, 2])
    static let longUpgraded = Fixture(file: "long-upgraded.db", counts: [4, 7, 6, 2, 3, 8, 2])
}

private struct AllRecords: Equatable {
    var cards: [PrayerCardRecord]
    var requests: [PrayerRequestRecord]
    var tags: [PrayerTagRecord]
    var cardTags: [PrayerCardTagRecord]
    var interactions: [PrayerInteractionRecord]
    var colors: [UserColorRecord]
    var boxes: [CardBoxRecord]

    var counts: [Int] {
        [cards.count, requests.count, tags.count, cardTags.count, interactions.count, colors.count, boxes.count]
    }

    static func fetch(_ db: Database) throws -> AllRecords {
        let id = Column("Id")
        return AllRecords(
            cards: try PrayerCardRecord.order(id).fetchAll(db),
            requests: try PrayerRequestRecord.order(id).fetchAll(db),
            tags: try PrayerTagRecord.order(id).fetchAll(db),
            cardTags: try PrayerCardTagRecord.order(id).fetchAll(db),
            interactions: try PrayerInteractionRecord.order(id).fetchAll(db),
            colors: try UserColorRecord.order(id).fetchAll(db),
            boxes: try CardBoxRecord.order(id).fetchAll(db)
        )
    }

    func update(_ db: Database) throws {
        for r in cards { try r.update(db) }
        for r in requests { try r.update(db) }
        for r in tags { try r.update(db) }
        for r in cardTags { try r.update(db) }
        for r in interactions { try r.update(db) }
        for r in colors { try r.update(db) }
        for r in boxes { try r.update(db) }
    }

    func insert(_ db: Database) throws {
        for r in cards { try r.insert(db) }
        for r in requests { try r.insert(db) }
        for r in tags { try r.insert(db) }
        for r in cardTags { try r.insert(db) }
        for r in interactions { try r.insert(db) }
        for r in colors { try r.insert(db) }
        for r in boxes { try r.insert(db) }
    }
}

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private func contractDbURL(_ file: String) -> URL {
    repoRoot().appendingPathComponent("contracts/db/\(file)")
}

private func tempCopy(of source: URL) throws -> String {
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_records_\(UUID().uuidString).db")
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

private func rawRows(_ db: Database) throws -> [String: [Row]] {
    var result: [String: [Row]] = [:]
    for table in tableNames {
        result[table] = try Row.fetchAll(db, sql: "SELECT * FROM \(table) ORDER BY Id")
    }
    return result
}

private func emptyAllTables(_ db: Database) throws {
    for table in tableNames { try db.execute(sql: "DELETE FROM \(table)") }
    try db.execute(sql: "DELETE FROM sqlite_sequence")
}

private func longUpgradedRecords() throws -> AllRecords {
    try withCopy(Fixture.longUpgraded.file) { database in
        try database.dbQueue.read { try AllRecords.fetch($0) }
    }
}

@Suite struct RecordRoundTripTests {
    @Test(arguments: [Fixture.fresh, Fixture.longUpgraded])
    fileprivate func everyRowDecodes(fixture: Fixture) throws {
        try withCopy(fixture.file) { database in
            let records = try database.dbQueue.read { try AllRecords.fetch($0) }
            #expect(records.counts == fixture.counts)
        }
    }

    @Test(arguments: [Fixture.fresh, Fixture.longUpgraded])
    fileprivate func writeBackLeavesRowsEqual(fixture: Fixture) throws {
        try withCopy(fixture.file) { database in
            let (before, decoded) = try database.dbQueue.read { db in
                (try rawRows(db), try AllRecords.fetch(db))
            }
            try database.dbQueue.write { try decoded.update($0) }
            let (after, reread) = try database.dbQueue.read { db in
                (try rawRows(db), try AllRecords.fetch(db))
            }
            #expect(after == before)
            #expect(reread == decoded)
        }
    }

    @Test func textAnsweredAtKeepsTextStorage() throws {
        try withCopy(Fixture.fresh.file) { database in
            try database.dbQueue.write { db in
                try db.execute(sql: "DROP TABLE PrayerRequest")
                try db.execute(sql: """
                    CREATE TABLE "PrayerRequest" (
                    "Id" integer primary key autoincrement not null,
                    "PrayerCardId" integer, "Title" varchar(100), "Details" varchar(1000),
                    "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer,
                    "AnsweredAt" TEXT, "CreatedAt" bigint, "UpdatedAt" bigint,
                    "NotifyHour" integer, "NotifyMinute" integer, "NotifyDayOfWeek" integer,
                    "NotifyDayOfMonth" integer, "NotifyDate" bigint, "IsImported" integer)
                    """)
                try db.execute(sql: """
                    INSERT INTO PrayerRequest (Id, PrayerCardId, Title, CanNotify, PrayerFrequency,
                        IsAnswered, AnsweredAt, CreatedAt, UpdatedAt)
                    VALUES (1, 1, 'Text ticks', 0, 0, 1, '638460882450000000', 638460882450000000, 638460882450000000)
                    """)
            }
            let decoded = try database.dbQueue.read { try PrayerRequestRecord.fetchAll($0) }
            #expect(decoded.count == 1)
            #expect(decoded.first?.answeredAtTicks == 638_460_882_450_000_000)

            try database.dbQueue.write { db in
                for record in decoded { try record.update(db) }
            }

            let storage = try database.dbQueue.read {
                try String.fetchOne($0, sql: "SELECT typeof(\"AnsweredAt\") FROM PrayerRequest")
            }
            let reread = try database.dbQueue.read { try PrayerRequestRecord.fetchAll($0) }
            #expect(storage == "text")
            #expect(reread == decoded)
        }
    }

    @Test func emptyTablesDecodeToEmpty() throws {
        try withCopy(Fixture.fresh.file) { database in
            let records = try database.dbQueue.write { db -> AllRecords in
                for table in tableNames { try db.execute(sql: "DELETE FROM \(table)") }
                return try AllRecords.fetch(db)
            }
            #expect(records.counts == [0, 0, 0, 0, 0, 0, 0])
        }
    }

    @Test func unknownPrayerFrequencyThrows() throws {
        try withCopy(Fixture.longUpgraded.file) { database in
            try database.dbQueue.write {
                try $0.execute(sql: "UPDATE PrayerCard SET PrayerFrequency = 99 WHERE Id = (SELECT MIN(Id) FROM PrayerCard)")
            }
            #expect(throws: RecordDecodingError.unknownEnumValue(table: "PrayerCard", column: "PrayerFrequency", value: 99)) {
                _ = try database.dbQueue.read { try PrayerCardRecord.fetchAll($0) }
            }
        }
    }

    @Test func unknownCardProtectionModeThrows() throws {
        try withCopy(Fixture.longUpgraded.file) { database in
            try database.dbQueue.write {
                try $0.execute(sql: "UPDATE CardBox SET CardProtectionMode = 7 WHERE Id = (SELECT MIN(Id) FROM CardBox)")
            }
            #expect(throws: RecordDecodingError.unknownEnumValue(table: "CardBox", column: "CardProtectionMode", value: 7)) {
                _ = try database.dbQueue.read { try CardBoxRecord.fetchAll($0) }
            }
        }
    }

    @Test func malformedTicksThrows() throws {
        try withCopy(Fixture.longUpgraded.file) { database in
            try database.dbQueue.write {
                try $0.execute(sql: "UPDATE PrayerRequest SET CreatedAt = 'abc' WHERE Id = (SELECT MIN(Id) FROM PrayerRequest)")
            }
            #expect(throws: RecordDecodingError.invalidDate(table: "PrayerRequest", column: "CreatedAt")) {
                _ = try database.dbQueue.read { try PrayerRequestRecord.fetchAll($0) }
            }
        }
    }

    @Test func unmappedColumnUntouchedOnWrite() throws {
        try withCopy(Fixture.longUpgraded.file) { database in
            try database.dbQueue.write { db in
                try db.execute(sql: "ALTER TABLE PrayerCard ADD COLUMN \"FutureColumn\" TEXT")
                try db.execute(sql: "UPDATE PrayerCard SET FutureColumn = 'kept'")
            }
            let cards = try database.dbQueue.read { try PrayerCardRecord.fetchAll($0) }
            try database.dbQueue.write { db in
                for card in cards { try card.update(db) }
            }
            let values = try database.dbQueue.read {
                try String?.fetchAll($0, sql: "SELECT FutureColumn FROM PrayerCard")
            }
            #expect(values.count == Fixture.longUpgraded.counts[0])
            #expect(values.allSatisfy { $0 == "kept" })
        }
    }

    @Test(arguments: [Fixture.fresh, Fixture.longUpgraded])
    fileprivate func writeBackKeepsUserVersionAndJournal(fixture: Fixture) throws {
        let path = try tempCopy(of: contractDbURL(fixture.file))
        defer { removeTemp(path) }
        let probe = try DatabaseQueue(path: path)
        let versionBefore = try probe.read { try Int.fetchOne($0, sql: "PRAGMA user_version") }
        try probe.close()

        let database = try PrayerDatabase(path: path)
        let records = try database.dbQueue.read { try AllRecords.fetch($0) }
        try database.dbQueue.write { try records.update($0) }
        let (version, journal) = try database.dbQueue.read { db in
            (try Int.fetchOne(db, sql: "PRAGMA user_version"), try String.fetchOne(db, sql: "PRAGMA journal_mode"))
        }
        try database.close()

        #expect(version == versionBefore)
        #expect(journal == "delete")
    }

    @Test func nativeWriteMatchesLongUpgraded() throws {
        let expected = try longUpgradedRecords()
        try withCopy(Fixture.fresh.file) { database in
            try database.dbQueue.write { db in
                try emptyAllTables(db)
                try expected.insert(db)
            }
            let actual = try database.dbQueue.read { try AllRecords.fetch($0) }
            #expect(actual == expected)
        }
    }

    @Test func committedNativeDbMatchesLongUpgraded() throws {
        let expected = try longUpgradedRecords()
        let actual = try withCopy("native-written.db") { database in
            try database.dbQueue.read { try AllRecords.fetch($0) }
        }
        #expect(actual == expected)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["PRAYERKIT_WRITE_CONTRACT_DB"] == "1"))
    func writeNativeContractDb() throws {
        let expected = try longUpgradedRecords()
        let work = try tempCopy(of: contractDbURL(Fixture.fresh.file))
        defer { removeTemp(work) }
        let database = try PrayerDatabase(path: work)
        try database.dbQueue.write { db in
            try emptyAllTables(db)
            try expected.insert(db)
        }
        try database.close()

        let target = contractDbURL("native-written.db")
        try? FileManager.default.removeItem(at: target)
        try FileManager.default.copyItem(at: URL(fileURLWithPath: work), to: target)

        #expect(!FileManager.default.fileExists(atPath: target.path + "-journal"))
    }
}
