import Foundation
import GRDB
import Testing

@testable import PrayerKit

@Suite struct PrayerDatabaseTests {
    private func tempPath() -> String {
        FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".db").path
    }

    private func removeFiles(_ path: String) {
        for suffix in ["", "-wal", "-shm", "-journal"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }

    private func makeFixture(at path: String) throws {
        let queue = try DatabaseQueue(path: path)
        try queue.write { db in
            try db.execute(sql: #"CREATE TABLE "PrayerRequest" ("Id" integer primary key autoincrement not null , "Title" varchar(100) , "AnsweredAt" bigint , "CreatedAt" bigint )"#)
            try db.execute(sql: #"INSERT INTO "PrayerRequest" ("Title", "CreatedAt") VALUES ('Hello', 638460882450000000)"#)
            try db.execute(sql: "PRAGMA user_version = 7")
        }
        try queue.close()
    }

    private func userVersion(at path: String) throws -> Int? {
        let queue = try DatabaseQueue(path: path)
        defer { try? queue.close() }
        return try queue.read { try Int.fetchOne($0, sql: "PRAGMA user_version") }
    }

    @Test func opensExistingFileAndReadsRow() throws {
        let path = tempPath()
        defer { removeFiles(path) }
        try makeFixture(at: path)

        let database = try PrayerDatabase(path: path)
        let title = try database.dbQueue.read { try String.fetchOne($0, sql: "SELECT Title FROM PrayerRequest") }
        try database.close()
        #expect(title == "Hello")
    }

    @Test func openCloseKeepsUserVersionAndDeleteJournal() throws {
        let path = tempPath()
        defer { removeFiles(path) }
        try makeFixture(at: path)
        let before = try userVersion(at: path)

        let database = try PrayerDatabase(path: path)
        let mode = try database.dbQueue.read { try String.fetchOne($0, sql: "PRAGMA journal_mode") }
        try database.close()

        #expect(mode == "delete")
        #expect(try userVersion(at: path) == before)
        #expect(before == 7)
    }

    @Test func openCloseLeavesFileUnchangedWithNoSidecars() throws {
        let path = tempPath()
        defer { removeFiles(path) }
        try makeFixture(at: path)
        let before = try Data(contentsOf: URL(fileURLWithPath: path))

        let database = try PrayerDatabase(path: path)
        _ = try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM PrayerRequest") }
        try database.close()

        #expect(try Data(contentsOf: URL(fileURLWithPath: path)) == before)
        for suffix in ["-wal", "-shm", "-journal"] {
            #expect(!FileManager.default.fileExists(atPath: path + suffix))
        }
    }

    @Test func missingFileThrowsFileNotFoundAndCreatesNothing() {
        let path = tempPath()
        defer { removeFiles(path) }

        #expect(throws: PrayerDatabaseError.fileNotFound) {
            try PrayerDatabase(path: path)
        }
        #expect(!FileManager.default.fileExists(atPath: path))
    }

    @Test func nonSqliteFileThrowsNotADatabase() throws {
        let path = tempPath()
        defer { removeFiles(path) }
        try Data(repeating: 0x41, count: 4096).write(to: URL(fileURLWithPath: path))

        #expect(throws: PrayerDatabaseError.notADatabase) {
            try PrayerDatabase(path: path)
        }
    }
}
