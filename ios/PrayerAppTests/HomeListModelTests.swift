import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(
    scope: HomeListScope,
    _ body: (HomeListModel, PrayerDatabase) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("HomeListModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let model = HomeListModel(scope: scope, database: database, overdueDayThreshold: 30)
    model.load()
    try await body(model, database)
}

@MainActor
private func ids(_ model: HomeListModel) -> [Int64] {
    (model.sections ?? []).flatMap(\.items).map(\.record.id)
}

@MainActor
@Suite struct HomeListModelTests {
    @Test func reloadFollowsLiveUnlockState() async throws {
        try await withModel(scope: .unanswered) { _, database in
            let card = try CardMutations(database: database, preferences: AppPreferences(defaults: UserDefaults(suiteName: "HomeListModelTests-\(UUID().uuidString)")!))
                .create(CardDraft(title: "Hidden reloadFollowsLiveUnlockState", protectionMode: .hidden), now: .now)
            let id = try PrayerStore(database: database)
                .create(PrayerDraft(prayerCardId: card.id, title: "reloadFollowsLiveUnlockState"), now: .now)
            var unlocked = true
            let model = HomeListModel(
                scope: .unanswered, database: database, overdueDayThreshold: 30, isSessionUnlocked: { unlocked })
            model.load()
            #expect(ids(model).contains(id))
            unlocked = false
            model.load()
            #expect(!ids(model).contains(id))
        }
    }

    @Test func overdueMatchesStoreOrder() async throws {
        try await withModel(scope: .overdue) { model, database in
            let expected = try database.overduePrayers(now: .now, overdueDayThreshold: 30, isSessionUnlocked: false)
                .map(\.id)
            #expect(ids(model) == expected)
        }
    }

    @Test func unansweredIsEveryActivePrayer() async throws {
        try await withModel(scope: .unanswered) { model, database in
            let expected = try PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: false)
                .map(\.record.id)
            #expect(ids(model) == expected)
            #expect(model.sections?.count == 1)
            #expect(model.sections?.first?.title == nil)
        }
    }

    @Test func activeCardsGroupsByCard() async throws {
        try await withModel(scope: .activeCards) { model, database in
            let metrics = try database.homeMetrics(now: .now, overdueDayThreshold: 30, isSessionUnlocked: false)
            let sections = try #require(model.sections)
            #expect(sections.count == metrics.activeCards)
            for section in sections {
                #expect(section.items.allSatisfy { $0.record.prayerCardId == section.id })
                #expect(section.title == section.items[0].cardTitle)
            }
        }
    }

    @Test func markAnsweredRemovesRow() async throws {
        try await withModel(scope: .unanswered) { model, _ in
            let before = ids(model)
            let first = try #require((model.sections ?? []).flatMap(\.items).first)
            model.setAnswered(first, answered: true)
            let after = ids(model)
            #expect(!after.contains(first.record.id))
            #expect(after.count == before.count - 1)
            #expect(model.errorMessage == nil)
        }
    }

    @Test func failedDeleteKeepsRowsAndSetsError() async throws {
        try await withModel(scope: .unanswered) { model, _ in
            let first = try #require((model.sections ?? []).flatMap(\.items).first)
            model.pendingDelete = first
            model.confirmDelete()
            let remaining = ids(model)
            #expect(model.errorMessage == nil)
            model.pendingDelete = first
            model.confirmDelete()
            #expect(ids(model) == remaining)
            #expect(model.errorMessage != nil)
        }
    }
}
