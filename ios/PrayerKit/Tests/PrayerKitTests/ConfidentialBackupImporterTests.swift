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

@Suite struct ConfidentialBackupImporterTests {
    private func exportedArchive(in directory: URL) throws -> URL {
        let live = directory.appendingPathComponent("live.db")
        try FileManager.default.copyItem(
            at: repoRoot().appendingPathComponent("contracts/db/long-upgraded.db"), to: live)
        let database = try PrayerDatabase(path: live.path)
        try database.dbQueue.write { db in
            try db.execute(sql: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 2")
        }
        let url = try scrubFileLock.withLock {
            try BackupExporter.export(
                database: database, passphrase: passphrase, to: directory.appendingPathComponent("exports"))
        }
        try database.close()
        return url
    }

    private func makeDirectory() throws -> URL {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("pk_importer_\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    @Test func roundTripRestoresProtectedCard() throws {
        let directory = try makeDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let archive = try exportedArchive(in: directory)
        let target = directory.appendingPathComponent("target.db")
        try FileManager.default.copyItem(
            at: repoRoot().appendingPathComponent("contracts/db/fresh-install.db"), to: target)

        let result = try BackupArchive.restore(archiveURL: archive, databaseURL: target, upkeep: { _ in })
        let payload = try ConfidentialBackupImporter.decode(#require(result.confidentialPayload), passphrase: passphrase)
        let database = try PrayerDatabase(path: target.path)
        defer { try? database.close() }
        try ConfidentialBackupImporter.insert(payload, into: database)

        let mode = try database.dbQueue.read { db in
            try Int.fetchOne(db, sql: "SELECT ProtectionMode FROM PrayerCard WHERE Id = 2")
        }
        #expect(mode == 1)
        let prayers = try database.dbQueue.read { db in
            try Int.fetchOne(db, sql: "SELECT COUNT(*) FROM PrayerRequest WHERE PrayerCardId = 2")
        }
        #expect(prayers == payload.prayers.count)
    }

    @Test func wrongPassphraseThrows() throws {
        let directory = try makeDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let archive = try exportedArchive(in: directory)
        let blob = try #require(BackupArchive.inspect(archiveURL: archive).confidentialPayload)
        #expect(throws: ConfidentialBackupError.authenticationFailed) {
            try ConfidentialBackupImporter.decode(blob, passphrase: "another passphrase!")
        }
    }
}
