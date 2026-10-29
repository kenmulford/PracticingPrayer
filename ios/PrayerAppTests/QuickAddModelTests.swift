import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(_ body: (QuickAddModel) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("QuickAddModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    try await body(QuickAddModel(database: database))
}

@MainActor
@Suite struct QuickAddModelTests {
    @Test func canSaveNeedsTitleAndTarget() async throws {
        try await withModel { model in
            #expect(!model.canSave)
            model.prayers[0].title = "Healing"
            #expect(!model.canSave)
            model.cardTitle = "Family"
            #expect(model.canSave)
            model.addTo = .existingCard
            #expect(!model.canSave)
            model.selectCard(id: 1, title: "Card")
            #expect(model.canSave)
            model.prayers[0].title = "  "
            #expect(!model.canSave)
        }
    }

    @Test func switchingModeKeepsPrayersAndClearsOtherTarget() async throws {
        try await withModel { model in
            model.prayers[0].title = "Healing"
            model.cardTitle = "Family"
            model.collectionId = 2
            model.addTo = .existingCard
            #expect(model.cardTitle.isEmpty)
            #expect(model.collectionId == 0)
            #expect(model.prayers[0].title == "Healing")
            model.selectCard(id: 1, title: "Card")
            model.addTo = .newCard
            #expect(model.selectedCardId == nil)
            #expect(model.selectedCardTitle == nil)
            #expect(model.prayers[0].title == "Healing")
        }
    }

    @Test func removePrayerKeepsLastEntry() async throws {
        try await withModel { model in
            let only = model.prayers[0].id
            model.removePrayer(id: only)
            #expect(model.prayers.count == 1)
            model.addPrayer()
            #expect(model.prayers.count == 2)
            model.removePrayer(id: only)
            #expect(model.prayers.count == 1)
            #expect(model.prayers[0].id != only)
        }
    }

    @Test func isDirtyTracksEntries() async throws {
        try await withModel { model in
            #expect(!model.isDirty)
            model.prayers[0].title = "Healing"
            #expect(model.isDirty)
            model.prayers[0].title = ""
            #expect(!model.isDirty)
            model.addPrayer()
            #expect(model.isDirty)
        }
    }

    @Test func collectionsExcludeSystem() async throws {
        try await withModel { model in
            model.loadCollections()
            #expect(!model.collections.contains { $0.isSystem })
        }
    }

    @Test func failedSaveKeepsEntries() async throws {
        try await withModel { model in
            model.addTo = .existingCard
            model.prayers[0].title = "Healing"
            model.selectCard(id: 999, title: "X")
            let saved = await model.save()
            #expect(!saved)
            #expect(model.errorMessage != nil)
            #expect(model.prayers[0].title == "Healing")
            #expect(!model.isSaving)
        }
    }

    @Test func saveReturnsTrue() async throws {
        try await withModel { model in
            model.prayers[0].title = "Healing"
            model.cardTitle = "Family"
            let saved = await model.save()
            #expect(saved)
            #expect(model.errorMessage == nil)
        }
    }
}
