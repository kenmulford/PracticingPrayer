import Foundation
import GRDB

public enum CollectionError: Error, Equatable { case emptyName, duplicateName }

public enum CollectionDeleteOption: Sendable, Equatable { case unassignCards, deleteAll }

public struct CollectionStore: Sendable {
    public static let maxNameLength = 50

    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    public func collections() throws -> [CardBoxRecord] {
        let records = try database.dbQueue.read { try CardBoxRecord.fetchAll($0) }
        return records.sorted { lhs, rhs in
            if lhs.sortOrder != rhs.sortOrder { return lhs.sortOrder < rhs.sortOrder }
            let order = lhs.name.localizedCaseInsensitiveCompare(rhs.name)
            if order != .orderedSame { return order == .orderedAscending }
            return lhs.id < rhs.id
        }
    }

    public func create(name: String, now: Date) throws -> CardBoxRecord {
        let trimmed = try validated(name)
        let ticks = now.ticks
        return try mappingDuplicate {
            try database.dbQueue.write { db in
                // Raw insert: the record's encode always writes Id, which would pin it to 0.
                try db.execute(
                    sql: """
                        INSERT INTO CardBox (Name, IsSystem, SystemKey, SortOrder, ProtectAllCards,
                            CardProtectionMode, CreatedAt, UpdatedAt)
                        VALUES (?, 0, NULL, 0, 0, 0, ?, ?)
                        """,
                    arguments: [trimmed, ticks, ticks])
                return try CardBoxRecord.find(db, key: db.lastInsertedRowID)
            }
        }
    }

    public func rename(id: Int64, to name: String, now: Date) throws {
        let trimmed = try validated(name)
        try mappingDuplicate {
            try database.dbQueue.write { db in
                guard var record = try CardBoxRecord.fetchOne(db, key: id), !record.isSystem else { return }
                record.name = trimmed
                record.updatedAtTicks = now.ticks
                try record.update(db)
            }
        }
    }

    public func setProtection(id: Int64, protectAllCards: Bool, mode: CardProtectionMode, now: Date) throws {
        try database.dbQueue.write { db in
            guard var record = try CardBoxRecord.fetchOne(db, key: id), !record.isSystem else { return }
            record.protectAllCards = protectAllCards
            record.cardProtectionMode = mode
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func delete(id: Int64, option: CollectionDeleteOption) throws {
        try database.dbQueue.write { db in
            guard let record = try CardBoxRecord.fetchOne(db, key: id), !record.isSystem else { return }
            switch option {
            case .unassignCards:
                try db.execute(sql: "UPDATE PrayerCard SET BoxId = 0 WHERE BoxId = ?", arguments: [id])
            case .deleteAll:
                let cardIds = try Int64.fetchAll(
                    db, sql: "SELECT Id FROM PrayerCard WHERE BoxId = ? ORDER BY Id", arguments: [id])
                for cardId in cardIds {
                    try CardMutations.deleteRows(cardId: cardId, db)
                }
            }
            _ = try CardBoxRecord.deleteOne(db, key: id)
        }
    }

    private func validated(_ name: String) throws -> String {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed.count > Self.maxNameLength { throw CollectionError.emptyName }
        return trimmed
    }

    private func mappingDuplicate<T>(_ body: () throws -> T) throws -> T {
        do {
            return try body()
        } catch let error as DatabaseError where error.extendedResultCode == .SQLITE_CONSTRAINT_UNIQUE {
            throw CollectionError.duplicateName
        }
    }
}
