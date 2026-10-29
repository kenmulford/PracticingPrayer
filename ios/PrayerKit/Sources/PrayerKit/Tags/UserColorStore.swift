import Foundation
import GRDB

public enum UserColorError: Error, Equatable { case notFound, defaultColor, invalidReplacement }

public struct UserColorStore: Sendable {
    public static let defaultPaletteHexValues = [
        "#B84040", "#B35A20", "#7A4020", "#1E7870",
        "#2E5A9A", "#663C8C", "#8C3860", "#505050",
    ]

    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    public func colors() throws -> [UserColorRecord] {
        try database.dbQueue.read { db in
            try UserColorRecord.order(Column("CreatedAt"), Column("Id")).fetchAll(db)
        }
    }

    public func customColors() throws -> [UserColorRecord] {
        try colors().filter { !$0.isDefault }
    }

    public func saveColor(hex: String, now: Date) throws -> UserColorRecord {
        let upper = hex.uppercased()
        return try database.dbQueue.write { db in
            if let existing = try UserColorRecord.fetchOne(
                db, sql: "SELECT * FROM UserColor WHERE UPPER(HexValue) = ? ORDER BY Id", arguments: [upper])
            {
                return existing
            }
            let ticks = now.ticks
            try db.execute(
                sql: "INSERT INTO UserColor (HexValue, IsDefault, CreatedAt) VALUES (?, 0, ?)",
                arguments: [upper, ticks])
            return UserColorRecord(
                id: db.lastInsertedRowID, hexValue: upper, isDefault: false, createdAtTicks: ticks)
        }
    }

    public func deleteColor(id: Int64, replacementHex: String, now: Date) throws {
        try database.dbQueue.write { db in
            guard let color = try UserColorRecord.fetchOne(db, key: id) else { throw UserColorError.notFound }
            if color.isDefault { throw UserColorError.defaultColor }
            guard color.hexValue.uppercased() != replacementHex.uppercased(),
                let replacement = try UserColorRecord.fetchOne(
                    db, sql: "SELECT * FROM UserColor WHERE UPPER(HexValue) = ? AND Id <> ? ORDER BY Id",
                    arguments: [replacementHex.uppercased(), id])
            else { throw UserColorError.invalidReplacement }
            try db.execute(
                sql: "UPDATE PrayerTag SET Color = ?, UpdatedAt = ? WHERE UPPER(Color) = UPPER(?)",
                arguments: [replacement.hexValue, now.ticks, color.hexValue])
            try db.execute(sql: "DELETE FROM UserColor WHERE Id = ?", arguments: [id])
        }
    }

    public func seedDefaults(now: Date) throws {
        let ticks = now.ticks
        try database.dbQueue.write { db in
            guard try UserColorRecord.fetchCount(db) == 0 else { return }
            for hex in Self.defaultPaletteHexValues {
                try db.execute(
                    sql: "INSERT INTO UserColor (HexValue, IsDefault, CreatedAt) VALUES (?, 1, ?)",
                    arguments: [hex, ticks])
            }
        }
    }
}
