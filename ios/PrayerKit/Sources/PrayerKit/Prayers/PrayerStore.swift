import Foundation
import GRDB

public enum PrayerSegment: Sendable, CaseIterable { case active, answered, all }

public enum PrayerStoreError: Error, Equatable {
    case emptyTitle
    case cardNotFound(Int64)
    case prayerNotFound(Int64)
}

public struct PrayerDraft: Sendable, Equatable {
    public var prayerCardId: Int64
    public var title: String
    public var details: String?
    public var tagIds: [Int64]
    public var isImported: Bool?

    public init(
        prayerCardId: Int64, title: String, details: String? = nil, tagIds: [Int64] = [],
        isImported: Bool? = nil
    ) {
        self.prayerCardId = prayerCardId
        self.title = title
        self.details = details
        self.tagIds = tagIds
        self.isImported = isImported
    }
}

public struct PrayerListItem: Sendable, Equatable {
    public var record: PrayerRequestRecord
    public var cardTitle: String
    public var tagIds: [Int64]
}

public struct PrayerStore: Sendable {
    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    public func create(_ draft: PrayerDraft, now: Date) throws -> Int64 {
        try validate(draft)
        return try database.dbQueue.write { db in
            try requireCard(draft.prayerCardId, db)
            let ticks = now.ticks
            // Raw insert: the record's encode always writes Id, which would pin it to 0.
            try db.execute(
                sql: """
                    INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency,
                        NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate,
                        IsImported, IsAnswered, AnsweredAt, CreatedAt, UpdatedAt)
                    VALUES (?, ?, ?, 0, ?, 9, 0, -1, -1, NULL, ?, 0, NULL, ?, ?)
                    """,
                arguments: [
                    draft.prayerCardId, draft.title, draft.details, PrayerFrequency.weekly.rawValue,
                    draft.isImported ?? false, ticks, ticks,
                ])
            let id = db.lastInsertedRowID
            try replaceTags(id, draft.tagIds, ticks, db)
            return id
        }
    }

    public func update(id: Int64, with draft: PrayerDraft, now: Date) throws {
        try validate(draft)
        try database.dbQueue.write { db in
            var record = try requirePrayer(id, db)
            try requireCard(draft.prayerCardId, db)
            record.title = draft.title
            record.details = draft.details
            record.prayerCardId = draft.prayerCardId
            record.updatedAtTicks = now.ticks
            try record.update(db)
            try replaceTags(id, draft.tagIds, now.ticks, db)
        }
    }

    public func delete(id: Int64) throws {
        try database.dbQueue.write { db in
            guard try Self.deleteRows(prayerId: id, db) else { throw PrayerStoreError.prayerNotFound(id) }
        }
    }

    // GRDB access is not reentrant, so a card delete runs this inside its own write.
    static func deleteRows(prayerId: Int64, _ db: Database) throws -> Bool {
        try db.execute(sql: "DELETE FROM PrayerInteraction WHERE PrayerId = ?", arguments: [prayerId])
        try db.execute(sql: "DELETE FROM PrayerCardTag WHERE PrayerRequestId = ?", arguments: [prayerId])
        return try PrayerRequestRecord.deleteOne(db, key: prayerId)
    }

    public func markAnswered(id: Int64, on answeredDate: Date? = nil, now: Date) throws {
        try database.dbQueue.write { db in
            var record = try requirePrayer(id, db)
            record.isAnswered = true
            record.answeredAtTicks = Calendar.current.startOfDay(for: answeredDate ?? now).ticks
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func markActive(id: Int64, now: Date) throws {
        try database.dbQueue.write { db in
            var record = try requirePrayer(id, db)
            record.isAnswered = false
            record.answeredAtTicks = nil
            record.updatedAtTicks = now.ticks
            try record.update(db)
        }
    }

    public func prayers(
        segment: PrayerSegment, search: String = "", tagIds: Set<Int64> = [],
        isSessionUnlocked: Bool = true
    ) throws -> [PrayerListItem] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        var blocked: Set<Int64> = []
        if !isSessionUnlocked {
            let (cards, boxes) = try database.dbQueue.read { db in
                (try PrayerCardRecord.fetchAll(db), try CardBoxRecord.fetchAll(db))
            }
            blocked = ProtectionPolicy.blockedCardIds(cards: cards, boxes: boxes, isSessionUnlocked: false)
        }
        return try loadItems().filter { item in
            if blocked.contains(item.record.prayerCardId) { return false }
            switch segment {
            case .active where item.record.isAnswered, .answered where !item.record.isAnswered:
                return false
            default:
                break
            }
            if !tagIds.isEmpty && tagIds.isDisjoint(with: item.tagIds) { return false }
            if query.isEmpty { return true }
            return item.record.title.range(of: query, options: .caseInsensitive) != nil
                || item.cardTitle.range(of: query, options: .caseInsensitive) != nil
        }
    }

    public func prayers(cardId: Int64) throws -> [PrayerListItem] {
        try loadItems().filter { $0.record.prayerCardId == cardId }
    }

    private func validate(_ draft: PrayerDraft) throws {
        if draft.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            throw PrayerStoreError.emptyTitle
        }
    }

    private func requireCard(_ id: Int64, _ db: Database) throws {
        guard try PrayerCardRecord.exists(db, key: id) else { throw PrayerStoreError.cardNotFound(id) }
    }

    private func requirePrayer(_ id: Int64, _ db: Database) throws -> PrayerRequestRecord {
        guard let record = try PrayerRequestRecord.fetchOne(db, key: id) else {
            throw PrayerStoreError.prayerNotFound(id)
        }
        return record
    }

    private func replaceTags(_ id: Int64, _ tagIds: [Int64], _ ticks: Int64, _ db: Database) throws {
        try db.execute(sql: "DELETE FROM PrayerCardTag WHERE PrayerRequestId = ?", arguments: [id])
        for tagId in Set(tagIds).sorted() {
            try db.execute(
                sql: """
                    INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt)
                    VALUES (0, ?, ?, ?)
                    """,
                arguments: [tagId, id, ticks])
        }
    }

    private func loadItems() throws -> [PrayerListItem] {
        let (records, titles, tagRows) = try database.dbQueue.read { db in
            (
                try PrayerRequestRecord.fetchAll(db),
                try Row.fetchAll(db, sql: "SELECT Id, Title FROM PrayerCard"),
                try Row.fetchAll(
                    db,
                    sql: "SELECT PrayerRequestId, PrayerTagId FROM PrayerCardTag WHERE PrayerRequestId IS NOT NULL")
            )
        }
        var cardTitles: [Int64: String] = [:]
        for row in titles { cardTitles[row["Id"]] = row["Title"] }
        var tagsByPrayer: [Int64: Set<Int64>] = [:]
        for row in tagRows { tagsByPrayer[row["PrayerRequestId"], default: []].insert(row["PrayerTagId"]) }
        return records
            .map {
                PrayerListItem(
                    record: $0, cardTitle: cardTitles[$0.prayerCardId] ?? "",
                    tagIds: (tagsByPrayer[$0.id] ?? []).sorted())
            }
            .sorted { a, b in
                switch a.cardTitle.compare(b.cardTitle, options: .caseInsensitive) {
                case .orderedAscending: return true
                case .orderedDescending: return false
                default: break
                }
                switch a.record.title.compare(b.record.title, options: .caseInsensitive) {
                case .orderedAscending: return true
                case .orderedDescending: return false
                default: return a.record.id < b.record.id
                }
            }
    }
}
