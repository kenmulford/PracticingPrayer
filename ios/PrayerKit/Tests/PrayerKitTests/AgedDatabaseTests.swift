import Foundation
import GRDB
import Testing

@testable import PrayerKit

/// A database that began as an early MAUI install. sqlite-net's auto-migration added each later column with
/// `ALTER TABLE ADD COLUMN` and no default, so existing rows hold NULL there, and 1.7.2 read NULL as false/0.
private let agedSchema = [
    #"CREATE TABLE "PrayerCard" ("Id" integer primary key autoincrement not null, "Title" varchar(100), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerRequest" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "Title" varchar(100), "Details" varchar(1000), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerTag" ("Id" integer primary key autoincrement not null, "Name" varchar(100), "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerCardTag" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "PrayerTagId" integer, "CreatedAt" bigint)"#,
    #"CREATE TABLE "PrayerInteraction" ("Id" integer primary key autoincrement not null, "PrayerId" integer, "InteractionType" varchar(50), "InteractionAt" bigint, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "UserColor" ("Id" integer primary key autoincrement not null, "HexValue" varchar, "CreatedAt" bigint)"#,
    #"CREATE TABLE "CardBox" ("Id" integer primary key autoincrement not null, "Name" varchar(50), "CreatedAt" bigint, "UpdatedAt" bigint)"#,
]

private let laterColumns = [
    ("PrayerCard", "IsFavorite", "integer"), ("PrayerCard", "IsSystem", "integer"),
    ("PrayerCard", "IsImported", "integer"), ("PrayerCard", "ProtectionMode", "integer"),
    ("PrayerCard", "BoxId", "integer"), ("PrayerCard", "PreArchiveBoxId", "integer"),
    ("PrayerCard", "SystemKey", "varchar(20)"),
    ("PrayerRequest", "AnsweredAt", "bigint"), ("PrayerRequest", "IsImported", "integer"),
    ("PrayerRequest", "NotifyHour", "integer"), ("PrayerRequest", "NotifyMinute", "integer"),
    ("PrayerRequest", "NotifyDayOfWeek", "integer"), ("PrayerRequest", "NotifyDayOfMonth", "integer"),
    ("PrayerRequest", "NotifyDate", "bigint"),
    ("PrayerTag", "Color", "varchar(9)"), ("PrayerTag", "IsSystem", "integer"),
    ("PrayerCardTag", "PrayerRequestId", "integer"),
    ("UserColor", "IsDefault", "integer"),
    ("CardBox", "IsSystem", "integer"), ("CardBox", "SystemKey", "varchar(20)"), ("CardBox", "SortOrder", "integer"),
    ("CardBox", "ProtectAllCards", "integer"), ("CardBox", "CardProtectionMode", "integer"),
]

private let now = Date(timeIntervalSince1970: 1_800_000_000)

private func withAgedDatabase(upkeep: Bool, _ body: (PrayerDatabase) throws -> Void) throws {
    let path = FileManager.default.temporaryDirectory.appendingPathComponent("pk_aged_\(UUID().uuidString).db").path
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
    }
    let queue = try DatabaseQueue(path: path)
    let t = now.ticks
    try queue.write { db in
        for sql in agedSchema { try db.execute(sql: sql) }
        try db.execute(sql: "INSERT INTO CardBox (Name, CreatedAt, UpdatedAt) VALUES ('Family', ?, ?)", arguments: [t, t])
        try db.execute(
            sql: "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, CreatedAt, UpdatedAt) VALUES ('Mom', 0, 1, 0, ?, ?), ('Work', 1, 2, 0, ?, ?)",
            arguments: [t, t, t, t])
        try db.execute(
            sql: "INSERT INTO PrayerRequest (PrayerCardId, Title, CanNotify, PrayerFrequency, IsAnswered, CreatedAt, UpdatedAt) VALUES (1, 'Health', 0, 1, 0, ?, ?), (2, 'Deadline', 1, 0, 0, ?, ?)",
            arguments: [t, t, t, t])
        try db.execute(sql: "INSERT INTO PrayerTag (Name, CreatedAt, UpdatedAt) VALUES ('Urgent', ?, ?)", arguments: [t, t])
        try db.execute(sql: "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, CreatedAt) VALUES (1, 1, ?)", arguments: [t])
        try db.execute(
            sql: "INSERT INTO PrayerInteraction (PrayerId, InteractionType, InteractionAt, CreatedAt, UpdatedAt) VALUES (1, 'Prayed', ?, ?, ?)",
            arguments: [t, t, t])
        try db.execute(sql: "INSERT INTO UserColor (HexValue, CreatedAt) VALUES ('#112233', ?)", arguments: [t])
        for (table, column, type) in laterColumns {
            try db.execute(sql: #"ALTER TABLE "\#(table)" ADD COLUMN "\#(column)" \#(type)"#)
        }
    }
    try queue.close()
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    if upkeep { try SchemaUpkeep.run(on: database, now: now, log: { Issue.record("upkeep step failed: \($0)") }) }
    try body(database)
}

@Suite struct AgedDatabaseTests {
    private func assertReads(_ database: PrayerDatabase) throws {
        for unlocked in [false, true] {
            let metrics = try database.homeMetrics(now: now, isSessionUnlocked: unlocked)
            #expect(metrics.activeCards == 2)
            #expect(metrics.unanswered == 2)

            let list = try database.cardList(isSessionUnlocked: unlocked)
            #expect(list.totalMatchCount == 2)
            #expect(try PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: unlocked).count == 2)
            #expect(try PrayerStore(database: database).prayers(segment: .answered, isSessionUnlocked: unlocked).isEmpty)
            #expect(try TagStore(database: database).tags(isSessionUnlocked: unlocked).count == 1)
        }

        let family = try #require(try CollectionStore(database: database).collections().first { $0.name == "Family" })
        #expect(!family.protectAllCards)
        #expect(family.cardProtectionMode == .unprotected)
        #expect(family.sortOrder == 0)

        let color = try #require(try UserColorStore(database: database).colors().first { $0.hexValue == "#112233" })
        #expect(!color.isDefault)

        let card = try #require(try database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: 1) })
        #expect(!card.isFavorite)
        #expect(card.prayerFrequency == .daily)

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("pk_aged_export_\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }
        _ = try BackupExporter.export(
            database: database, passphrase: "correct horse battery staple", to: directory, now: now)
    }

    @Test func decodesNullColumnsWithoutUpkeep() throws {
        try withAgedDatabase(upkeep: false, assertReads)
    }

    @Test func readsAfterUpkeepBackfill() throws {
        try withAgedDatabase(upkeep: true, assertReads)
    }

    @Test func upkeepBackfillsNullFlagsAndModes() throws {
        try withAgedDatabase(upkeep: true) { database in
            let nulls = try database.dbQueue.read { db in
                try [
                    "SELECT COUNT(*) FROM CardBox WHERE ProtectAllCards IS NULL OR CardProtectionMode IS NULL OR SortOrder IS NULL OR IsSystem IS NULL",
                    "SELECT COUNT(*) FROM PrayerCard WHERE CanNotify IS NULL OR IsAnswered IS NULL OR IsFavorite IS NULL OR IsSystem IS NULL OR IsImported IS NULL OR ProtectionMode IS NULL OR PrayerFrequency IS NULL",
                    "SELECT COUNT(*) FROM PrayerRequest WHERE CanNotify IS NULL OR IsAnswered IS NULL OR IsImported IS NULL OR PrayerFrequency IS NULL",
                    "SELECT COUNT(*) FROM PrayerTag WHERE IsSystem IS NULL",
                    "SELECT COUNT(*) FROM UserColor WHERE IsDefault IS NULL",
                ].map { try Int.fetchOne(db, sql: $0) ?? -1 }
            }
            #expect(nulls == [0, 0, 0, 0, 0])
        }
    }
}
