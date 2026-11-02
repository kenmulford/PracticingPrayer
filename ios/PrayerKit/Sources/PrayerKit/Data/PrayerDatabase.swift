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

    private init(creating path: String) throws {
        dbQueue = try DatabaseQueue(path: path, configuration: Configuration())
    }

    /// Opens the file, or creates an empty one when none exists (a fresh install). The caller runs
    /// `SchemaUpkeep.run` next, which builds the 1.7.2 schema and seeds the System and Archived boxes.
    public static func openOrCreate(path: String) throws -> PrayerDatabase {
        if FileManager.default.fileExists(atPath: path) { return try PrayerDatabase(path: path) }
        try FileManager.default.createDirectory(
            at: URL(fileURLWithPath: path).deletingLastPathComponent(), withIntermediateDirectories: true)
        return try PrayerDatabase(creating: path)
    }

    public func close() throws {
        try dbQueue.close()
    }
}
