import Foundation
import GRDB
import Testing

@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private let sevenTables = ["PrayerCard", "PrayerRequest", "PrayerTag", "PrayerCardTag", "PrayerInteraction", "UserColor", "CardBox"]

private let legacySchema = [
    #"CREATE TABLE "PrayerCard" ("Id" integer primary key autoincrement not null, "Title" varchar(100), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerRequest" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "Title" varchar(100), "Details" varchar(1000), "CanNotify" integer, "PrayerFrequency" integer, "IsAnswered" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerTag" ("Id" integer primary key autoincrement not null, "Name" varchar(100), "Color" varchar(9), "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "PrayerCardTag" ("Id" integer primary key autoincrement not null, "PrayerCardId" integer, "PrayerTagId" integer, "CreatedAt" bigint)"#,
    #"CREATE TABLE "PrayerInteraction" ("Id" integer primary key autoincrement not null, "PrayerId" integer, "InteractionType" varchar(50), "InteractionAt" bigint, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
    #"CREATE TABLE "UserColor" ("Id" integer primary key autoincrement not null, "HexValue" varchar, "CreatedAt" bigint)"#,
    #"CREATE TABLE "CardBox" ("Id" integer primary key autoincrement not null, "Name" varchar(50), "IsSystem" integer, "SystemKey" varchar(20), "SortOrder" integer, "CreatedAt" bigint, "UpdatedAt" bigint)"#,
]

private struct ForcedFailure: Error {}

@Suite struct BackupArchiveTests {
    private func workDirectory() throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("pk_backup_\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func fixtureCopy(_ file: String, into directory: URL) throws -> URL {
        let destination = directory.appendingPathComponent("live-\(UUID().uuidString).db")
        try FileManager.default.copyItem(
            at: repoRoot().appendingPathComponent("contracts/db/\(file)"), to: destination)
        return destination
    }

    private func snapshot(_ database: PrayerDatabase) throws -> [String: [Row]] {
        try database.dbQueue.read { db in
            var result = [String: [Row]]()
            for name in sevenTables {
                result[name] = try Row.fetchAll(db, sql: #"SELECT * FROM "\#(name)" ORDER BY Id"#)
            }
            return result
        }
    }

    private func snapshot(of url: URL) throws -> [String: [Row]] {
        let database = try PrayerDatabase(path: url.path)
        defer { try? database.close() }
        return try snapshot(database)
    }

    private func legacyDatabase(in directory: URL) throws -> URL {
        let url = directory.appendingPathComponent("legacy.db")
        let queue = try DatabaseQueue(path: url.path)
        try queue.write { db in
            for sql in legacySchema { try db.execute(sql: sql) }
        }
        try queue.close()
        return url
    }

    @Test func exportWritesDatabaseOnlyArchive() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = try fixtureCopy("long-upgraded.db", into: directory)
        let exports = directory.appendingPathComponent("exports", isDirectory: true)
        try FileManager.default.createDirectory(at: exports, withIntermediateDirectories: true)

        let utc = TimeZone(identifier: "UTC")!
        let date = Date(timeIntervalSince1970: 1_793_188_800)
        #expect(BackupArchive.fileName(for: date, timeZone: utc) == "practicing_prayer_2026-10-28.pcrd")

        let url = try BackupArchive.export(databaseURL: source, confidential: nil, to: exports, now: date)
        #expect(url.deletingLastPathComponent().standardizedFileURL == exports.standardizedFileURL)
        #expect(url.pathExtension == "pcrd")
        let entries = try BackupArchive.entries(of: Data(contentsOf: url))
        #expect(Set(entries.keys) == ["prayer_app.db"])
        #expect(entries["prayer_app.db"] == (try Data(contentsOf: source)))
    }

    @Test func exportIncludesConfidentialEntry() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = try fixtureCopy("long-upgraded.db", into: directory)
        let payload = Data((0..<300).map { UInt8($0 % 251) })
        let url = try BackupArchive.export(databaseURL: source, confidential: payload, to: directory)
        let entries = try BackupArchive.entries(of: Data(contentsOf: url))
        #expect(entries["confidential.enc"] == payload)
        #expect(entries["prayer_app.db"] != nil)
    }

    @Test func exportRemovesStalePcrdFiles() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = try fixtureCopy("long-upgraded.db", into: directory)
        let exports = directory.appendingPathComponent("exports", isDirectory: true)
        try FileManager.default.createDirectory(at: exports, withIntermediateDirectories: true)
        try Data([1]).write(to: exports.appendingPathComponent("old.pcrd"))
        try Data([2]).write(to: exports.appendingPathComponent("keep.txt"))

        _ = try BackupArchive.export(databaseURL: source, confidential: nil, to: exports)

        #expect(!FileManager.default.fileExists(atPath: exports.appendingPathComponent("old.pcrd").path))
        #expect(FileManager.default.fileExists(atPath: exports.appendingPathComponent("keep.txt").path))
    }

    @Test func restoresMauiMadeFixture() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let live = try fixtureCopy("fresh-install.db", into: directory)
        let archive = repoRoot().appendingPathComponent("contracts/backup/maui-made.pcrd")

        let result = try BackupArchive.restore(archiveURL: archive, databaseURL: live, upkeep: { _ in })

        #expect(result.confidentialPayload == nil)
        let restored = try snapshot(of: live)
        let expected = try snapshot(of: repoRoot().appendingPathComponent("contracts/db/long-upgraded.db"))
        #expect(restored == expected)
        #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("prayer_app_backup.tmp").path))
    }

    @Test func restoreReturnsConfidentialPayload() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = try fixtureCopy("long-upgraded.db", into: directory)
        let payload = Data("sealed-bytes".utf8)
        let url = try BackupArchive.export(databaseURL: source, confidential: payload, to: directory)
        let live = try fixtureCopy("fresh-install.db", into: directory)

        let result = try BackupArchive.restore(archiveURL: url, databaseURL: live, upkeep: { _ in })

        #expect(result.confidentialPayload == payload)
    }

    @Test func rejectsNonZip() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let live = try fixtureCopy("fresh-install.db", into: directory)
        let before = try Data(contentsOf: live)
        let bogus = directory.appendingPathComponent("bogus.pcrd")
        try Data("not a zip at all, just text".utf8).write(to: bogus)

        #expect(throws: BackupArchiveError.invalidBackup) {
            try BackupArchive.restore(archiveURL: bogus, databaseURL: live, upkeep: { _ in })
        }
        #expect(try Data(contentsOf: live) == before)
        #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("prayer_app_backup.tmp").path))
        #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("prayer_app_restore.db").path))
    }

    @Test func rejectsZipWithoutDatabaseEntry() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let live = try fixtureCopy("fresh-install.db", into: directory)
        let before = try Data(contentsOf: live)
        let archive = directory.appendingPathComponent("no-db.pcrd")
        try BackupArchive.archive(entries: [("confidential.enc", Data([1, 2, 3]))]).write(to: archive)

        #expect(throws: BackupArchiveError.invalidBackup) {
            try BackupArchive.restore(archiveURL: archive, databaseURL: live, upkeep: { _ in })
        }
        #expect(try Data(contentsOf: live) == before)
        #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("prayer_app_backup.tmp").path))
    }

    @Test func rejectsEntryAboveLimit() throws {
        let data = try BackupArchive.archive(entries: [("prayer_app.db", Data(repeating: 7, count: 64))])
        #expect(throws: BackupArchiveError.invalidBackup) {
            try BackupArchive.entries(of: data, maxEntryBytes: 10)
        }
        #expect(try BackupArchive.entries(of: data)["prayer_app.db"] == Data(repeating: 7, count: 64))
    }

    @Test func failureAfterSwapRestoresOriginal() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let live = try fixtureCopy("fresh-install.db", into: directory)
        let before = try Data(contentsOf: live)
        let archive = repoRoot().appendingPathComponent("contracts/backup/maui-made.pcrd")
        let preCounts: RowCounts = try {
            let database = try PrayerDatabase(path: live.path)
            defer { try? database.close() }
            return try database.rowCounts()
        }()

        #expect(throws: ForcedFailure.self) {
            try BackupArchive.restore(archiveURL: archive, databaseURL: live, upkeep: { _ in throw ForcedFailure() })
        }

        let database = try PrayerDatabase(path: live.path)
        defer { try? database.close() }
        #expect(try database.rowCounts() == preCounts)
        #expect(try Data(contentsOf: live) == before)
        #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("prayer_app_backup.tmp").path))
    }

    @Test func restoredOlderSchemaGainsColumns() throws {
        let directory = try workDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let legacy = try legacyDatabase(in: directory)
        let url = try BackupArchive.export(databaseURL: legacy, confidential: nil, to: directory)
        let live = try fixtureCopy("fresh-install.db", into: directory)

        _ = try BackupArchive.restore(archiveURL: url, databaseURL: live)

        let database = try PrayerDatabase(path: live.path)
        defer { try? database.close() }
        try database.dbQueue.read { db in
            let columns = try Row.fetchAll(db, sql: #"PRAGMA table_info("PrayerRequest")"#).map { $0["name"] as String }
            #expect(columns.contains("NotifyDate"))
        }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["PP_WRITE_BACKUP_FIXTURES"] == "1"))
    func writeNativeArchiveFixture() throws {
        let root = repoRoot()
        let database = try Data(contentsOf: root.appendingPathComponent("contracts/db/native-written.db"))
        let archive = try BackupArchive.archive(entries: [("prayer_app.db", database)])
        try archive.write(to: root.appendingPathComponent("contracts/backup/native-made.pcrd"))
    }
}
