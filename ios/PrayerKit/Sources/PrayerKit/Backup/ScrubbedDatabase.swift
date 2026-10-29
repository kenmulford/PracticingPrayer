import Foundation
import GRDB

public enum ScrubbedDatabase {
    /// Copies the database file and deletes the confidential rows from the copy. The journal mode is
    /// DELETE, so the copy needs no checkpoint.
    public static func write(
        from source: URL, to destination: URL, removing split: ConfidentialExportSplitter.SplitResult
    ) throws {
        try FileManager.default.copyItem(at: source, to: destination)
        let database = try PrayerDatabase(path: destination.path)
        do {
            try database.dbQueue.write { db in
                _ = try PrayerCardRecord.deleteAll(db, keys: split.confidentialCards.map(\.id))
                _ = try PrayerRequestRecord.deleteAll(db, keys: split.confidentialPrayers.map(\.id))
                _ = try PrayerInteractionRecord.deleteAll(db, keys: split.confidentialInteractions.map(\.id))
                _ = try PrayerCardTagRecord.deleteAll(db, keys: split.confidentialTagJunctions.map(\.id))
            }
        } catch {
            try? database.close()
            throw error
        }
        try database.close()
    }
}
