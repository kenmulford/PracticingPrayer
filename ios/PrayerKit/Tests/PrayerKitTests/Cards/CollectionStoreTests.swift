import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func tempCopy(of file: String) throws -> String {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_collectionstore_\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: source, to: dest)
    return dest.path
}

private func removeTemp(_ path: String) {
    for suffix in ["", "-journal", "-wal", "-shm"] {
        try? FileManager.default.removeItem(atPath: path + suffix)
    }
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase, CollectionStore) throws -> T) throws -> T {
    let path = try tempCopy(of: file)
    defer { removeTemp(path) }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try body(database, CollectionStore(database: database))
}

private func count(_ database: PrayerDatabase, _ table: String, where clause: String = "1") throws -> Int {
    try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM \(table) WHERE \(clause)") ?? 0 }
}

private func run(_ database: PrayerDatabase, _ sql: String) throws {
    try database.dbQueue.write { try $0.execute(sql: sql) }
}

private func card(_ database: PrayerDatabase, _ id: Int64) throws -> PrayerCardRecord {
    try #require(database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: id) })
}

private func box(_ database: PrayerDatabase, _ id: Int64) throws -> CardBoxRecord? {
    try database.dbQueue.read { try CardBoxRecord.fetchOne($0, key: id) }
}

@Suite struct CollectionStoreTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    let later = Date(timeIntervalSince1970: 1_800_100_000)

    @Test func createPersistsTrimmedNameWithProtectAllOff() throws {
        try withCopy("fresh-install.db") { database, store in
            let created = try store.create(name: "  Family  ", now: now)
            #expect(created.name == "Family")
            #expect(!created.protectAllCards)
            #expect(!created.isSystem)
            try #expect(count(database, "CardBox") == 3)
        }
    }

    @Test func listOnFreshInstallIsSystemThenArchived() throws {
        try withCopy("fresh-install.db") { _, store in
            try #expect(store.collections().map(\.name) == ["System", "Archived"])
        }
    }

    @Test func listOrdersBySortOrderThenName() throws {
        try withCopy("fresh-install.db") { _, store in
            _ = try store.create(name: "Zeta", now: now)
            _ = try store.create(name: "alpha", now: now)
            try #expect(store.collections().map(\.name) == ["alpha", "Zeta", "System", "Archived"])
        }
    }

    @Test func renameBumpsUpdatedAt() throws {
        try withCopy("fresh-install.db") { database, store in
            let created = try store.create(name: "Home", now: now)
            try store.rename(id: created.id, to: "Work", now: later)
            let fetched = try #require(try box(database, created.id))
            #expect(fetched.name == "Work")
            #expect(fetched.updatedAtTicks == later.ticks)
        }
    }

    @Test func emptyWhitespaceOrOverlongNameThrowsAndWritesNothing() throws {
        try withCopy("fresh-install.db") { database, store in
            #expect(throws: CollectionError.emptyName) { try store.create(name: "   ", now: now) }
            #expect(throws: CollectionError.emptyName) {
                try store.create(name: String(repeating: "a", count: 51), now: now)
            }
            try #expect(count(database, "CardBox") == 2)
        }
    }

    @Test func duplicateNameThrowsAndWritesNothing() throws {
        try withCopy("fresh-install.db") { database, store in
            #expect(throws: CollectionError.duplicateName) { try store.create(name: "System", now: now) }
            try #expect(count(database, "CardBox") == 2)
            let first = try store.create(name: "First", now: now)
            let second = try store.create(name: "Second", now: now)
            #expect(throws: CollectionError.duplicateName) {
                try store.rename(id: second.id, to: "First", now: later)
            }
            try #expect(box(database, first.id) == first)
            try #expect(box(database, second.id) == second)
            try #expect(count(database, "CardBox") == 4)
        }
    }

    @Test func protectAllCascadesToUnprotectedCardsOnly() throws {
        try withCopy("long-upgraded.db") { database, store in
            let created = try store.create(name: "Locked", now: now)
            try run(database, "UPDATE PrayerCard SET BoxId = \(created.id) WHERE Id IN (2, 3)")
            try run(database, "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 3")
            try store.setProtection(id: created.id, protectAllCards: true, mode: .hidden, now: later)
            let updated = try #require(try box(database, created.id))
            try #expect(ProtectionPolicy.effectiveMode(of: card(database, 2), box: updated) == .hidden)
            try #expect(ProtectionPolicy.effectiveMode(of: card(database, 3), box: updated) == .lockedVisible)
        }
    }

    @Test func protectAllOffResolvesToNoneWithoutRewritingCards() throws {
        try withCopy("long-upgraded.db") { database, store in
            let created = try store.create(name: "Locked", now: now)
            try run(database, "UPDATE PrayerCard SET BoxId = \(created.id) WHERE Id IN (2, 3)")
            let before = try card(database, 2)
            try store.setProtection(id: created.id, protectAllCards: true, mode: .hidden, now: later)
            try store.setProtection(id: created.id, protectAllCards: false, mode: .hidden, now: later)
            let updated = try #require(try box(database, created.id))
            try #expect(ProtectionPolicy.effectiveMode(of: card(database, 2), box: updated) == .unprotected)
            try #expect(card(database, 2) == before)
        }
    }

    @Test func deleteUnassignKeepsCardsPrayersInteractions() throws {
        try withCopy("long-upgraded.db") { database, store in
            let created = try store.create(name: "Temp", now: now)
            try run(database, "UPDATE PrayerCard SET BoxId = \(created.id) WHERE Id IN (2, 3)")
            try store.delete(id: created.id, option: .unassignCards)
            try #expect(card(database, 2).boxId == 0)
            try #expect(card(database, 3).boxId == 0)
            try #expect(count(database, "PrayerCard") == 4)
            try #expect(count(database, "PrayerRequest") == 7)
            try #expect(count(database, "PrayerInteraction") == 3)
            try #expect(box(database, created.id) == nil)
        }
    }

    @Test func deleteAllRemovesCardsAndPrayers() throws {
        try withCopy("long-upgraded.db") { database, store in
            let created = try store.create(name: "Temp", now: now)
            try run(database, "UPDATE PrayerCard SET BoxId = \(created.id) WHERE Id IN (2, 3)")
            try store.delete(id: created.id, option: .deleteAll)
            try #expect(count(database, "PrayerCard") == 2)
            try #expect(count(database, "PrayerRequest") == 4)
            try #expect(count(database, "PrayerInteraction") == 0)
            try #expect(box(database, created.id) == nil)
        }
    }

    @Test func deleteEmptyCollectionUnderEitherOption() throws {
        try withCopy("fresh-install.db") { database, store in
            let one = try store.create(name: "One", now: now)
            let two = try store.create(name: "Two", now: now)
            try store.delete(id: one.id, option: .unassignCards)
            try store.delete(id: two.id, option: .deleteAll)
            try #expect(count(database, "CardBox") == 2)
        }
    }

    @Test func systemAndArchivedAreImmutable() throws {
        try withCopy("fresh-install.db") { database, store in
            for id: Int64 in [1, 2] {
                let before = try #require(try box(database, id))
                try store.rename(id: id, to: "Renamed", now: later)
                try store.setProtection(id: id, protectAllCards: true, mode: .hidden, now: later)
                try store.delete(id: id, option: .deleteAll)
                try #expect(box(database, id) == before)
            }
        }
    }

    @Test func missingIdIsNoOp() throws {
        try withCopy("fresh-install.db") { database, store in
            try store.delete(id: 999, option: .deleteAll)
            try store.rename(id: 999, to: "Nope", now: later)
            try #expect(count(database, "CardBox") == 2)
        }
    }
}
