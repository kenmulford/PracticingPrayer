import Foundation
import GRDB

public enum RecordDecodingError: Error, Equatable {
    case unknownEnumValue(table: String, column: String, value: Int64)
    case invalidDate(table: String, column: String)
}

/// Integer values match `PrayerApp/Models/PrayerFrequency.cs`; they are stored unchanged.
public enum PrayerFrequency: Int64, Sendable, CaseIterable {
    case oneTime = 0, daily = 1, weekly = 2, monthly = 3, yearly = 4
}

/// `unprotected` is the C# `None`; `.none` would collide with `Optional.none`.
public enum CardProtectionMode: Int64, Sendable, CaseIterable {
    case unprotected = 0, lockedVisible = 1, hidden = 2
}

extension Row {
    func ticks(_ column: String, table: String) throws -> Int64? {
        do {
            return try TicksDate.decodeTicks(self[column] as DatabaseValue, column: column)
        } catch {
            throw RecordDecodingError.invalidDate(table: table, column: column)
        }
    }

    func requiredTicks(_ column: String, table: String) throws -> Int64 {
        guard let value = try ticks(column, table: table) else {
            throw RecordDecodingError.invalidDate(table: table, column: column)
        }
        return value
    }

    func enumValue<E: RawRepresentable>(_ type: E.Type, _ column: String, table: String) throws -> E?
    where E.RawValue == Int64 {
        guard let raw: Int64 = try decode(forColumn: column) else { return nil }
        guard let value = E(rawValue: raw) else {
            throw RecordDecodingError.unknownEnumValue(table: table, column: column, value: raw)
        }
        return value
    }

    func requiredEnum<E: RawRepresentable>(_ type: E.Type, _ column: String, table: String) throws -> E
    where E.RawValue == Int64 {
        let raw: Int64 = try decode(forColumn: column)
        guard let value = E(rawValue: raw) else {
            throw RecordDecodingError.unknownEnumValue(table: table, column: column, value: raw)
        }
        return value
    }
}
