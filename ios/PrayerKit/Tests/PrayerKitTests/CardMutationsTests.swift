import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private func tempCopy(of file: String) throws -> String {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let dest = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_cardmutations_\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: source, to: dest)
    return dest.path
}

private func removeTemp(_ path: String) {
    for suffix in ["", "-journal", "-wal", "-shm"] {
        try? FileManager.default.removeItem(atPath: path + suffix)
    }
}

private func withCopy<T>(
    _ file: String, archivedFolderId: Int = 2, _ body: (PrayerDatabase, CardMutations) throws -> T
) throws -> T {
    let path = try tempCopy(of: file)
    defer { removeTemp(path) }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    let suite = "pk_cardmutations_\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: suite)!
    defer { defaults.removePersistentDomain(forName: suite) }
    let preferences = AppPreferences(defaults: defaults)
    preferences.archivedFolderId = archivedFolderId
    return try body(database, CardMutations(database: database, preferences: preferences))
}

private func card(_ database: PrayerDatabase, _ id: Int64) throws -> PrayerCardRecord {
    try #require(database.dbQueue.read { try PrayerCardRecord.fetchOne($0, key: id) })
}

private func count(_ database: PrayerDatabase, _ table: String, where clause: String = "1") throws -> Int {
    try database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM \(table) WHERE \(clause)") ?? 0 }
}

private func run(_ database: PrayerDatabase, _ sql: String) throws {
    try database.dbQueue.write { try $0.execute(sql: sql) }
}

private func addBox(_ database: PrayerDatabase, _ id: Int64) throws {
    let ticks = Date(timeIntervalSince1970: 1_700_000_000).ticks
    try run(
        database,
        "INSERT INTO CardBox (Id, Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) VALUES (\(id), 'Box\(id)', 0, 1, 0, 0, \(ticks), \(ticks))"
    )
}

@Suite struct CardMutationsTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func createPersistsValuesAndReturnsId() throws {
        try withCopy("fresh-install.db") { database, mutations in
            try addBox(database, 10)
            for (index, mode) in CardProtectionMode.allCases.enumerated() {
                let created = try mutations.create(
                    CardDraft(title: "T\(index)", boxId: 10, protectionMode: mode, isImported: index == 1), now: now)
                #expect(created.id != 0)
                let fetched = try card(database, created.id)
                #expect(fetched == created)
                #expect(fetched.title == "T\(index)")
                #expect(fetched.boxId == 10)
                #expect(fetched.protectionMode == mode)
                #expect(fetched.isImported == (index == 1))
                #expect(fetched.prayerFrequency == .weekly)
                #expect(!fetched.isFavorite)
                #expect(!fetched.isAnswered)
                #expect(fetched.createdAtTicks == now.ticks)
            }
        }
    }

    @Test func editChangesOnlyTitleBoxAndMode() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try addBox(database, 10)
            let before = try card(database, 2)
            let later = now.addingTimeInterval(60)
            try mutations.update(
                id: 2, with: CardDraft(title: "Renamed", boxId: 10, protectionMode: .hidden, isImported: true),
                now: later)
            var expected = before
            expected.title = "Renamed"
            expected.boxId = 10
            expected.protectionMode = .hidden
            expected.updatedAtTicks = later.ticks
            #expect(try card(database, 2) == expected)
        }
    }

    @Test func favoriteAndUnfavoritePersist() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try mutations.setFavorite(id: 2, true, now: now)
            #expect(try card(database, 2).isFavorite)
            try mutations.setFavorite(id: 2, false, now: now)
            #expect(try !card(database, 2).isFavorite)
        }
    }

    @Test func moveSetsBoxIdAndZeroMakesLoose() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try addBox(database, 10)
            try mutations.move(id: 2, toBox: 10, now: now)
            #expect(try card(database, 2).boxId == 10)
            try mutations.move(id: 2, toBox: 0, now: now)
            #expect(try card(database, 2).boxId == 0)
        }
    }

    @Test func archiveStashesBoxAndUnarchiveRestores() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try addBox(database, 10)
            try mutations.move(id: 2, toBox: 10, now: now)
            try mutations.archive(id: 2, now: now)
            var record = try card(database, 2)
            #expect(record.boxId == 2)
            #expect(record.preArchiveBoxId == 10)
            try mutations.unarchive(id: 2, now: now)
            record = try card(database, 2)
            #expect(record.boxId == 10)
            #expect(record.preArchiveBoxId == nil)
        }
    }

    @Test func deleteRemovesCardPrayersInteractionsAndTagRows() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try run(
                database,
                "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt) SELECT 0, Id, 5, CreatedAt FROM PrayerTag LIMIT 1"
            )
            #expect(try count(database, "PrayerCardTag", where: "PrayerRequestId = 5") == 1)
            try mutations.delete(id: 2)
            #expect(try count(database, "PrayerCard", where: "Id = 2") == 0)
            #expect(try count(database, "PrayerRequest", where: "PrayerCardId = 2") == 0)
            #expect(try count(database, "PrayerRequest", where: "Id IN (5, 6)") == 0)
            #expect(try count(database, "PrayerInteraction", where: "PrayerId IN (5, 6)") == 0)
            #expect(try count(database, "PrayerCardTag", where: "PrayerRequestId IN (5, 6)") == 0)
            #expect(try count(database, "PrayerCard", where: "Id IN (1, 3, 4)") == 3)
            #expect(try count(database, "PrayerRequest", where: "Id IN (1, 2, 3, 4, 7)") == 5)
            #expect(try count(database, "PrayerInteraction", where: "PrayerId = 7") == 1)
        }
    }

    @Test func emptyTitleThrowsAndWritesNothing() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            let cards = try count(database, "PrayerCard")
            let before = try card(database, 2)
            for title in ["", "  \n"] {
                #expect(throws: CardMutationError.emptyTitle) {
                    try mutations.create(CardDraft(title: title), now: now)
                }
                #expect(throws: CardMutationError.emptyTitle) {
                    try mutations.update(id: 2, with: CardDraft(title: title), now: now)
                }
            }
            #expect(try count(database, "PrayerCard") == cards)
            #expect(try card(database, 2) == before)
        }
    }

    @Test func unarchiveFallsBackToLoose() throws {
        for stash in ["NULL", "0", "99"] {
            try withCopy("long-upgraded.db") { database, mutations in
                try run(database, "UPDATE PrayerCard SET BoxId = 2, PreArchiveBoxId = \(stash) WHERE Id = 2")
                try mutations.unarchive(id: 2, now: now)
                let record = try card(database, 2)
                #expect(record.boxId == 0)
                #expect(record.preArchiveBoxId == nil)
            }
        }
    }

    @Test func systemCardRejectsArchiveUnarchiveDelete() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            let before = try card(database, 4)
            #expect(throws: CardMutationError.systemCard(4)) { try mutations.archive(id: 4, now: now) }
            #expect(throws: CardMutationError.systemCard(4)) { try mutations.unarchive(id: 4, now: now) }
            #expect(throws: CardMutationError.systemCard(4)) { try mutations.delete(id: 4) }
            #expect(try card(database, 4) == before)
        }
    }

    @Test func moveToMissingBoxThrows() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            let cards = try count(database, "PrayerCard")
            let before = try card(database, 2)
            #expect(throws: CardMutationError.boxNotFound(99)) { try mutations.move(id: 2, toBox: 99, now: now) }
            #expect(throws: CardMutationError.boxNotFound(99)) {
                try mutations.create(CardDraft(title: "X", boxId: 99), now: now)
            }
            #expect(throws: CardMutationError.boxNotFound(99)) {
                try mutations.update(id: 2, with: CardDraft(title: "X", boxId: 99), now: now)
            }
            #expect(try count(database, "PrayerCard") == cards)
            #expect(try card(database, 2) == before)
        }
    }

    @Test func archiveWithUnsetFolderThrows() throws {
        try withCopy("long-upgraded.db", archivedFolderId: 0) { database, mutations in
            let before = try card(database, 2)
            #expect(throws: CardMutationError.archivedFolderUnset) { try mutations.archive(id: 2, now: now) }
            #expect(try card(database, 2) == before)
        }
    }

    @Test func archiveTwiceAndUnarchiveLooseAreNoOps() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            let loose = try card(database, 3)
            try mutations.unarchive(id: 3, now: now)
            #expect(try card(database, 3) == loose)
            try mutations.archive(id: 2, now: now)
            let archived = try card(database, 2)
            try mutations.archive(id: 2, now: now.addingTimeInterval(60))
            #expect(try card(database, 2) == archived)
        }
    }

    @Test func missingCardThrowsNotFound() throws {
        try withCopy("long-upgraded.db") { _, mutations in
            let error = CardMutationError.cardNotFound(999)
            #expect(throws: error) { try mutations.update(id: 999, with: CardDraft(title: "X"), now: now) }
            #expect(throws: error) { try mutations.setFavorite(id: 999, true, now: now) }
            #expect(throws: error) { try mutations.move(id: 999, toBox: 0, now: now) }
            #expect(throws: error) { try mutations.archive(id: 999, now: now) }
            #expect(throws: error) { try mutations.unarchive(id: 999, now: now) }
            #expect(throws: error) { try mutations.delete(id: 999) }
        }
    }

    @Test func failedDeleteRollsBack() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try run(
                database,
                "CREATE TRIGGER pk_fail BEFORE DELETE ON PrayerRequest WHEN OLD.Id = 6 BEGIN SELECT RAISE(ABORT, 'fail'); END"
            )
            #expect(throws: DatabaseError.self) { try mutations.delete(id: 2) }
            #expect(try count(database, "PrayerCard", where: "Id = 2") == 1)
            #expect(try count(database, "PrayerRequest", where: "Id IN (5, 6)") == 2)
            #expect(try count(database, "PrayerInteraction") == 3)
        }
    }

    @Test func longUpgradedArchivedCardRoundTrips() throws {
        try withCopy("long-upgraded.db") { database, mutations in
            try addBox(database, 10)
            try run(database, "UPDATE PrayerCard SET BoxId = 2, PreArchiveBoxId = 10 WHERE Id = 2")
            try mutations.unarchive(id: 2, now: now)
            var record = try card(database, 2)
            #expect(record.boxId == 10)
            #expect(record.preArchiveBoxId == nil)
            try mutations.archive(id: 2, now: now)
            record = try card(database, 2)
            #expect(record.boxId == 2)
            #expect(record.preArchiveBoxId == 10)
        }
    }
}
