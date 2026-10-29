import Foundation
import GRDB
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private func withFreshCopy<T>(_ body: (PrayerDatabase) throws -> T) throws -> T {
    let source = repoRoot().appendingPathComponent("contracts/db/fresh-install.db")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_colors_\(UUID().uuidString).db").path
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

private func allColors(_ database: PrayerDatabase) throws -> [UserColorRecord] {
    try database.dbQueue.read { try UserColorRecord.order(Column("Id")).fetchAll($0) }
}

private func allTags(_ database: PrayerDatabase) throws -> [PrayerTagRecord] {
    try database.dbQueue.read { try PrayerTagRecord.order(Column("Id")).fetchAll($0) }
}

@Suite struct UserColorStoreTests {
    @Test func saveUppercasesAndIsNotDefault() throws {
        try withFreshCopy { database in
            let saved = try UserColorStore(database: database).saveColor(hex: "#aabbcc", now: now)
            #expect(saved.hexValue == "#AABBCC")
            #expect(saved.isDefault == false)
            #expect(saved.createdAtTicks == now.ticks)
            #expect(try allColors(database).last == saved)
        }
    }

    @Test func saveExistingHexReturnsExistingRow() throws {
        try withFreshCopy { database in
            let existing = try allColors(database)[0]
            let saved = try UserColorStore(database: database).saveColor(hex: existing.hexValue.lowercased(), now: now)
            #expect(saved == existing)
            #expect(try allColors(database).count == 8)
        }
    }

    @Test func customColorsEmptyOnFreshInstall() throws {
        try withFreshCopy { database in
            let custom = try UserColorStore(database: database).customColors()
            #expect(custom.isEmpty)
        }
    }

    @Test func deleteReassignsTagsThenRemoves() throws {
        try withFreshCopy { database in
            let store = UserColorStore(database: database)
            let custom = try store.saveColor(hex: "#AABBCC", now: now)
            let replacement = try allColors(database)[0]
            try database.dbQueue.write { db in
                try db.execute(sql: """
                    INSERT INTO PrayerTag (Name, Color, IsSystem, CreatedAt, UpdatedAt)
                    VALUES ('Mine', '#aabbcc', 0, 1, 1)
                    """)
            }
            try store.deleteColor(id: custom.id, replacementHex: replacement.hexValue.lowercased(), now: now)
            let mine = try #require(try allTags(database).first { $0.name == "Mine" })
            #expect(mine.color == replacement.hexValue)
            #expect(mine.updatedAtTicks == now.ticks)
            #expect(try allColors(database).contains { $0.id == custom.id } == false)
        }
    }

    @Test func deleteUnusedColorRemovesOnlyColor() throws {
        try withFreshCopy { database in
            let store = UserColorStore(database: database)
            let custom = try store.saveColor(hex: "#AABBCC", now: now)
            let tagsBefore = try allTags(database)
            try store.deleteColor(id: custom.id, replacementHex: "#B84040", now: now)
            #expect(try allTags(database) == tagsBefore)
            #expect(try allColors(database).count == 8)
        }
    }

    @Test func deleteDefaultThrows() throws {
        try withFreshCopy { database in
            let before = try allColors(database)
            #expect(throws: UserColorError.defaultColor) {
                try UserColorStore(database: database).deleteColor(id: before[0].id, replacementHex: "#B35A20", now: now)
            }
            #expect(try allColors(database) == before)
        }
    }

    @Test func deleteMissingIdThrows() throws {
        try withFreshCopy { database in
            let before = try allColors(database)
            #expect(throws: UserColorError.notFound) {
                try UserColorStore(database: database).deleteColor(id: 999, replacementHex: "#B84040", now: now)
            }
            #expect(try allColors(database) == before)
        }
    }

    @Test func replacementSameAsDeletedThrows() throws {
        try withFreshCopy { database in
            let store = UserColorStore(database: database)
            let custom = try store.saveColor(hex: "#AABBCC", now: now)
            let before = try allColors(database)
            #expect(throws: UserColorError.invalidReplacement) {
                try store.deleteColor(id: custom.id, replacementHex: "#aabbcc", now: now)
            }
            #expect(try allColors(database) == before)
        }
    }

    @Test func replacementMissingThrows() throws {
        try withFreshCopy { database in
            let store = UserColorStore(database: database)
            let custom = try store.saveColor(hex: "#AABBCC", now: now)
            let before = try allColors(database)
            #expect(throws: UserColorError.invalidReplacement) {
                try store.deleteColor(id: custom.id, replacementHex: "#123456", now: now)
            }
            #expect(try allColors(database) == before)
        }
    }

    @Test func seedDefaultsOnlyWhenEmpty() throws {
        try withFreshCopy { database in
            let store = UserColorStore(database: database)
            let untouched = try allColors(database)
            try store.seedDefaults(now: now)
            #expect(try allColors(database) == untouched)

            try database.dbQueue.write { try $0.execute(sql: "DELETE FROM UserColor") }
            try store.seedDefaults(now: now)
            try store.seedDefaults(now: now)
            let seeded = try allColors(database)
            #expect(seeded.map(\.hexValue) == UserColorStore.defaultPaletteHexValues)
            #expect(seeded.count == 8)
            #expect(seeded.allSatisfy { $0.isDefault })
        }
    }
}
