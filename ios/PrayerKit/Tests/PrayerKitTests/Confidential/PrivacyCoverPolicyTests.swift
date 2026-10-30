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
        .appendingPathComponent("pk_privacycover_\(UUID().uuidString).db").path
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

@Test func activeSceneIsNeverCovered() {
    #expect(!PrivacyCoverPolicy.isCovered(isSceneActive: true, hasProtectedCards: true))
}

@Test func inactiveSceneIsCoveredOnlyWithProtectedCards() {
    #expect(PrivacyCoverPolicy.isCovered(isSceneActive: false, hasProtectedCards: true))
    #expect(!PrivacyCoverPolicy.isCovered(isSceneActive: false, hasProtectedCards: false))
}

@Test func emptyDatabaseHasNoProtectedCards() {
    #expect(!PrivacyCoverPolicy.hasProtectedCards(cards: [], boxes: []))
}

@Test func unprotectedCardsAreNotProtected() {
    #expect(!PrivacyCoverPolicy.hasProtectedCards(cards: [card(protectionMode: .unprotected)], boxes: []))
}

@Test func ownHiddenOrLockedModeIsProtected() {
    #expect(PrivacyCoverPolicy.hasProtectedCards(cards: [card(protectionMode: .hidden)], boxes: []))
    #expect(PrivacyCoverPolicy.hasProtectedCards(cards: [card(protectionMode: .lockedVisible)], boxes: []))
}

@Test func protectAllBoxCascades() {
    let cards = [card(protectionMode: .unprotected, boxId: 10)]
    let boxes = [box(id: 10, protectAll: true, mode: .lockedVisible)]
    #expect(PrivacyCoverPolicy.hasProtectedCards(cards: cards, boxes: boxes))
}

@Test func unreadableDatabaseFailsClosed() throws {
    try withDatabase("long-upgraded.db") { database in
        try database.close()
        #expect(PrivacyCoverPolicy.hasProtectedCards(in: database))
    }
}
