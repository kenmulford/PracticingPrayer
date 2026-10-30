import Foundation
import GRDB

/// Builds a `.pcrd` from the live database, scrubbing confidential rows into an encrypted blob when asked.
public enum BackupExporter {
    public static func hasConfidentialCards(in database: PrayerDatabase) throws -> Bool {
        try database.dbQueue.read { db in
            !ConfidentialExportSplitter.split(
                cards: try PrayerCardRecord.fetchAll(db), boxes: try CardBoxRecord.fetchAll(db),
                prayers: [], interactions: [], tagJunctions: []
            ).confidentialCards.isEmpty
        }
    }

    /// Runs inside one read so no write lands mid-export. The live file is only copied, never written.
    public static func export(
        database: PrayerDatabase, passphrase: String?, to exportDirectory: URL, now: Date = Date()
    ) throws -> URL {
        try database.dbQueue.read { db in
            let databaseURL = URL(fileURLWithPath: database.dbQueue.path)
            guard let passphrase else {
                return try BackupArchive.export(
                    databaseURL: databaseURL, confidential: nil, to: exportDirectory, now: now)
            }
            let split = ConfidentialExportSplitter.split(
                cards: try PrayerCardRecord.fetchAll(db), boxes: try CardBoxRecord.fetchAll(db),
                prayers: try PrayerRequestRecord.fetchAll(db), interactions: try PrayerInteractionRecord.fetchAll(db),
                tagJunctions: try PrayerCardTagRecord.fetchAll(db))
            let payload = ConfidentialExportPayload(
                cards: split.confidentialCards, prayers: split.confidentialPrayers,
                interactions: split.confidentialInteractions, tagJunctions: split.confidentialTagJunctions)
            let blob = try ConfidentialBackupCrypto.encrypt(try payload.toJSON(), passphrase: passphrase)
            let scrubbed = FileManager.default.temporaryDirectory
                .appendingPathComponent("prayer_app_scrub_\(UUID().uuidString).db")
            defer { try? FileManager.default.removeItem(at: scrubbed) }
            try ScrubbedDatabase.write(from: databaseURL, to: scrubbed, removing: split)
            return try BackupArchive.export(databaseURL: scrubbed, confidential: blob, to: exportDirectory, now: now)
        }
    }
}
