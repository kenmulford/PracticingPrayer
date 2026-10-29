import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withDatabase(_ body: (PrayerDatabase) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("TagEditorModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    try await body(database)
}

private func fixtureTags(_ database: PrayerDatabase) throws -> (urgent: PrayerTagRecord, family: PrayerTagRecord) {
    let tags = try TagStore(database: database).tags().map(\.tag)
    return (try #require(tags.first { $0.name == "Urgent" }), try #require(tags.first { $0.name == "Family" }))
}

@MainActor
@Suite struct TagEditorModelTests {
    @Test func defaultColorNames() {
        let names = UserColorStore.defaultPaletteHexValues.map(TagEditorModel.colorName(forHex:))
        #expect(names == ["Red", "Orange", "Brown", "Teal", "Blue", "Purple", "Pink", "Gray"])
    }

    @Test func customColorNames() {
        #expect(TagEditorModel.colorName(forHex: "#FF0000") == "Red")
        #expect(TagEditorModel.colorName(forHex: "#112233") == "Dark Blue")
    }

    @Test func canSaveNeedsTrimmedName() async throws {
        try await withDatabase { database in
            let model = TagEditorModel(database: database, tag: nil)
            model.name = "   "
            #expect(!model.canSave)
            model.name = " Prayer "
            #expect(model.canSave)
        }
    }

    @Test func offPaletteTagKeepsColor() async throws {
        try await withDatabase { database in
            let (urgent, _) = try fixtureTags(database)
            let model = TagEditorModel(database: database, tag: urgent)
            #expect(model.selectedHex == nil)
            model.name = "Urgent Now"
            #expect(model.save())
            let saved = try TagStore(database: database).tags().first { $0.tag.id == urgent.id }?.tag
            #expect(saved?.name == "Urgent Now")
            #expect(saved?.color == "#FF0000")
        }
    }

    @Test func duplicateNameSetsAlert() async throws {
        try await withDatabase { database in
            _ = try fixtureTags(database)
            let model = TagEditorModel(database: database, tag: nil)
            model.name = "Family"
            #expect(!model.save())
            #expect(model.alert == .duplicateName("Family"))
        }
    }

    @Test func existingHexSelectsWithoutAdding() async throws {
        try await withDatabase { database in
            _ = try fixtureTags(database)
            let model = TagEditorModel(database: database, tag: nil)
            model.addCustomColor(hex: "#2e5a9a")
            #expect(model.swatches.count == 8)
            #expect(model.selectedHex == "#2E5A9A")
        }
    }

    @Test func deleteUsedColorMovesTags() async throws {
        try await withDatabase { database in
            _ = try fixtureTags(database)
            let model = TagEditorModel(database: database, tag: nil)
            model.addCustomColor(hex: "#123456")
            let custom = try #require(model.swatches.first { $0.hex == "#123456" })
            model.name = "Moves"
            #expect(model.save())
            let created = try #require(
                TagStore(database: database).tags().first { $0.tag.name == "Moves" }?.tag)
            #expect(model.tagsUsing(custom) == 1)
            #expect(model.deleteColor(custom, replacementHex: "#B84040"))
            let moved = try TagStore(database: database).tags().first { $0.tag.id == created.id }?.tag
            #expect(moved?.color == "#B84040")
            #expect(!model.swatches.contains { $0.hex == "#123456" })
            #expect(model.selectedHex == "#B84040")
        }
    }

    @Test func systemTagNameNotEditable() async throws {
        try await withDatabase { database in
            let system = PrayerTagRecord(
                id: 9999, name: "Recently Notified", color: "#505050", isSystem: true,
                createdAtTicks: 1, updatedAtTicks: 1)
            let model = TagEditorModel(database: database, tag: system)
            #expect(!model.isNameEditable)
            #expect(!model.canDelete)
        }
    }
}
