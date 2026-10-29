import Foundation
import GRDB

public enum CardMutationError: Error, Equatable {
    case emptyTitle
    case cardNotFound(Int64)
    case boxNotFound(Int64)
    case systemCard(Int64)
    case archivedFolderUnset
}

public struct CardDraft: Sendable, Equatable {
    public var title: String
    public var boxId: Int64
    public var protectionMode: CardProtectionMode
    public var isImported: Bool?

    public init(
        title: String, boxId: Int64 = 0, protectionMode: CardProtectionMode = .unprotected,
        isImported: Bool? = nil
    ) {
        self.title = title
        self.boxId = boxId
        self.protectionMode = protectionMode
        self.isImported = isImported
    }
}

// Not Sendable: AppPreferences holds UserDefaults.
public struct CardMutations {
    private let database: PrayerDatabase
    private let preferences: AppPreferences

    public init(database: PrayerDatabase, preferences: AppPreferences) {
        self.database = database
        self.preferences = preferences
    }

    public func create(_ draft: CardDraft, now: Date) throws -> PrayerCardRecord {
        try validate(draft)
        return try database.dbQueue.write { db in
            try requireBox(draft.boxId, db)
            let id = try Self.insertRow(draft, ticks: now.ticks, db)
            return try requireCard(id, db)
        }
    }

    // GRDB access is not reentrant, so an import inserts inside its own write.
    static func insertRow(_ draft: CardDraft, ticks: Int64, _ db: Database) throws -> Int64 {
        // Raw insert: the record's encode always writes Id, which would pin it to 0.
        try db.execute(
            sql: """
                INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, IsSystem,
                    IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey, CreatedAt, UpdatedAt)
                VALUES (?, 0, ?, 0, 0, 0, ?, ?, ?, NULL, NULL, ?, ?)
                """,
            arguments: [
                draft.title, PrayerFrequency.weekly.rawValue, draft.isImported ?? false,
                draft.protectionMode.rawValue, draft.boxId, ticks, ticks,
            ])
        return db.lastInsertedRowID
    }

    public func update(id: Int64, with draft: CardDraft, now: Date) throws {
        try validate(draft)
        try database.dbQueue.write { db in
            var record = try requireCard(id, db)
            try requireBox(draft.boxId, db)
            record.title = draft.title
            record.boxId = draft.boxId
            record.protectionMode = draft.protectionMode
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func setFavorite(id: Int64, _ isFavorite: Bool, now: Date) throws {
        try database.dbQueue.write { db in
            var record = try requireCard(id, db)
            record.isFavorite = isFavorite
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func move(id: Int64, toBox boxId: Int64, now: Date) throws {
        try database.dbQueue.write { db in
            var record = try requireCard(id, db)
            try requireBox(boxId, db)
            record.boxId = boxId
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func archive(id: Int64, now: Date) throws {
        let folder = Int64(preferences.archivedFolderId)
        try database.dbQueue.write { db in
            var record = try requireMutableCard(id, db)
            guard folder != 0 else { throw CardMutationError.archivedFolderUnset }
            let current = record.boxId ?? 0
            if current == folder { return }
            record.preArchiveBoxId = current
            record.boxId = folder
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func unarchive(id: Int64, now: Date) throws {
        let folder = Int64(preferences.archivedFolderId)
        try database.dbQueue.write { db in
            var record = try requireMutableCard(id, db)
            if folder == 0 || (record.boxId ?? 0) != folder { return }
            var target: Int64 = 0
            if let stash = record.preArchiveBoxId, stash != 0, try CardBoxRecord.exists(db, key: stash) {
                target = stash
            }
            record.boxId = target
            record.preArchiveBoxId = nil
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func delete(id: Int64) throws {
        try database.dbQueue.write { db in
            _ = try requireMutableCard(id, db)
            try Self.deleteRows(cardId: id, db)
        }
    }

    // GRDB access is not reentrant, so a collection delete runs this inside its own write.
    static func deleteRows(cardId: Int64, _ db: Database) throws {
        let prayerIds = try Int64.fetchAll(
            db, sql: "SELECT Id FROM PrayerRequest WHERE PrayerCardId = ? ORDER BY Id", arguments: [cardId])
        for prayerId in prayerIds {
            _ = try PrayerStore.deleteRows(prayerId: prayerId, db)
        }
        _ = try PrayerCardRecord.deleteOne(db, key: cardId)
    }

    private func validate(_ draft: CardDraft) throws {
        if draft.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            throw CardMutationError.emptyTitle
        }
    }

    private func requireCard(_ id: Int64, _ db: Database) throws -> PrayerCardRecord {
        guard let record = try PrayerCardRecord.fetchOne(db, key: id) else {
            throw CardMutationError.cardNotFound(id)
        }
        return record
    }

    private func requireMutableCard(_ id: Int64, _ db: Database) throws -> PrayerCardRecord {
        let record = try requireCard(id, db)
        if record.isSystem == true { throw CardMutationError.systemCard(id) }
        return record
    }

    private func requireBox(_ id: Int64, _ db: Database) throws {
        if id != 0, try !CardBoxRecord.exists(db, key: id) { throw CardMutationError.boxNotFound(id) }
    }
}
