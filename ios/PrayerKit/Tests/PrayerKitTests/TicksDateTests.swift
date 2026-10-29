import Foundation
import GRDB
import Testing

@testable import PrayerKit

@Suite struct TicksDateTests {
    private static let unixEpochTicks: Int64 = 621_355_968_000_000_000
    private static let knownTicks: Int64 = 638_460_882_450_000_000

    private func components(_ date: Date) -> DateComponents {
        Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
    }

    @Test func roundTripsDateWithinOneTick() throws {
        let d = Date(timeIntervalSinceReferenceDate: 815_000_000.1234567)
        let back = try Date(ticks: d.ticks)
        #expect(abs(back.timeIntervalSince(d)) < 1e-7 + d.timeIntervalSinceReferenceDate.ulp)
    }

    @Test func knownTicksDecodeToSameWallClockComponents() throws {
        let c = components(try Date(ticks: Self.knownTicks))
        #expect(c.year == 2024)
        #expect(c.month == 3)
        #expect(c.day == 15)
        #expect(c.hour == 8)
        #expect(c.minute == 30)
        #expect(c.second == 45)
    }

    @Test func unixEpochTicksAreLocalMidnight1970() throws {
        let c = components(try Date(ticks: Self.unixEpochTicks))
        #expect(c.year == 1970)
        #expect(c.month == 1)
        #expect(c.day == 1)
        #expect(c.hour == 0)
        #expect(c.minute == 0)
        #expect(c.second == 0)

        let midnight = try #require(Calendar.current.date(from: DateComponents(
            year: 1970, month: 1, day: 1, hour: 0, minute: 0, second: 0)))
        #expect(midnight.ticks == Self.unixEpochTicks)
    }

    @Test func nullDecodesToNilAndNilEncodesToNull() throws {
        #expect(try TicksDate.decode(.null, column: "AnsweredAt") == nil)
        #expect(TicksDate.encode(nil).isNull)
    }

    @Test func digitTextDecodesLikeInteger() throws {
        let fromText = try TicksDate.decode("638460882450000000".databaseValue, column: "AnsweredAt")
        let fromInt = try TicksDate.decode(Int64(638_460_882_450_000_000).databaseValue, column: "AnsweredAt")
        #expect(fromText == fromInt)
        #expect(fromText != nil)

        #expect(throws: TicksDateError.columnOutOfRange(column: "AnsweredAt", ticks: -1)) {
            try TicksDate.decode("-1".databaseValue, column: "AnsweredAt")
        }
    }

    @Test func otherStorageThrowsNamingColumn() {
        let values: [DatabaseValue] = [
            "2024-03-15".databaseValue,
            "+5".databaseValue,
            "".databaseValue,
            1.5.databaseValue,
            Data([1]).databaseValue,
        ]
        for value in values {
            #expect(throws: TicksDateError.invalidValue(column: "AnsweredAt")) {
                try TicksDate.decode(value, column: "AnsweredAt")
            }
        }
    }

    @Test func outOfRangeTicksThrow() {
        #expect(throws: TicksDateError.columnOutOfRange(column: "CreatedAt", ticks: Int64.max)) {
            try TicksDate.decode(Int64.max.databaseValue, column: "CreatedAt")
        }
        #expect(throws: TicksDateError.outOfRange(ticks: Int64.min)) {
            try Date(ticks: Int64.min)
        }
    }

    @Test func textAffinityAnsweredAtRoundTripsAsText() throws {
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString + ".db").path
        defer { try? FileManager.default.removeItem(atPath: path) }
        let queue = try DatabaseQueue(path: path)
        defer { try? queue.close() }

        try queue.write { db in
            try db.execute(sql: #"CREATE TABLE "PrayerRequest" ("Id" integer primary key autoincrement not null , "AnsweredAt" TEXT)"#)
            try db.execute(
                sql: #"INSERT INTO "PrayerRequest" ("AnsweredAt") VALUES (?)"#,
                arguments: [Int64(638_460_882_450_000_000)])
        }

        let first = try queue.read { db -> Date? in
            let value = try #require(try DatabaseValue.fetchOne(db, sql: #"SELECT "AnsweredAt" FROM "PrayerRequest""#))
            return try TicksDate.decode(value, column: "AnsweredAt")
        }
        #expect(first != nil)

        try queue.write { db in
            try db.execute(
                sql: #"UPDATE "PrayerRequest" SET "AnsweredAt" = ?"#,
                arguments: [TicksDate.encode(first)])
        }

        let storage = try queue.read { try String.fetchOne($0, sql: #"SELECT typeof("AnsweredAt") FROM "PrayerRequest""#) }
        #expect(storage == "text")

        let second = try queue.read { db -> Date? in
            let value = try #require(try DatabaseValue.fetchOne(db, sql: #"SELECT "AnsweredAt" FROM "PrayerRequest""#))
            return try TicksDate.decode(value, column: "AnsweredAt")
        }
        #expect(second == first)
    }
}
