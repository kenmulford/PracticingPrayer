import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private func contractDbURL(_ file: String) -> URL {
    repoRoot().appendingPathComponent("contracts/db/\(file)")
}

private func tempCopy(of source: URL) throws -> String {
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_prayerstore_\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: source, to: dest)
    return dest.path
}

private func removeTemp(_ path: String) {
    for suffix in ["", "-journal", "-wal", "-shm"] {
        try? FileManager.default.removeItem(atPath: path + suffix)
    }
}

private func withCopy<T>(_ file: String, _ body: (PrayerDatabase) throws -> T) throws -> T {
    let path = try tempCopy(of: contractDbURL(file))
    defer { removeTemp(path) }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try body(database)
}

private func count(_ database: PrayerDatabase, _ table: String, where clause: String = "1") throws -> Int {
    try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM \(table) WHERE \(clause)") ?? 0 }
}

private func ids(_ items: [PrayerListItem]) -> [Int64] { items.map(\.record.id) }

private func item(_ store: PrayerStore, _ id: Int64) throws -> PrayerListItem {
    try #require(store.prayers(segment: .all, isSessionUnlocked: true).first { $0.record.id == id })
}

@Suite struct PrayerStoreTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func createPersistsAndActiveReturnsIt() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            let id = try store.create(PrayerDraft(prayerCardId: 1, title: "Heal"), now: now)
            let active = try store.prayers(segment: .active, isSessionUnlocked: true)
            #expect(ids(active) == [id])
            #expect(active[0].cardTitle == "Quick Add")
        }
    }

    @Test func editPersistsTitleDetailsCardAndTags() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try store.update(
                id: 5, with: PrayerDraft(prayerCardId: 3, title: "New", details: "Body", tagIds: [4]), now: now)
            let fetched = try item(store, 5)
            #expect(fetched.record.title == "New")
            #expect(fetched.record.details == "Body")
            #expect(fetched.record.prayerCardId == 3)
            #expect(fetched.tagIds == [4])
        }
    }

    @Test func markAnsweredStampsMidnightOfNow() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try store.markAnswered(id: 5, now: now)
            let record = try item(store, 5).record
            #expect(record.isAnswered)
            #expect(record.answeredAtTicks == Calendar.current.startOfDay(for: now).ticks)
        }
    }

    @Test func markAnsweredExplicitDateOverrides() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            let other = now.addingTimeInterval(-10 * 86_400)
            try store.markAnswered(id: 5, on: other, now: now)
            try #expect(item(store, 5).record.answeredAtTicks == Calendar.current.startOfDay(for: other).ticks)
        }
    }

    @Test func markActiveClearsAnsweredAt() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try store.markAnswered(id: 5, now: now)
            try store.markActive(id: 5, now: now)
            let record = try item(store, 5).record
            #expect(!record.isAnswered)
            #expect(record.answeredAtTicks == nil)
        }
    }

    @Test func segmentsSplitAnswered() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try store.markAnswered(id: 5, now: now)
            let active = try ids(store.prayers(segment: .active, isSessionUnlocked: true))
            #expect(!active.contains(5) && !active.contains(6))
            try #expect(ids(store.prayers(segment: .answered, isSessionUnlocked: true)) == [5, 6])
            try #expect(store.prayers(segment: .all, isSessionUnlocked: true).count == 7)
        }
    }

    @Test func allSortsByCardTitleThenTitle() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try #expect(ids(store.prayers(segment: .all, isSessionUnlocked: true)) == [5, 6, 7, 1, 2, 3, 4])
        }
    }

    @Test func sortIsCaseInsensitive() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            for title in ["beta", "Alpha", "Charlie"] {
                _ = try store.create(PrayerDraft(prayerCardId: 1, title: title), now: now)
            }
            try #expect(store.prayers(segment: .all, isSessionUnlocked: true).map(\.record.title) == ["Alpha", "beta", "Charlie"])
        }
    }

    @Test func searchTrimsAndMatchesTitleOrCardTitle() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try #expect(ids(store.prayers(segment: .all, search: "  fixture card 2 ", isSessionUnlocked: true)) == [7])
            try #expect(ids(store.prayers(segment: .all, search: "ENTRY 3", isSessionUnlocked: true)) == [3])
        }
    }

    @Test func tagFilterIsOrAndComposes() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            let a = try store.create(PrayerDraft(prayerCardId: 1, title: "Aaa", tagIds: [1]), now: now)
            let b = try store.create(PrayerDraft(prayerCardId: 1, title: "Bbb", tagIds: [2]), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: 1, title: "Ccc"), now: now)
            try #expect(ids(store.prayers(segment: .all, tagIds: [1, 2], isSessionUnlocked: true)) == [a, b])
            try #expect(ids(store.prayers(segment: .all, search: "Aaa", tagIds: [1, 2], isSessionUnlocked: true)) == [a])
            try #expect(store.prayers(segment: .answered, tagIds: [1, 2], isSessionUnlocked: true).isEmpty)
        }
    }

    @Test func isImportedSurvivesSaveEditQuery() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            let id = try store.create(PrayerDraft(prayerCardId: 1, title: "Imp", isImported: true), now: now)
            try store.update(id: id, with: PrayerDraft(prayerCardId: 1, title: "Imp2"), now: now)
            try #expect(item(store, id).record.isImported == true)
        }
    }

    @Test func emptyStoreReturnsEmpty() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            for segment in PrayerSegment.allCases {
                try #expect(store.prayers(segment: segment, isSessionUnlocked: true).isEmpty)
            }
            try #expect(store.prayers(cardId: 1).isEmpty)
        }
    }

    @Test func blankSearchAndEmptyTagsApplyNoFilter() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try #expect(store.prayers(segment: .all, search: "   ", isSessionUnlocked: true).count == 7)
            try #expect(store.prayers(segment: .all, tagIds: [], isSessionUnlocked: true).count == 7)
        }
    }

    @Test func noMatchReturnsEmpty() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try #expect(store.prayers(segment: .all, search: "zzz", isSessionUnlocked: true).isEmpty)
            try #expect(store.prayers(segment: .all, tagIds: [999], isSessionUnlocked: true).isEmpty)
        }
    }

    @Test func createValidates() throws {
        try withCopy("fresh-install.db") { database in
            let store = PrayerStore(database: database)
            #expect(throws: PrayerStoreError.emptyTitle) {
                try store.create(PrayerDraft(prayerCardId: 1, title: "   "), now: now)
            }
            #expect(throws: PrayerStoreError.cardNotFound(999)) {
                try store.create(PrayerDraft(prayerCardId: 999, title: "X"), now: now)
            }
            try #expect(count(database, "PrayerRequest") == 0)
        }
    }

    @Test func missingIdThrowsNotFound() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            #expect(throws: PrayerStoreError.prayerNotFound(999)) {
                try store.update(id: 999, with: PrayerDraft(prayerCardId: 1, title: "X"), now: now)
            }
            #expect(throws: PrayerStoreError.prayerNotFound(999)) { try store.delete(id: 999) }
            try #expect(count(database, "PrayerRequest") == 7)
        }
    }

    @Test func deleteRemovesPrayerInteractionsAndTags() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try store.update(id: 5, with: PrayerDraft(prayerCardId: 2, title: "T", tagIds: [1]), now: now)
            try store.delete(id: 5)
            try #expect(count(database, "PrayerRequest", where: "Id = 5") == 0)
            try #expect(count(database, "PrayerInteraction", where: "PrayerId = 5") == 0)
            try #expect(count(database, "PrayerCardTag", where: "PrayerRequestId = 5") == 0)
            try #expect(count(database, "PrayerInteraction") == 2)
            try #expect(count(database, "PrayerCardTag") == 2)
            try #expect(count(database, "PrayerRequest") == 6)
        }
    }

    @Test func writeFailureLeavesNoRow() throws {
        try withCopy("fresh-install.db") { database in
            try database.dbQueue.write {
                try $0.execute(sql: """
                    CREATE TEMP TRIGGER force_fail BEFORE INSERT ON PrayerCardTag \
                    BEGIN SELECT RAISE(ABORT, 'forced'); END
                    """)
            }
            let store = PrayerStore(database: database)
            #expect(throws: (any Error).self) {
                try store.create(PrayerDraft(prayerCardId: 1, title: "X", tagIds: [1]), now: now)
            }
            try #expect(count(database, "PrayerRequest") == 0)
        }
    }

    @Test func prayersByCard() throws {
        try withCopy("long-upgraded.db") { database in
            let store = PrayerStore(database: database)
            try #expect(ids(store.prayers(cardId: 2)) == [5, 6])
        }
    }
}
