import Foundation
import GRDB

public enum TicksDateError: Error, Equatable {
    case outOfRange(ticks: Int64)
    case invalidValue(column: String)
    case columnOutOfRange(column: String, ticks: Int64)
}

private let ticksPerSecond: Int64 = 10_000_000

extension Date {
    /// .NET ticks (100 ns since 0001-01-01) read as local wall-clock time with no offset,
    /// matching `DateTime.Now` stored by sqlite-net.
    public init(ticks: Int64) throws(TicksDateError) {
        guard TicksDate.validTicks.contains(ticks) else {
            throw TicksDateError.outOfRange(ticks: ticks)
        }
        let sinceUnix = ticks - TicksDate.unixEpochTicks
        // Split before converting: ticks near today exceed Double's exact integer range.
        var seconds = sinceUnix / ticksPerSecond
        var remainder = sinceUnix % ticksPerSecond
        if remainder < 0 {
            seconds -= 1
            remainder += ticksPerSecond
        }
        let wall = Date(timeIntervalSince1970: Double(seconds))
        let zone = TimeZone.current
        let guess = wall.addingTimeInterval(-Double(zone.secondsFromGMT(for: wall)))
        let actual = wall.addingTimeInterval(-Double(zone.secondsFromGMT(for: guess)))
        self = actual.addingTimeInterval(Double(remainder) / Double(ticksPerSecond))
    }

    public var ticks: Int64 {
        let minSeconds = Double(TicksDate.validTicks.lowerBound - TicksDate.unixEpochTicks) / Double(ticksPerSecond)
        let maxSeconds = Double(TicksDate.validTicks.upperBound - TicksDate.unixEpochTicks) / Double(ticksPerSecond)
        let offset = Double(TimeZone.current.secondsFromGMT(for: self))
        let wall = min(max(timeIntervalSince1970 + offset, minSeconds), maxSeconds)
        let whole = wall.rounded(.down)
        var subTicks = Int64(((wall - whole) * Double(ticksPerSecond)).rounded())
        var seconds = Int64(whole)
        if subTicks >= ticksPerSecond {
            seconds += 1
            subTicks -= ticksPerSecond
        }
        let total = TicksDate.unixEpochTicks + seconds * ticksPerSecond + subTicks
        return min(max(total, TicksDate.validTicks.lowerBound), TicksDate.validTicks.upperBound)
    }
}

/// GRDB codec for sqlite-net date columns. A bare `Date` binds as text in GRDB, so every write
/// goes through this type.
public struct TicksDate: DatabaseValueConvertible, Sendable, Equatable {
    public var date: Date

    public init(_ date: Date) {
        self.date = date
    }

    public static let unixEpochTicks: Int64 = 621_355_968_000_000_000
    /// 0001-01-01 through `DateTime.MaxValue`.
    public static let validTicks: ClosedRange<Int64> = 0...3_155_378_975_999_999_999

    public var databaseValue: DatabaseValue {
        date.ticks.databaseValue
    }

    public static func fromDatabaseValue(_ dbValue: DatabaseValue) -> TicksDate? {
        guard let date = try? decode(dbValue, column: "") else { return nil }
        return TicksDate(date)
    }

    /// INTEGER and base-10 TEXT decode; `AnsweredAt` is created `TEXT`, so SQLite stores its
    /// integer ticks as text.
    public static func decode(_ dbValue: DatabaseValue, column: String) throws(TicksDateError) -> Date? {
        guard let ticks = try decodeTicks(dbValue, column: column) else { return nil }
        do {
            return try Date(ticks: ticks)
        } catch {
            throw TicksDateError.columnOutOfRange(column: column, ticks: ticks)
        }
    }

    /// Validated raw ticks, for records that must write back the exact stored value.
    public static func decodeTicks(_ dbValue: DatabaseValue, column: String) throws(TicksDateError) -> Int64? {
        let ticks: Int64
        switch dbValue.storage {
        case .null:
            return nil
        case .int64(let value):
            ticks = value
        case .string(let text):
            guard isBase10Integer(text), let value = Int64(text) else {
                throw TicksDateError.invalidValue(column: column)
            }
            ticks = value
        case .double, .blob:
            throw TicksDateError.invalidValue(column: column)
        }
        guard validTicks.contains(ticks) else {
            throw TicksDateError.columnOutOfRange(column: column, ticks: ticks)
        }
        return ticks
    }

    public static func encode(_ date: Date?) -> DatabaseValue {
        guard let date else { return .null }
        return TicksDate(date).databaseValue
    }

    private static func isBase10Integer(_ text: String) -> Bool {
        var digits = Substring(text)
        if digits.first == "-" { digits = digits.dropFirst() }
        return !digits.isEmpty && digits.utf8.allSatisfy { $0 >= 0x30 && $0 <= 0x39 }
    }
}
