import Foundation
import GRDB
import os

/// The MAUI `DBService.UpdateSchema` upkeep, ported step for step. Every step is idempotent so
/// it can run on each open. Table creation and the box seed propagate; every other step logs
/// and lets the run continue, as the MAUI app does.
public enum SchemaUpkeep {
    private static let ticksPerDay: Int64 = 864_000_000_000
    private static let seedColorHexes = [
        "#B84040", "#B35A20", "#7A4020", "#1E7870",
        "#2E5A9A", "#663C8C", "#8C3860", "#505050",
    ]

    public static func run(on database: PrayerDatabase, now: Date = Date(), log: (String) -> Void = SchemaUpkeep.logError) throws {
        let queue = database.dbQueue

        func step(_ name: String, _ body: (Database) throws -> Void) {
            do { try queue.write(body) } catch { log("\(name): \(error)") }
        }
        func column(_ table: String, _ name: String, _ definition: String) {
            step("\(table).\(name)") { try addColumnIfMissing($0, table: table, column: name, definition: definition) }
        }

        try queue.write(createTables)

        column("PrayerCard", "IsFavorite", "INTEGER DEFAULT 0")
        column("PrayerCard", "ProtectionMode", "INTEGER DEFAULT 0")
        column("PrayerRequest", "AnsweredAt", "TEXT")
        column("PrayerCardTag", "PrayerRequestId", "INTEGER DEFAULT 0")
        step("PrayerCardTag_PrayerRequestId") {
            try $0.execute(sql: #"CREATE INDEX IF NOT EXISTS "PrayerCardTag_PrayerRequestId" ON "PrayerCardTag"("PrayerRequestId")"#)
        }
        step("promoteCardTags", promoteCardTags)

        column("PrayerCard", "IsSystem", "INTEGER DEFAULT 0")
        column("PrayerCard", "IsImported", "INTEGER DEFAULT 0")
        column("PrayerRequest", "IsImported", "INTEGER DEFAULT 0")

        step("IsSystem title backfill") { db in
            for title in ["Quick Add", "Shared with me"] {
                try db.execute(sql: "UPDATE PrayerCard SET IsSystem = 1 WHERE Title = ? AND IsSystem = 0", arguments: [title])
            }
        }

        try queue.write { try seedBoxes($0, now: now) }
        column("PrayerCard", "BoxId", "INTEGER DEFAULT 0")
        column("PrayerCard", "SystemKey", "TEXT")
        column("PrayerCard", "PreArchiveBoxId", "INTEGER")
        step("moveSystemCardsToSystemBox") { db in
            guard let systemBoxId = try Int64.fetchOne(db, sql: "SELECT Id FROM CardBox WHERE SystemKey = 'system'") else { return }
            try db.execute(
                sql: "UPDATE PrayerCard SET BoxId = ? WHERE BoxId = 0 AND (IsSystem = 1 OR IsImported = 1)",
                arguments: [systemBoxId])
        }
        step("SystemKey backfill") { db in
            for (key, title) in [("quick_add", "Quick Add"), ("shared_with_me", "Shared with me")] {
                try db.execute(
                    sql: "UPDATE PrayerCard SET SystemKey = ? WHERE IsSystem = 1 AND Title = ? AND SystemKey IS NULL",
                    arguments: [key, title])
            }
        }

        column("CardBox", "ProtectAllCards", "INTEGER DEFAULT 0")
        column("CardBox", "CardProtectionMode", "INTEGER DEFAULT 0")

        step("PrayerRequestTag removal") { try $0.execute(sql: "DROP TABLE IF EXISTS PrayerRequestTag") }

        step("cleanOrphanInteractions", cleanOrphanInteractions)
        step("cleanOrphanCardTags", cleanOrphanCardTags)

        column("UserColor", "IsDefault", "INTEGER DEFAULT 0")
        column("PrayerTag", "IsSystem", "INTEGER DEFAULT 0")

        column("PrayerRequest", "NotifyHour", "INTEGER DEFAULT 9")
        column("PrayerRequest", "NotifyMinute", "INTEGER DEFAULT 0")
        column("PrayerRequest", "NotifyDayOfWeek", "INTEGER DEFAULT -1")
        column("PrayerRequest", "NotifyDayOfMonth", "INTEGER DEFAULT -1")
        column("PrayerRequest", "NotifyDate", "INTEGER")

        step("backfillNotifyDates") { try backfillNotifyDates($0, now: now) }
        step("backfillDefaultColors", backfillDefaultColors)
    }

    public static func logError(_ message: String) {
        Logger(subsystem: "PrayerKit", category: "SchemaUpkeep").error("\(message, privacy: .public)")
    }

    /// Mirrors the DDL `contracts/db/fresh-install.db` carries, which is what sqlite-net writes.
    static func createTables(_ db: Database) throws {
        let statements = [
            #"CREATE TABLE IF NOT EXISTS "PrayerCard" ("Id" integer primary key autoincrement not null, "Title" varchar(100), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "IsFavorite" integer, "IsSystem" integer, "IsImported" integer, "ProtectionMode" integer, "BoxId" integer, "PreArchiveBoxId" integer, "SystemKey" varchar(20), "CreatedAt" bigint, "UpdatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "PrayerRequest" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "Title" varchar(100), "Details" varchar(1000), "CanNotify" integer, "PrayerFrequency" integer, "NotifyHour" integer, "NotifyMinute" integer, "NotifyDayOfWeek" integer, "NotifyDayOfMonth" integer, "NotifyDate" bigint, "IsImported" integer, "IsAnswered" integer, "AnsweredAt" bigint, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "PrayerTag" ("Id" integer primary key autoincrement not null, "Name" varchar(100), "Color" varchar(9), "IsSystem" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "PrayerCardTag" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "PrayerTagId" integer, "PrayerRequestId" integer, "CreatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "PrayerInteraction" ("Id" integer primary key autoincrement not null, "PrayerId" integer, "InteractionType" varchar(50), "InteractionAt" bigint, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "UserColor" ("Id" integer primary key autoincrement not null, "HexValue" varchar, "IsDefault" integer, "CreatedAt" bigint)"#,
            #"CREATE TABLE IF NOT EXISTS "CardBox" ("Id" integer primary key autoincrement not null, "Name" varchar(50), "IsSystem" integer, "SystemKey" varchar(20), "SortOrder" integer, "ProtectAllCards" integer, "CardProtectionMode" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
            #"CREATE INDEX IF NOT EXISTS "PrayerRequest_PrayerCardId" ON "PrayerRequest"("PrayerCardId")"#,
            #"CREATE UNIQUE INDEX IF NOT EXISTS "PrayerTag_Name" ON "PrayerTag"("Name")"#,
            #"CREATE INDEX IF NOT EXISTS "PrayerCardTag_PrayerCardId" ON "PrayerCardTag"("PrayerCardId")"#,
            #"CREATE INDEX IF NOT EXISTS "PrayerCardTag_PrayerTagId" ON "PrayerCardTag"("PrayerTagId")"#,
            #"CREATE INDEX IF NOT EXISTS "PrayerInteraction_PrayerId" ON "PrayerInteraction"("PrayerId")"#,
            #"CREATE UNIQUE INDEX IF NOT EXISTS "CardBox_Name" ON "CardBox"("Name")"#,
        ]
        for sql in statements { try db.execute(sql: sql) }
    }

    static func addColumnIfMissing(_ db: Database, table: String, column: String, definition: String) throws {
        guard try !db.columns(in: table).contains(where: { $0.name == column }) else { return }
        try db.execute(sql: #"ALTER TABLE "\#(table)" ADD COLUMN "\#(column)" \#(definition)"#)
    }

    static func promoteCardTags(_ db: Database) throws {
        guard try db.columns(in: "PrayerCardTag").contains(where: { $0.name == "PrayerRequestId" }) else { return }
        let legacyRows = try Row.fetchAll(
            db, sql: "SELECT Id, PrayerCardId, PrayerTagId, CreatedAt FROM PrayerCardTag WHERE PrayerRequestId = 0")
        for legacy in legacyRows {
            let cardId: DatabaseValue = legacy["PrayerCardId"]
            let tagId: DatabaseValue = legacy["PrayerTagId"]
            let createdAt: DatabaseValue = legacy["CreatedAt"]
            let requestIds = try Int64.fetchAll(db, sql: "SELECT Id FROM PrayerRequest WHERE PrayerCardId = ?", arguments: [cardId])
            for requestId in requestIds {
                let exists = try Bool.fetchOne(
                    db,
                    sql: "SELECT EXISTS(SELECT 1 FROM PrayerCardTag WHERE PrayerRequestId = ? AND PrayerTagId = ?)",
                    arguments: [requestId, tagId]) ?? false
                if exists { continue }
                try db.execute(
                    sql: "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt) VALUES (?, ?, ?, ?)",
                    arguments: [cardId, tagId, requestId, createdAt])
            }
            let legacyId: DatabaseValue = legacy["Id"]
            try db.execute(sql: "DELETE FROM PrayerCardTag WHERE Id = ?", arguments: [legacyId])
        }
    }

    static func seedBoxes(_ db: Database, now: Date) throws {
        let columns = try db.columns(in: "CardBox").map(\.name)
        let hasProtection = columns.contains("ProtectAllCards") && columns.contains("CardProtectionMode")
        let stamp = TicksDate(now)
        for (key, name, sortOrder) in [("system", "System", 900), ("archived", "Archived", 999)] {
            let exists = try Bool.fetchOne(
                db, sql: "SELECT EXISTS(SELECT 1 FROM CardBox WHERE SystemKey = ?)", arguments: [key]) ?? false
            if exists { continue }
            if hasProtection {
                try db.execute(
                    sql: "INSERT INTO CardBox (Name, IsSystem, SystemKey, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) VALUES (?, 1, ?, ?, 0, 0, ?, ?)",
                    arguments: [name, key, sortOrder, stamp, stamp])
            } else {
                try db.execute(
                    sql: "INSERT INTO CardBox (Name, IsSystem, SystemKey, SortOrder, CreatedAt, UpdatedAt) VALUES (?, 1, ?, ?, ?, ?)",
                    arguments: [name, key, sortOrder, stamp, stamp])
            }
        }
    }

    static func cleanOrphanInteractions(_ db: Database) throws {
        try db.execute(sql: "DELETE FROM PrayerInteraction WHERE PrayerId NOT IN (SELECT Id FROM PrayerRequest)")
    }

    static func cleanOrphanCardTags(_ db: Database) throws {
        try db.execute(
            sql: "DELETE FROM PrayerCardTag WHERE PrayerRequestId > 0 AND PrayerRequestId NOT IN (SELECT Id FROM PrayerRequest)")
    }

    /// Ticks arithmetic matches `NotificationHelper.GetFirstFireTime`, which is zone-free.
    static func backfillNotifyDates(_ db: Database, now: Date) throws {
        let rows = try Row.fetchAll(
            db,
            sql: "SELECT Id, CreatedAt, NotifyHour, NotifyMinute FROM PrayerRequest WHERE PrayerFrequency IN (0, 4) AND NotifyDate IS NULL")
        for row in rows {
            let id: Int64 = row["Id"]
            let createdValue: DatabaseValue = row["CreatedAt"]
            let created = try TicksDate.decodeTicks(createdValue, column: "CreatedAt") ?? now.ticks
            let hour: Int64 = row["NotifyHour"] ?? 9
            let minute: Int64 = row["NotifyMinute"] ?? 0
            let day = created - created % ticksPerDay
            let sameDay = day + (hour * 3600 + minute * 60) * 10_000_000
            let fire = created < sameDay ? sameDay : sameDay + ticksPerDay
            try db.execute(
                sql: "UPDATE PrayerRequest SET NotifyDate = ? WHERE Id = ?",
                arguments: [fire - fire % ticksPerDay, id])
        }
    }

    static func backfillDefaultColors(_ db: Database) throws {
        for hex in seedColorHexes {
            try db.execute(sql: "UPDATE UserColor SET IsDefault = 1 WHERE UPPER(HexValue) = ?", arguments: [hex.uppercased()])
        }
    }
}
