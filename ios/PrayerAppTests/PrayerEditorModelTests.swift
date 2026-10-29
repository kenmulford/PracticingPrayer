import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private let testPreferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerEditorModelTests") ?? .standard)

@MainActor
private func withEditor(_ body: (PrayerDatabase) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("PrayerEditorModelTests-\(UUID().uuidString).db")
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
@Suite struct PrayerEditorModelTests {
    @Test func newWithoutCardCannotSave() async throws {
        try await withEditor { database in
            let model = PrayerEditorModel(prayerId: nil, cardId: nil, cardTitle: nil,
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            model.title = "newWithoutCardCannotSave"
            #expect(model.isNew)
            #expect(!model.canSave)
        }
    }

    @Test func preselectedCardIsNotDirty() async throws {
        try await withEditor { database in
            let model = PrayerEditorModel(prayerId: nil, cardId: 2, cardTitle: "Card",
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            #expect(!model.isDirty)
            model.title = "x"
            #expect(model.isDirty)
            #expect(model.canSave)
        }
    }

    @Test func createAnsweredStoresDateOnly() async throws {
        try await withEditor { database in
            let tag = try TagStore(database: database).createTag(name: "createAnswered", color: "#336699", now: .now)
            let model = PrayerEditorModel(prayerId: nil, cardId: 2, cardTitle: "Card",
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            let yesterday = Calendar.current.date(byAdding: .hour, value: 15, to: Calendar.current.date(
                byAdding: .day, value: -1, to: Calendar.current.startOfDay(for: .now))!)!
            model.title = "createAnsweredStoresDateOnly"
            model.isAnswered = true
            model.answeredOn = yesterday
            model.tagIds = [tag.id]
            #expect(await model.save())
            let item = try PrayerStore(database: database).prayers(cardId: 2)
                .first { $0.record.title == "createAnsweredStoresDateOnly" }
            #expect(item?.record.isAnswered == true)
            #expect(item?.record.answeredAtTicks == Calendar.current.startOfDay(for: yesterday).ticks)
            #expect(item?.tagIds == [tag.id])
        }
    }

    @Test func saveAndAddAnotherKeepsCard() async throws {
        try await withEditor { database in
            let model = PrayerEditorModel(prayerId: nil, cardId: 2, cardTitle: "Card",
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            model.title = "saveAndAddAnotherKeepsCard"
            model.details = "notes"
            model.isAnswered = true
            #expect(await model.saveAndAddAnother())
            #expect(model.cardId == 2)
            #expect(model.title == "")
            #expect(model.details == "")
            #expect(model.tagIds.isEmpty)
            #expect(!model.isAnswered)
            let created = try PrayerStore(database: database).prayers(cardId: 2)
                .filter { $0.record.title == "saveAndAddAnotherKeepsCard" }
            #expect(created.count == 1)
        }
    }

    @Test func editLoadsThenMarksActive() async throws {
        try await withEditor { database in
            let tag = try TagStore(database: database).createTag(name: "editLoads", color: "#336699", now: .now)
            let store = PrayerStore(database: database)
            let id = try store.create(
                PrayerDraft(prayerCardId: 2, title: "editLoadsThenMarksActive", tagIds: [tag.id]), now: .now)
            try store.markAnswered(id: id, now: .now)
            let model = PrayerEditorModel(prayerId: id, cardId: nil, cardTitle: nil,
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            #expect(model.title == "editLoadsThenMarksActive")
            #expect(model.tagIds == [tag.id])
            #expect(model.isAnswered)
            #expect(!model.isDirty)
            model.isAnswered = false
            #expect(await model.save())
            let record = try store.prayers(cardId: 2).first { $0.record.id == id }?.record
            #expect(record?.isAnswered == false)
            #expect(record?.answeredAtTicks == nil)
        }
    }

    @Test func failedSaveKeepsEntries() async throws {
        try await withEditor { database in
            let model = PrayerEditorModel(prayerId: nil, cardId: 999_999, cardTitle: "Gone",
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            model.title = "failedSaveKeepsEntries"
            #expect(!(await model.save()))
            #expect(model.alert != nil)
            #expect(model.title == "failedSaveKeepsEntries")
        }
    }

    @Test func deleteRemovesPrayer() async throws {
        try await withEditor { database in
            let store = PrayerStore(database: database)
            let id = try store.create(PrayerDraft(prayerCardId: 2, title: "deleteRemovesPrayer"), now: .now)
            let model = PrayerEditorModel(prayerId: id, cardId: nil, cardTitle: nil,
                database: database, preferences: testPreferences, reminders: RecordingReconciler())
            model.load()
            #expect(model.delete())
            #expect(try store.prayers(cardId: 2).first { $0.record.id == id } == nil)
        }
    }
}
