import Foundation
import GRDB

public enum TagError: Error, Equatable { case duplicateName, systemTag }

public struct TagSummary: Equatable, Sendable {
    public let tag: PrayerTagRecord
    public let prayerCount: Int
}

public struct TagStore: Sendable {
    public static let recentlyNotifiedName = "Recently Notified"
    public static let recentlyNotifiedColor = "#505050"

    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    public func tags() throws -> [TagSummary] {
        let (records, counts) = try database.dbQueue.read { db in
            let records = try PrayerTagRecord.fetchAll(db)
            let rows = try Row.fetchAll(db, sql: """
                SELECT PrayerTagId, COUNT(DISTINCT PrayerRequestId) AS N FROM PrayerCardTag
                WHERE PrayerRequestId > 0 GROUP BY PrayerTagId
                """)
            let counts = Dictionary(uniqueKeysWithValues: rows.map { ($0["PrayerTagId"] as Int64, $0["N"] as Int) })
            return (records, counts)
        }
        let ordered = records.sorted { a, b in
            let aSystem = a.isSystem == true, bSystem = b.isSystem == true
            if aSystem != bSystem { return aSystem }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
        return ordered.map { TagSummary(tag: $0, prayerCount: counts[$0.id] ?? 0) }
    }

    public func createTag(name: String, color: String, now: Date) throws -> PrayerTagRecord {
        let ticks = now.ticks
        return try mappingDuplicate {
            try database.dbQueue.write { db in
                try db.execute(
                    sql: "INSERT INTO PrayerTag (Name, Color, IsSystem, CreatedAt, UpdatedAt) VALUES (?, ?, 0, ?, ?)",
                    arguments: [name, color, ticks, ticks])
                return PrayerTagRecord(
                    id: db.lastInsertedRowID, name: name, color: color, isSystem: false,
                    createdAtTicks: ticks, updatedAtTicks: ticks)
            }
        }
    }

    public func renameTag(id: Int64, to name: String, now: Date) throws {
        try mappingDuplicate {
            try database.dbQueue.write { db in
                guard let tag = try PrayerTagRecord.fetchOne(db, key: id) else { return }
                if tag.isSystem == true { throw TagError.systemTag }
                try db.execute(
                    sql: "UPDATE PrayerTag SET Name = ?, UpdatedAt = ? WHERE Id = ?",
                    arguments: [name, now.ticks, id])
            }
        }
    }

    public func recolorTag(id: Int64, to color: String, now: Date) throws {
        try database.dbQueue.write { db in
            try db.execute(
                sql: "UPDATE PrayerTag SET Color = ?, UpdatedAt = ? WHERE Id = ?",
                arguments: [color, now.ticks, id])
        }
    }

    public func deleteTag(id: Int64) throws {
        try database.dbQueue.write { db in
            guard let tag = try PrayerTagRecord.fetchOne(db, key: id) else { return }
            if tag.isSystem == true { throw TagError.systemTag }
            try db.execute(sql: "DELETE FROM PrayerCardTag WHERE PrayerTagId = ?", arguments: [id])
            try db.execute(sql: "DELETE FROM PrayerTag WHERE Id = ?", arguments: [id])
        }
    }

    public func seedSystemTags(now: Date) throws {
        let ticks = now.ticks
        try database.dbQueue.write { db in
            let exists = try Bool.fetchOne(
                db, sql: "SELECT EXISTS(SELECT 1 FROM PrayerTag WHERE LOWER(Name) = LOWER(?))",
                arguments: [Self.recentlyNotifiedName]) ?? false
            guard !exists else { return }
            try db.execute(
                sql: "INSERT INTO PrayerTag (Name, Color, IsSystem, CreatedAt, UpdatedAt) VALUES (?, ?, 1, ?, ?)",
                arguments: [Self.recentlyNotifiedName, Self.recentlyNotifiedColor, ticks, ticks])
        }
    }

    private func mappingDuplicate<T>(_ body: () throws -> T) throws -> T {
        do {
            return try body()
        } catch let error as DatabaseError where error.extendedResultCode == .SQLITE_CONSTRAINT_UNIQUE {
            throw TagError.duplicateName
        }
    }
}
