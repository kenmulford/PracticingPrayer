import Foundation
import GRDB

public enum ScrubbedDatabase {
    /// Copies the database file and deletes the confidential rows from the copy. The journal mode is
    /// DELETE, so the copy needs no checkpoint. `secure_delete` zeroes deleted cells and `VACUUM` rebuilds
    /// the file, so no confidential text survives in free pages of the exported copy.
    public static func write(
        from source: URL, to destination: URL, removing split: ConfidentialExportSplitter.SplitResult
    ) throws {
        try FileManager.default.copyItem(at: source, to: destination)
        let database = try PrayerDatabase(path: destination.path)
        do {
            try database.dbQueue.writeWithoutTransaction { db in
                try db.execute(sql: "PRAGMA secure_delete = ON")
                try db.inTransaction {
                    _ = try PrayerCardRecord.deleteAll(db, keys: split.confidentialCards.map(\.id))
                    _ = try PrayerRequestRecord.deleteAll(db, keys: split.confidentialPrayers.map(\.id))
                    _ = try PrayerInteractionRecord.deleteAll(db, keys: split.confidentialInteractions.map(\.id))
                    _ = try PrayerCardTagRecord.deleteAll(db, keys: split.confidentialTagJunctions.map(\.id))
                    return .commit
                }
                try db.execute(sql: "VACUUM")
            }
        } catch {
            try? database.close()
            throw error
        }
        try database.close()
    }
}
