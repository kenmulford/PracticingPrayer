import Foundation
import Testing
import GRDB
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func list(
    of file: String,
    sql: [String] = [],
    filter: CardListFilter = CardListFilter()
) throws -> CardList {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_cardlist_\(UUID().uuidString).db").path
    try FileManager.default.copyItem(atPath: source.path, toPath: path)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    try database.dbQueue.write { db in
        for statement in sql { try db.execute(sql: statement) }
    }
    return try database.cardList(filter: filter)
}

private func boxIds(_ list: CardList) -> [Int64] { list.sections.map(\.boxId) }

private func ids(_ list: CardList, box: Int64) -> [Int64] {
    list.sections.first { $0.boxId == box }?.cards.map(\.card.id) ?? []
}

private func newCard(_ id: Int, _ title: String, box: Int, favorite: Int = 0) -> String {
    "INSERT INTO PrayerCard (Id, Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, BoxId) "
        + "VALUES (\(id), '\(title)', 0, 0, 0, \(favorite), 638712864000000000, 638712864000000000, \(box))"
}

private func newBox(_ id: Int, _ name: String) -> String {
    "INSERT INTO CardBox (Id, Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
        + "VALUES (\(id), '\(name)', 0, 1, 0, 0, 638712864000000000, 638712864000000000)"
}

private let tagInserts = [
    "INSERT INTO PrayerCardTag (Id, PrayerCardId, PrayerTagId, CreatedAt, PrayerRequestId) VALUES (10, 0, 4, 638712864000000000, 6)",
    "INSERT INTO PrayerCardTag (Id, PrayerCardId, PrayerTagId, CreatedAt, PrayerRequestId) VALUES (11, 0, 5, 638712864000000000, 7)",
]

@Test func defaultOrderOnLongUpgraded() throws {
    let result = try list(of: "long-upgraded.db")
    #expect(boxIds(result) == [0, 1, 2])
    #expect(ids(result, box: 0) == [3, 1, 2])
    #expect(result.sections[0].cards.map(\.activePrayerCount) == [1, 4, 1])
    #expect(ids(result, box: 1) == [4])
    #expect(ids(result, box: 2).isEmpty)
    #expect(result.totalMatchCount == 4)
}

@Test func userCollectionsSortByNameThenId() throws {
    let result = try list(of: "long-upgraded.db", sql: [newBox(10, "beta"), newBox(11, "Alpha"), newBox(12, "alpha")])
    #expect(boxIds(result) == [0, 11, 12, 10, 1, 2])
}

@Test func cardsSortFavoriteThenTitleThenId() throws {
    let result = try list(of: "long-upgraded.db", sql: [newCard(20, "same", box: 0), newCard(21, "Same", box: 0)])
    #expect(ids(result, box: 0) == [3, 1, 2, 20, 21])
}

@Test func emptyDatabaseKeepsUserAndArchivedSections() throws {
    let result = try list(of: "fresh-install.db", sql: ["DELETE FROM PrayerCard", newBox(10, "Mine")])
    #expect(boxIds(result) == [10, 2])
    #expect(result.sections.allSatisfy { $0.cards.isEmpty })
    #expect(result.totalMatchCount == 0)
}

@Test func searchIsTrimmedCaseInsensitiveSubstring() throws {
    let result = try list(of: "long-upgraded.db", filter: CardListFilter(searchText: "  fixture CARD "))
    #expect(boxIds(result) == [0, 1, 2])
    #expect(ids(result, box: 0) == [3, 2])
    #expect(result.totalMatchCount == 2)
}

@Test func tagFilterUsesUnansweredPrayerTagsOnly() throws {
    let both = try list(of: "long-upgraded.db", sql: tagInserts, filter: CardListFilter(tagIds: [4, 5]))
    #expect(ids(both, box: 0) == [3])
    #expect(both.totalMatchCount == 1)
    let answeredOnly = try list(of: "long-upgraded.db", sql: tagInserts, filter: CardListFilter(tagIds: [4]))
    #expect(answeredOnly.totalMatchCount == 0)
}

@Test func searchAndTagFilterBothMustMatch() throws {
    let result = try list(of: "long-upgraded.db", sql: tagInserts, filter: CardListFilter(searchText: "General", tagIds: [5]))
    #expect(result.totalMatchCount == 0)
}

@Test func noMatchReturnsEverySectionEmpty() throws {
    let result = try list(of: "long-upgraded.db", filter: CardListFilter(searchText: "zzz"))
    #expect(boxIds(result) == [0, 1, 2])
    #expect(result.sections.allSatisfy { $0.cards.isEmpty })
    #expect(result.totalMatchCount == 0)
}

@Test func whitespaceSearchAndEmptyTagsAreNoFilter() throws {
    let filtered = try list(of: "long-upgraded.db", filter: CardListFilter(searchText: "  \n", tagIds: []))
    #expect(filtered == (try list(of: "long-upgraded.db")))
}

@Test func orphanBoxIdCardIsOmitted() throws {
    let result = try list(of: "long-upgraded.db", sql: ["UPDATE PrayerCard SET BoxId = 99 WHERE Id = 2"])
    #expect(!result.sections.contains { $0.cards.contains { $0.card.id == 2 } })
    #expect(result.totalMatchCount == 3)
}

@Test func missingArchivedBoxOmitsArchivedSection() throws {
    let result = try list(of: "long-upgraded.db", sql: ["DELETE FROM CardBox WHERE Id = 2"])
    #expect(boxIds(result) == [0, 1])
}

@Test func readFailureThrows() throws {
    #expect(throws: (any Error).self) {
        try list(of: "long-upgraded.db", sql: ["DROP TABLE PrayerRequest"])
    }
}
