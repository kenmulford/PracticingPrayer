import Foundation
import GRDB

public enum BackupExporterError: Error, Equatable {
    case passphraseTooShort
    /// A protected card exists and the caller did not acknowledge a plaintext export.
    case confidentialCardsPresent
}

/// Builds a `.pcrd` from the live database, scrubbing confidential rows into an encrypted blob when asked.
public enum BackupExporter {
    public static func hasConfidentialCards(in database: PrayerDatabase) throws -> Bool {
        try database.dbQueue.read(containsConfidentialCards)
    }

    private static func containsConfidentialCards(_ db: Database) throws -> Bool {
        !ConfidentialExportSplitter.split(
            cards: try PrayerCardRecord.fetchAll(db), boxes: try CardBoxRecord.fetchAll(db),
            prayers: [], interactions: [], tagJunctions: []
        ).confidentialCards.isEmpty
    }

    /// Runs inside one read so no write lands mid-export. The live file is only copied, never written.
    /// A plaintext export re-checks for protected cards in that read (one may have appeared since the caller's
    /// check) and refuses unless `acknowledgingPlaintext`, the user's explicit "Continue Unencrypted".
    public static func export(
        database: PrayerDatabase, passphrase: String?, acknowledgingPlaintext: Bool = false,
        to exportDirectory: URL, now: Date = Date()
    ) throws -> URL {
        if let passphrase, !PassphrasePolicy.meetsMinimumLength(passphrase) {
            throw BackupExporterError.passphraseTooShort
        }
        return try database.dbQueue.read { db in
            let databaseURL = URL(fileURLWithPath: database.dbQueue.path)
            guard let passphrase else {
                if !acknowledgingPlaintext, try containsConfidentialCards(db) {
                    throw BackupExporterError.confidentialCardsPresent
                }
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
