import Foundation
import GRDB

public enum PrayerDatabaseError: Error, Equatable {
    case fileNotFound
    case notADatabase
}

/// Opens an existing MAUI-owned SQLite file in place. A `DatabaseQueue` (not a pool) keeps the
/// DELETE journal; a pool would require WAL. No PRAGMA is executed, so `user_version` and
/// `journal_mode` stay as the MAUI app left them.
public final class PrayerDatabase: Sendable {
    public let dbQueue: DatabaseQueue

    public init(path: String) throws {
        // GRDB creates a missing file by default.
        guard FileManager.default.fileExists(atPath: path) else {
            throw PrayerDatabaseError.fileNotFound
        }
        do {
            dbQueue = try DatabaseQueue(path: path, configuration: Configuration())
        } catch let error as DatabaseError where error.resultCode == .SQLITE_NOTADB {
            throw PrayerDatabaseError.notADatabase
        }
    }

    public func close() throws {
        try dbQueue.close()
    }
}
