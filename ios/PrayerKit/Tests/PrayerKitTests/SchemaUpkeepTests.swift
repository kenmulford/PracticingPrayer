import Foundation
import GRDB
import Testing

@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private let legacySchema = [
    #"CREATE TABLE "PrayerCard" ("Id" integer primary key autoincrement not null, "Title" varchar(100), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerRequest" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "Title" varchar(100), "Details" varchar(1000), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerTag" ("Id" integer primary key autoincrement not null, "Name" varchar(100), "Color" varchar(9), "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerCardTag" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "PrayerTagId" integer, "CreatedAt" bigint)"#,
    #"CREATE TABLE "PrayerInteraction" ("Id" integer primary key autoincrement not null, "PrayerId" integer, "InteractionType" varchar(50), "InteractionAt" bigint, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "UserColor" ("Id" integer primary key autoincrement not null, "HexValue" varchar, "CreatedAt" bigint)"#,
    #"CREATE TABLE "CardBox" ("Id" integer primary key autoincrement not null, "Name" varchar(50), "IsSystem" integer, "SystemKey" varchar(20), "SortOrder" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
]

private let tables = ["PrayerCard", "PrayerRequest", "PrayerTag", "PrayerCardTag", "PrayerInteraction", "UserColor", "CardBox"]

private func ticks(_ year: Int, _ month: Int, _ day: Int, _ hour: Int, _ minute: Int) -> Int64 {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(identifier: "UTC")!
    let date = calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))!
    return TicksDate.unixEpochTicks + Int64(date.timeIntervalSince1970) * 10_000_000
}

@Suite struct SchemaUpkeepTests {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    private func tempPath() -> String {
        FileManager.default.temporaryDirectory.appendingPathComponent("pk_upkeep_\(UUID().uuidString).db").path
    }

    private func remove(_ path: String) {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }

    private func emptyFile() -> String {
        let path = tempPath()
        FileManager.default.createFile(atPath: path, contents: Data())
        return path
    }

    private func legacyFile(_ extra: [String] = []) throws -> String {
        let path = tempPath()
        let queue = try DatabaseQueue(path: path)
        try queue.write { db in
            for sql in legacySchema + extra { try db.execute(sql: sql) }
        }
        try queue.close()
        return path
    }

    private func fixtureCopy(_ file: String) throws -> String {
        let path = tempPath()
        try FileManager.default.copyItem(
            atPath: repoRoot().appendingPathComponent("contracts/db/\(file)").path, toPath: path)
        return path
    }

    private func upgraded(_ path: String, log: (String) -> Void = { _ in }) throws -> PrayerDatabase {
        let database = try PrayerDatabase(path: path)
        try SchemaUpkeep.run(on: database, now: now, log: log)
        return database
    }

    private func snapshot(_ database: PrayerDatabase) throws -> [String: [Row]] {
        try database.dbQueue.read { db in
            var result = [String: [Row]]()
            result["sqlite_master"] = try Row.fetchAll(
                db, sql: "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY name")
            let names = try String.fetchAll(
                db, sql: "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            for name in names {
                result[name] = try Row.fetchAll(db, sql: #"SELECT * FROM "\#(name)" ORDER BY Id"#)
            }
            return result
        }
    }

    @Test func emptyFileCreatesTablesAndSeedsTwoBoxes() throws {
        let path = emptyFile()
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            let existing = try String.fetchAll(
                db, sql: "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
            #expect(Set(existing) == Set(tables))
            let boxes = try Row.fetchAll(db, sql: "SELECT SystemKey, SortOrder FROM CardBox ORDER BY SortOrder")
            #expect(boxes.map { [$0["SystemKey"] as String, String($0["SortOrder"] as Int)] } == [["system", "900"], ["archived", "999"]])
            for table in tables where table != "CardBox" {
                #expect(try Int.fetchOne(db, sql: #"SELECT COUNT(*) FROM "\#(table)""#) == 0)
            }
        }
    }

    @Test func legacyDatabaseGainsEveryColumnWithDefaults() throws {
        let path = try legacyFile()
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        let expected: [(String, String, String?)] = [
            ("PrayerCard", "IsFavorite", "0"), ("PrayerCard", "ProtectionMode", "0"),
            ("PrayerRequest", "AnsweredAt", nil), ("PrayerCardTag", "PrayerRequestId", "0"),
            ("PrayerCard", "IsSystem", "0"), ("PrayerCard", "IsImported", "0"),
            ("PrayerRequest", "IsImported", "0"), ("PrayerCard", "BoxId", "0"),
            ("PrayerCard", "SystemKey", nil), ("PrayerCard", "PreArchiveBoxId", nil),
            ("CardBox", "ProtectAllCards", "0"), ("CardBox", "CardProtectionMode", "0"),
            ("UserColor", "IsDefault", "0"), ("PrayerTag", "IsSystem", "0"),
            ("PrayerRequest", "NotifyHour", "9"), ("PrayerRequest", "NotifyMinute", "0"),
            ("PrayerRequest", "NotifyDayOfWeek", "-1"), ("PrayerRequest", "NotifyDayOfMonth", "-1"),
            ("PrayerRequest", "NotifyDate", nil),
        ]
        #expect(expected.count == 19)
        try database.dbQueue.read { db in
            for (table, column, defaultValue) in expected {
                let info = try Row.fetchAll(db, sql: #"PRAGMA table_info("\#(table)")"#)
                let match = info.first { ($0["name"] as String) == column }
                #expect(match != nil, "\(table).\(column) missing")
                #expect((match?["dflt_value"] as String?) == defaultValue, "\(table).\(column) default")
            }
        }
    }

    @Test func legacyRowsKeptAndOnlyListedDataChanges() throws {
        let path = try legacyFile([
            "INSERT INTO PrayerCard (Id, Title) VALUES (1, 'Quick Add'), (2, 'Plain')",
            "INSERT INTO PrayerRequest (Id, PrayerCardId, Title, PrayerFrequency, CreatedAt) VALUES (1, 2, 'p', 1, 638000000000000000)",
            "INSERT INTO PrayerTag (Id, Name) VALUES (1, 'a')",
            "INSERT INTO PrayerInteraction (Id, PrayerId) VALUES (1, 1)",
            "INSERT INTO UserColor (Id, HexValue) VALUES (1, '#123456')",
            "INSERT INTO CardBox (Id, Name, IsSystem, SystemKey, SortOrder) VALUES (1, 'Mine', 0, NULL, 1)",
        ])
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            for table in ["PrayerCard", "PrayerRequest", "PrayerTag", "PrayerInteraction", "UserColor"] {
                #expect(try Int64.fetchAll(db, sql: #"SELECT Id FROM "\#(table)" WHERE Id = 1"#) == [1], "\(table)")
            }
            #expect(try Int.fetchOne(db, sql: "SELECT COUNT(*) FROM PrayerCard") == 2)
            #expect(try Int.fetchOne(db, sql: "SELECT COUNT(*) FROM CardBox WHERE Name = 'Mine' AND Id = 1") == 1)
            let systemBox = try Int64.fetchOne(db, sql: "SELECT Id FROM CardBox WHERE SystemKey = 'system'")
            let quickAdd = try Row.fetchOne(db, sql: "SELECT IsSystem, SystemKey, BoxId FROM PrayerCard WHERE Id = 1")
            #expect(quickAdd?["IsSystem"] as Int? == 1)
            #expect(quickAdd?["SystemKey"] as String? == "quick_add")
            #expect(quickAdd?["BoxId"] as Int64? == systemBox)
            #expect(try Int.fetchOne(db, sql: "SELECT BoxId FROM PrayerCard WHERE Id = 2") == 0)
        }
    }

    @Test func dropsPrayerRequestTagWhenPresent() throws {
        let path = try legacyFile([#"CREATE TABLE "PrayerRequestTag" ("Id" integer primary key, "PrayerRequestId" integer)"#])
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            let actual166 = try db.tableExists("PrayerRequestTag")
            #expect(actual166 == false)
        }
    }

    @Test func orphanCleanupsDeleteOnlyOrphans() throws {
        let path = emptyFile()
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.write { db in
            try db.execute(sql: "INSERT INTO PrayerRequest (Id, Title) VALUES (1, 'p')")
            try db.execute(sql: "INSERT INTO PrayerInteraction (Id, PrayerId) VALUES (1, 1), (2, 99)")
            try db.execute(sql: "INSERT INTO PrayerCardTag (Id, PrayerTagId, PrayerRequestId) VALUES (1, 1, 1), (2, 1, 99), (3, 1, 0)")
            try SchemaUpkeep.cleanOrphanInteractions(db)
            try SchemaUpkeep.cleanOrphanCardTags(db)
        }
        try database.dbQueue.read { db in
            let actual183 = try Int64.fetchAll(db, sql: "SELECT Id FROM PrayerInteraction ORDER BY Id")
            #expect(actual183 == [1])
            let actual184 = try Int64.fetchAll(db, sql: "SELECT Id FROM PrayerCardTag ORDER BY Id")
            #expect(actual184 == [1, 3])
        }
    }

    @Test func promotesLegacyCardTagsPerPrayer() throws {
        let path = emptyFile()
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.write { db in
            try db.execute(sql: "INSERT INTO PrayerRequest (Id, PrayerCardId, Title) VALUES (1, 5, 'a'), (2, 5, 'b')")
            try db.execute(sql: "INSERT INTO PrayerCardTag (Id, PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt) VALUES (1, 5, 7, 1, 1), (2, 5, 7, 0, 42)")
            try SchemaUpkeep.promoteCardTags(db)
        }
        try database.dbQueue.read { db in
            let rows = try Row.fetchAll(db, sql: "SELECT PrayerRequestId, PrayerTagId, CreatedAt FROM PrayerCardTag ORDER BY PrayerRequestId")
            #expect(rows.count == 2)
            #expect(rows.map { $0["PrayerRequestId"] as Int } == [1, 2])
            #expect(rows.map { $0["PrayerTagId"] as Int } == [7, 7])
            #expect(rows[1]["CreatedAt"] as Int == 42)
        }
    }

    @Test func backfillsNotifyDateForOneTimeAndYearly() throws {
        let path = emptyFile()
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        let night = ticks(2026, 4, 17, 21, 45)
        let morning = ticks(2026, 4, 17, 8, 0)
        try database.dbQueue.write { db in
            try db.execute(
                sql: """
                    INSERT INTO PrayerRequest (Id, PrayerFrequency, CreatedAt, NotifyHour, NotifyMinute, NotifyDate) VALUES
                    (1, 0, ?, 9, 0, NULL), (2, 4, ?, 9, 0, NULL), (3, 1, ?, 9, 0, NULL),
                    (4, 0, ?, 9, 0, 123), (5, 0, ?, 9, 0, NULL), (6, 0, ?, NULL, NULL, NULL)
                    """,
                arguments: [night, night, night, night, morning, morning])
            try SchemaUpkeep.backfillNotifyDates(db, now: now)
        }
        let nextDay = ticks(2026, 4, 18, 0, 0)
        let sameDay = ticks(2026, 4, 17, 0, 0)
        try database.dbQueue.read { db in
            let dates = try Row.fetchAll(db, sql: "SELECT Id, NotifyDate FROM PrayerRequest ORDER BY Id")
            #expect(dates.map { $0["NotifyDate"] as Int64? } == [nextDay, nextDay, nil, 123, sameDay, sameDay])
        }
    }

    @Test func marksSeedColorsDefault() throws {
        let path = try legacyFile([
            "INSERT INTO UserColor (Id, HexValue) VALUES (1, '#b84040'), (2, '#123456')",
        ])
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            let actual240 = try Int.fetchOne(db, sql: "SELECT IsDefault FROM UserColor WHERE Id = 1")
            #expect(actual240 == 1)
            let actual241 = try Int.fetchOne(db, sql: "SELECT IsDefault FROM UserColor WHERE Id = 2")
            #expect(actual241 == 0)
        }
    }

    @Test func failingStepIsLoggedAndRunContinues() throws {
        let path = try legacyFile([
            "INSERT INTO PrayerRequest (Id, PrayerFrequency, CreatedAt) VALUES (1, 0, 'abc')",
            "INSERT INTO UserColor (Id, HexValue) VALUES (1, '#B84040')",
        ])
        defer { remove(path) }
        var messages = [String]()
        let database = try upgraded(path, log: { messages.append($0) })
        defer { try? database.close() }
        #expect(messages.contains { $0.hasPrefix("backfillNotifyDates") })
        try database.dbQueue.read { db in
            let actual256 = try Int.fetchOne(db, sql: "SELECT IsDefault FROM UserColor WHERE Id = 1")
            #expect(actual256 == 1)
        }
    }

    @Test func tableCreationFailurePropagates() throws {
        let path = try legacyFile(["INSERT INTO PrayerTag (Id, Name) VALUES (1, 'a'), (2, 'a')"])
        defer { remove(path) }
        let database = try PrayerDatabase(path: path)
        defer { try? database.close() }
        #expect(throws: (any Error).self) {
            try SchemaUpkeep.run(on: database, now: now, log: { _ in })
        }
    }

    @Test func secondRunChangesNothing() throws {
        let path = try fixtureCopy("long-upgraded.db")
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        let first = try snapshot(database)
        try SchemaUpkeep.run(on: database, now: now, log: { _ in })
        #expect(try snapshot(database) == first)
    }

    @Test func preservesUnknownSchemaAndUserVersion() throws {
        let path = try legacyFile([
            #"ALTER TABLE "PrayerCard" ADD COLUMN "Extra" text"#,
            "INSERT INTO PrayerCard (Id, Title, Extra) VALUES (1, 'x', 'kept')",
            #"CREATE TABLE "Foreign" ("Id" integer primary key, "Value" text)"#,
            #"INSERT INTO "Foreign" (Id, Value) VALUES (1, 'alien')"#,
            "PRAGMA user_version = 7",
        ])
        defer { remove(path) }
        let database = try upgraded(path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            let actual292 = try String.fetchOne(db, sql: "SELECT Extra FROM PrayerCard WHERE Id = 1")
            #expect(actual292 == "kept")
            let actual293 = try String.fetchOne(db, sql: "SELECT Value FROM \"Foreign\" WHERE Id = 1")
            #expect(actual293 == "alien")
            let actual294 = try Int.fetchOne(db, sql: "PRAGMA user_version")
            #expect(actual294 == 7)
        }
    }

    @Test func fixturesKeepRowCounts() throws {
        for file in ["fresh-install.db", "long-upgraded.db"] {
            let path = try fixtureCopy(file)
            defer { remove(path) }
            let database = try PrayerDatabase(path: path)
            defer { try? database.close() }
            let before = try database.rowCounts()
            try SchemaUpkeep.run(on: database, now: now, log: { _ in })
            #expect(try database.rowCounts() == before, "\(file)")
        }
    }
}
