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
        .appendingPathComponent("CollectionEditorModelTests-\(UUID().uuidString).db")
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

@MainActor
@Suite struct CollectionEditorModelTests {
    @Test func canSaveNeedsTrimmedNameUpToFifty() async throws {
        try await withDatabase { database in
            let model = CollectionEditorModel(database: database, collection: nil)
            for name in ["", "   ", String(repeating: "a", count: 51)] {
                model.name = name
                #expect(!model.canSave)
            }
            model.name = " Ministry "
            #expect(model.canSave)
        }
    }

    @Test func isDirtyIgnoresModeWhileProtectionOff() async throws {
        try await withDatabase { database in
            let box = try CollectionStore(database: database).create(name: "Dirty Test", now: .now)
            let model = CollectionEditorModel(database: database, collection: box)
            #expect(!model.isDirty)
            model.protectionMode = .hidden
            #expect(!model.isDirty)
            model.protectAllCards = true
            #expect(model.isDirty)
        }
    }

    @Test func newEditorDefaults() async throws {
        try await withDatabase { database in
            let model = CollectionEditorModel(database: database, collection: nil)
            #expect(model.isNew)
            #expect(model.name.isEmpty)
            #expect(!model.protectAllCards)
            #expect(model.protectionMode == .lockedVisible)
        }
    }

    @Test func saveNewCreatesCollection() async throws {
        try await withDatabase { database in
            let model = CollectionEditorModel(database: database, collection: nil)
            model.name = " Ministry "
            #expect(model.save())
            let saved = try CollectionStore(database: database).collections().first { $0.name == "Ministry" }
            #expect(saved != nil)
            #expect(saved?.protectAllCards == false)
        }
    }

    @Test func saveDuplicateKeepsValuesAndSetsAlert() async throws {
        try await withDatabase { database in
            _ = try CollectionStore(database: database).create(name: "Family", now: .now)
            let model = CollectionEditorModel(database: database, collection: nil)
            model.name = "Family"
            #expect(!model.save())
            #expect(model.alert == .duplicateName("Family"))
            #expect(model.name == "Family")
        }
    }

    @Test func saveEditPersistsNameAndProtection() async throws {
        try await withDatabase { database in
            let store = CollectionStore(database: database)
            let box = try store.create(name: "Before", now: .now)
            let model = CollectionEditorModel(database: database, collection: box)
            model.name = "After"
            model.protectAllCards = true
            model.protectionMode = .hidden
            #expect(model.save())
            let saved = try store.collections().first { $0.id == box.id }
            #expect(saved?.name == "After")
            #expect(saved?.protectAllCards == true)
            #expect(saved?.cardProtectionMode == .hidden)
        }
    }

    @Test func deleteUnassignRemovesCollection() async throws {
        try await withDatabase { database in
            let store = CollectionStore(database: database)
            let box = try store.create(name: "Doomed", now: .now)
            let model = CollectionEditorModel(database: database, collection: box)
            #expect(model.delete(option: .unassignCards))
            #expect(try store.collections().allSatisfy { $0.id != box.id })
        }
    }
}
