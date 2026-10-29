import Foundation
import GRDB
import Testing

@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private let passphrase = "correct horse battery"

@Suite struct BackupExporterTests {
    private struct Fixture {
        let directory: URL
        let live: URL
        let database: PrayerDatabase
    }

    private func makeFixture(protectCard: Bool = false) throws -> Fixture {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("pk_exporter_\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let live = directory.appendingPathComponent("live.db")
        try FileManager.default.copyItem(
            at: repoRoot().appendingPathComponent("contracts/db/long-upgraded.db"), to: live)
        let database = try PrayerDatabase(path: live.path)
        if protectCard {
            try database.dbQueue.write { db in
                try db.execute(sql: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 2")
            }
        }
        return Fixture(directory: directory, live: live, database: database)
    }

    private func count(_ db: Database, _ sql: String) throws -> Int {
        try Int.fetchOne(db, sql: sql) ?? -1
    }

    private func exportDirectory(_ fixture: Fixture) -> URL {
        fixture.directory.appendingPathComponent("exports", isDirectory: true)
    }

    private func openEntry(_ data: Data, in directory: URL) throws -> PrayerDatabase {
        let url = directory.appendingPathComponent("opened-\(UUID().uuidString).db")
        try data.write(to: url)
        return try PrayerDatabase(path: url.path)
    }

    @Test func noProtectedCardsReportsNone() throws {
        let fixture = try makeFixture()
        #expect(try BackupExporter.hasConfidentialCards(in: fixture.database) == false)
    }

    @Test func protectedCardIsReported() throws {
        let fixture = try makeFixture(protectCard: true)
        #expect(try BackupExporter.hasConfidentialCards(in: fixture.database) == true)
    }

    @Test func unencryptedExportHoldsOnlyDatabase() throws {
        let fixture = try makeFixture(protectCard: true)
        let now = Date()
        let url = try BackupExporter.export(
            database: fixture.database, passphrase: nil, to: exportDirectory(fixture), now: now)
        let entries = try BackupArchive.entries(of: Data(contentsOf: url))
        #expect(Set(entries.keys) == ["prayer_app.db"])
        #expect(url.lastPathComponent == BackupArchive.fileName(for: now))
    }

    @Test func passphraseExportScrubsAndEncrypts() throws {
        let fixture = try makeFixture(protectCard: true)
        let url = try BackupExporter.export(
            database: fixture.database, passphrase: passphrase, to: exportDirectory(fixture))
        let entries = try BackupArchive.entries(of: Data(contentsOf: url))
        #expect(Set(entries.keys) == ["prayer_app.db", "confidential.enc"])

        let scrubbed = try openEntry(#require(entries["prayer_app.db"]), in: fixture.directory)
        defer { try? scrubbed.close() }
        let remaining = try scrubbed.dbQueue.read { db in
            [
                try count(db, "SELECT COUNT(*) FROM PrayerCard WHERE Id = 2"),
                try count(db, "SELECT COUNT(*) FROM PrayerRequest WHERE PrayerCardId = 2"),
                try count(db, "SELECT COUNT(*) FROM PrayerInteraction WHERE PrayerId IN (5, 6)"),
                try count(db, "SELECT COUNT(*) FROM PrayerCardTag WHERE PrayerRequestId IN (5, 6)"),
            ]
        }
        #expect(remaining == [0, 0, 0, 0])

        let json = try ConfidentialBackupCrypto.decrypt(#require(entries["confidential.enc"]), passphrase: passphrase)
        let payload = try ConfidentialExportPayload.fromJSON(json)
        #expect(payload.cards.map(\.id) == [2])
        #expect(Set(payload.prayers.map(\.id)) == [5, 6])
    }

    @Test func passphraseExportLeavesLiveDatabase() throws {
        let fixture = try makeFixture(protectCard: true)
        _ = try BackupExporter.export(
            database: fixture.database, passphrase: passphrase, to: exportDirectory(fixture))
        let cards = try fixture.database.dbQueue.read { db in
            try count(db, "SELECT COUNT(*) FROM PrayerCard WHERE Id = 2")
        }
        #expect(cards == 1)
        let leftovers = try FileManager.default.contentsOfDirectory(atPath: FileManager.default.temporaryDirectory.path)
            .filter { $0.hasPrefix("prayer_app_scrub_") }
        #expect(leftovers.isEmpty)
    }

    @Test func emptyDatabaseExports() throws {
        let fixture = try makeFixture()
        try fixture.database.dbQueue.write { db in
            try db.execute(sql: "DELETE FROM PrayerCard; DELETE FROM PrayerRequest")
        }
        let url = try BackupExporter.export(
            database: fixture.database, passphrase: nil, to: exportDirectory(fixture))
        #expect(FileManager.default.fileExists(atPath: url.path))
    }
}
