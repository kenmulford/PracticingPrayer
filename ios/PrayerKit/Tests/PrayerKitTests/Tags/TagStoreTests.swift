import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase) throws -> T) throws -> T {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_tags_\(UUID().uuidString).db").path
    try FileManager.default.copyItem(at: source, to: URL(fileURLWithPath: path))
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try body(database)
}

private let now = Date(timeIntervalSince1970: 1_800_000_000)

private func fetchTag(_ database: PrayerDatabase, _ id: Int64) throws -> PrayerTagRecord? {
    try database.dbQueue.read { try PrayerTagRecord.fetchOne($0, key: id) }
}

private func allTags(_ database: PrayerDatabase) throws -> [PrayerTagRecord] {
    try database.dbQueue.read { try PrayerTagRecord.order(Column("Id")).fetchAll($0) }
}

@Suite struct TagStoreTests {
    @Test func listsSystemFirstThenByNameWithCounts() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write { db in
                for _ in 0..<2 {
                    try db.execute(sql: """
                        INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt)
                        VALUES (1, 1, 1, 638712864000000000)
                        """)
                }
            }
            let summaries = try TagStore(database: database).tags(isSessionUnlocked: true)
            #expect(summaries.map(\.tag.name) == [
                "Recently Notified", "Family", "Fixture Tag 1", "Fixture Tag 2", "Urgent", "Work",
            ])
            #expect(summaries.first { $0.tag.id == 1 }?.prayerCount == 1)
            #expect(summaries.first { $0.tag.id == 4 }?.prayerCount == 0)
        }
    }

    @Test func lockedCountsExcludeHiddenCardPrayers() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write { db in
                try db.execute(sql: """
                    INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt)
                    VALUES (1, 1, 1, 638712864000000000)
                    """)
                try db.execute(sql: """
                    UPDATE PrayerCard SET ProtectionMode = 2
                    WHERE Id = (SELECT PrayerCardId FROM PrayerRequest WHERE Id = 1)
                    """)
            }
            let store = TagStore(database: database)
            #expect(try store.tags(isSessionUnlocked: false).first { $0.tag.id == 1 }?.prayerCount == 0)
            #expect(try store.tags(isSessionUnlocked: true).first { $0.tag.id == 1 }?.prayerCount == 1)
        }
    }

    @Test func emptyTableListsEmpty() throws {
        try withCopy("long-upgraded.db") { database in
            try database.dbQueue.write { try $0.execute(sql: "DELETE FROM PrayerTag") }
            #expect(try TagStore(database: database).tags(isSessionUnlocked: true).isEmpty)
        }
    }

    @Test func createStoresTag() throws {
        try withCopy("fresh-install.db") { database in
            let created = try TagStore(database: database).createTag(name: "Hope", color: "#112233", now: now)
            #expect(try fetchTag(database, created.id) == created)
            #expect(created.isSystem == false)
            #expect(created.createdAtTicks == now.ticks)
            #expect(created.updatedAtTicks == now.ticks)
            #expect(created.name == "Hope")
            #expect(created.color == "#112233")
        }
    }

    @Test func renameAndRecolorUpdateFields() throws {
        try withCopy("long-upgraded.db") { database in
            let store = TagStore(database: database)
            try store.renameTag(id: 2, to: "Kin", now: now)
            try store.recolorTag(id: 2, to: "#ABCDEF", now: now)
            let tag = try #require(try fetchTag(database, 2))
            #expect(tag.name == "Kin")
            #expect(tag.color == "#ABCDEF")
            #expect(tag.updatedAtTicks == now.ticks)
        }
    }

    @Test func duplicateNameOnCreateThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let before = try allTags(database)
            #expect(throws: TagError.duplicateName) {
                try TagStore(database: database).createTag(name: "Work", color: "#000000", now: now)
            }
            #expect(try allTags(database) == before)
        }
    }

    @Test func duplicateNameOnRenameThrows() throws {
        try withCopy("long-upgraded.db") { database in
            let before = try allTags(database)
            #expect(throws: TagError.duplicateName) {
                try TagStore(database: database).renameTag(id: 2, to: "Work", now: now)
            }
            #expect(try allTags(database) == before)
        }
    }

    @Test func systemTagRenameAndDeleteThrow() throws {
        try withCopy("long-upgraded.db") { database in
            let before = try allTags(database)
            let store = TagStore(database: database)
            #expect(throws: TagError.systemTag) { try store.renameTag(id: 6, to: "Other", now: now) }
            #expect(throws: TagError.systemTag) { try store.deleteTag(id: 6) }
            #expect(try allTags(database) == before)
        }
    }

    @Test func systemTagRecolorAllowed() throws {
        try withCopy("long-upgraded.db") { database in
            try TagStore(database: database).recolorTag(id: 6, to: "#010203", now: now)
            #expect(try fetchTag(database, 6)?.color == "#010203")
        }
    }

    @Test func deleteRemovesTagAndAssignments() throws {
        try withCopy("long-upgraded.db") { database in
            try TagStore(database: database).deleteTag(id: 4)
            #expect(try fetchTag(database, 4) == nil)
            let left = try database.dbQueue.read {
                try PrayerCardTagRecord.filter(Column("PrayerTagId") == 4).fetchCount($0)
            }
            #expect(left == 0)
        }
    }

    @Test func seedAddsSystemTagOnceWhenAbsent() throws {
        try withCopy("fresh-install.db") { database in
            try database.dbQueue.write { try $0.execute(sql: "DELETE FROM PrayerTag") }
            let store = TagStore(database: database)
            try store.seedSystemTags(now: now)
            try store.seedSystemTags(now: now)
            let tags = try allTags(database)
            #expect(tags.count == 1)
            #expect(tags[0].name == TagStore.recentlyNotifiedName)
            #expect(tags[0].color == "#505050")
            #expect(tags[0].isSystem == true)
        }
    }
}
