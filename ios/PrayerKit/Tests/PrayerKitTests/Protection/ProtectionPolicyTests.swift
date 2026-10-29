import Foundation
import Testing
import GRDB
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func card(id: Int64 = 1, protectionMode: CardProtectionMode?, boxId: Int64? = nil) -> PrayerCardRecord {
    PrayerCardRecord(
        id: id, title: "Card \(id)", canNotify: false, prayerFrequency: .weekly,
        isAnswered: false, isFavorite: false, isSystem: nil, isImported: nil,
        protectionMode: protectionMode, boxId: boxId, preArchiveBoxId: nil,
        systemKey: nil, createdAtTicks: 0, updatedAtTicks: 0)
}

private func box(id: Int64 = 10, protectAll: Bool, mode: CardProtectionMode) -> CardBoxRecord {
    CardBoxRecord(
        id: id, name: "Box \(id)", isSystem: false, systemKey: nil, sortOrder: 1,
        protectAllCards: protectAll, cardProtectionMode: mode, createdAtTicks: 0, updatedAtTicks: 0)
}

private func withDatabase<T>(_ file: String, sql: [String] = [], _ body: (PrayerDatabase) throws -> T) throws -> T {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_protection_\(UUID().uuidString).db").path
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
    return try body(database)
}

private func looseIds(_ list: CardList) -> [Int64] {
    list.sections.first { $0.boxId == 0 }?.cards.map(\.card.id) ?? []
}

private let cardsProtected = [
    "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 2",
    "UPDATE PrayerCard SET ProtectionMode = 2 WHERE Id = 3",
]

@Test(arguments: CardProtectionMode.allCases, [true, false])
func modeCrossedWithSessionState(mode: CardProtectionMode, unlocked: Bool) {
    let c = card(protectionMode: mode)
    #expect(ProtectionPolicy.isAccessBlocked(c, box: nil, isSessionUnlocked: unlocked) == (mode != .unprotected && !unlocked))
    #expect(ProtectionPolicy.isHiddenWhileLocked(c, box: nil, isSessionUnlocked: unlocked) == (mode == .hidden && !unlocked))
}

@Test func nilOwnModeWithNoBoxIsUnprotected() {
    #expect(ProtectionPolicy.effectiveMode(of: card(protectionMode: nil), box: nil) == .unprotected)
}

@Test func ownModeBeatsBox() {
    let result = ProtectionPolicy.effectiveMode(
        of: card(protectionMode: .lockedVisible, boxId: 10), box: box(protectAll: true, mode: .hidden))
    #expect(result == .lockedVisible)
}

@Test(arguments: [true, false])
func unprotectedCardTakesBoxModeOnlyWithProtectAll(protectAll: Bool) {
    let result = ProtectionPolicy.effectiveMode(
        of: card(protectionMode: nil, boxId: 10), box: box(protectAll: protectAll, mode: .hidden))
    #expect(result == (protectAll ? .hidden : .unprotected))
}

@Test(arguments: [true, false])
func blockedCardIdsEmptyInput(unlocked: Bool) {
    #expect(ProtectionPolicy.blockedCardIds(cards: [], boxes: [], isSessionUnlocked: unlocked).isEmpty)
}

@Test func rawValuesMatch172() {
    #expect(CardProtectionMode(rawValue: 0) == .unprotected)
    #expect(CardProtectionMode(rawValue: 1) == .lockedVisible)
    #expect(CardProtectionMode(rawValue: 2) == .hidden)
}

@Test func legacyRowsDecodeToSameEffectiveMode() throws {
    try withDatabase("long-upgraded.db", sql: ["UPDATE PrayerCard SET ProtectionMode = 2 WHERE Id = 3"]) { database in
        let cards = try database.dbQueue.read { try PrayerCardRecord.fetchAll($0) }
        let one = try #require(cards.first { $0.id == 1 })
        let three = try #require(cards.first { $0.id == 3 })
        #expect(ProtectionPolicy.effectiveMode(of: one, box: nil) == .unprotected)
        #expect(ProtectionPolicy.effectiveMode(of: three, box: nil) == .hidden)
    }
}

@Test func lockedListMasksLockedVisibleAndDropsHidden() throws {
    try withDatabase("long-upgraded.db", sql: cardsProtected) { database in
        let locked = try database.cardList(isSessionUnlocked: false)
        #expect(looseIds(locked) == [1, 2])
        let items = locked.sections.first { $0.boxId == 0 }?.cards ?? []
        let masked = try #require(items.first { $0.card.id == 2 })
        #expect(masked.card.title == "Protected")
        #expect(masked.activePrayerCount == nil)
        #expect(masked.isMasked)
        let open = try #require(items.first { $0.card.id == 1 })
        #expect(open.activePrayerCount == 4)
        #expect(!open.isMasked)
    }
}

@Test func unlockedListIsUnfiltered() throws {
    try withDatabase("long-upgraded.db", sql: cardsProtected) { database in
        let unlocked = try database.cardList(isSessionUnlocked: true)
        #expect(looseIds(unlocked) == [3, 1, 2])
        let items = unlocked.sections.first { $0.boxId == 0 }?.cards ?? []
        #expect(items.allSatisfy { !$0.isMasked })
        #expect(items.map(\.card.title).allSatisfy { $0 != "Protected" })
    }
}

@Test func lockedSearchAndTagFilterExcludeProtected() throws {
    try withDatabase("long-upgraded.db", sql: cardsProtected) { database in
        let filter = CardListFilter(searchText: "fixture")
        let locked = try database.cardList(filter: filter, isSessionUnlocked: false)
        #expect(locked.totalMatchCount == 0)
        let unlocked = try database.cardList(filter: filter, isSessionUnlocked: true)
        #expect(looseIds(unlocked) == [3, 2])
    }
}

@Test func boxCascadeHidesCardWhileLocked() throws {
    let sql = [
        "INSERT INTO CardBox (Id, Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES (10, 'Vault', 0, 1, 1, 2, 638712864000000000, 638712864000000000)",
        "UPDATE PrayerCard SET BoxId = 10 WHERE Id = 2",
    ]
    try withDatabase("long-upgraded.db", sql: sql) { database in
        let locked = try database.cardList(isSessionUnlocked: false)
        #expect(locked.sections.first { $0.boxId == 10 }?.cards.isEmpty == true)
        let unlocked = try database.cardList(isSessionUnlocked: true)
        #expect(unlocked.sections.first { $0.boxId == 10 }?.cards.map(\.card.id) == [2])
    }
}

@Test func lockedPrayersExcludeProtectedCards() throws {
    try withDatabase("long-upgraded.db", sql: ["UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 1"]) { database in
        let store = PrayerStore(database: database)
        let locked = try store.prayers(segment: .all, isSessionUnlocked: false)
        #expect(Set(locked.map(\.record.id)) == [5, 6, 7])
        #expect(try store.prayers(segment: .all, isSessionUnlocked: true).count == 7)
    }
}

@Test func emptyInputBothStates() throws {
    try withDatabase("fresh-install.db", sql: ["DELETE FROM PrayerCard"]) { database in
        let locked = try database.cardList(isSessionUnlocked: false)
        let unlocked = try database.cardList(isSessionUnlocked: true)
        #expect(locked == unlocked)
        #expect(locked.totalMatchCount == 0)
        let store = PrayerStore(database: database)
        #expect(try store.prayers(segment: .all, isSessionUnlocked: false).isEmpty)
        #expect(try store.prayers(segment: .all, isSessionUnlocked: true).isEmpty)
    }
}
