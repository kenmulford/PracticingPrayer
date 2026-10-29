import Foundation
import GRDB

/// Puts the cards from `confidential.enc` back into a restored database, keeping the backup's Ids.
public enum ConfidentialBackupImporter {
    public static func decode(_ blob: Data, passphrase: String) throws -> ConfidentialExportPayload {
        try ConfidentialExportPayload.fromJSON(ConfidentialBackupCrypto.decrypt(blob, passphrase: passphrase))
    }

    public static func insert(_ payload: ConfidentialExportPayload, into database: PrayerDatabase) throws {
        try database.dbQueue.write { db in
            for card in payload.cards { try card.insert(db) }
            for prayer in payload.prayers { try prayer.insert(db) }
            for interaction in payload.interactions { try interaction.insert(db) }
            for junction in payload.tagJunctions { try junction.insert(db) }
        }
    }
}
