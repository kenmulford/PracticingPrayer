import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

private func openFixture() throws -> (PrayerDatabase, URL) {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("PrayerTimeLauncherTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    return (try PrayerDatabase(path: copy.path), copy)
}

@MainActor
private func withLauncher(
    _ body: (PrayerTimeLauncher, PrayerDatabase, CardMutations) throws -> Void
) throws {
    let (database, copy) = try openFixture()
    defer {
        try? database.close()
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerTimeLauncherTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = 2
    try body(
        PrayerTimeLauncher(database: database), database,
        CardMutations(database: database, preferences: preferences))
}

private func ids(_ items: [PrayerListItem]) -> Set<Int64> {
    Set(items.map(\.record.id))
}

@MainActor
@Suite struct PrayerTimeLauncherTests {
    private let now = Date.now

    @Test func allScopeMatchesActiveUnlockedQuery() throws {
        try withLauncher { launcher, database, _ in
            let expected = try PrayerStore(database: database).prayers(segment: .active, isSessionUnlocked: false)
            #expect(try launcher.items(for: .all) == expected)
            #expect(!expected.isEmpty)
        }
    }

    @Test func cardsScopeIsUnionOfActivePrayers() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let a = try cards.create(CardDraft(title: "Alpha cardsScope"), now: now)
            let b = try cards.create(CardDraft(title: "Beta cardsScope"), now: now)
            let a1 = try store.create(PrayerDraft(prayerCardId: a.id, title: "a1"), now: now)
            let a2 = try store.create(PrayerDraft(prayerCardId: a.id, title: "a2"), now: now)
            let b1 = try store.create(PrayerDraft(prayerCardId: b.id, title: "b1"), now: now)
            try store.markAnswered(id: a2, now: now)
            #expect(ids(try launcher.items(for: .cards([a.id, b.id]))) == [a1, b1])
        }
    }

    @Test func cardsScopeIncludesProtectedPrayersOnlyWhenUnlocked() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let card = try cards.create(CardDraft(title: "Secret cardsScope", protectionMode: .lockedVisible), now: now)
            let prayer = try store.create(PrayerDraft(prayerCardId: card.id, title: "secret"), now: now)
            #expect(ids(try launcher.items(for: .cards([card.id]), isSessionUnlocked: true)) == [prayer])
            #expect(try launcher.items(for: .cards([card.id]), isSessionUnlocked: false).isEmpty)
        }
    }

    @Test func eligibleCardsExcludeArchivedAndEmpty() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let kept = try cards.create(CardDraft(title: "Kept eligible"), now: now)
            let archived = try cards.create(CardDraft(title: "Archived eligible"), now: now)
            let empty = try cards.create(CardDraft(title: "Empty eligible"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: kept.id, title: "k"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: archived.id, title: "x"), now: now)
            try cards.archive(id: archived.id, now: now)
            let found = try launcher.eligibleCards(search: "").flatMap(\.cards).map(\.card.id)
            #expect(found.contains(kept.id))
            #expect(!found.contains(archived.id))
            #expect(!found.contains(empty.id))
        }
    }

    @Test func collectionOptionsListOnlyCollectionsWithEligibleCards() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let collections = CollectionStore(database: database)
            let empty = try collections.create(name: "Empty options", now: now)
            let full = try collections.create(name: "Full options", now: now)
            let card = try cards.create(CardDraft(title: "In full", boxId: full.id), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p2"), now: now)
            let options = try launcher.collectionOptions()
            #expect(!options.contains { $0.id == empty.id })
            #expect(options.first { $0.id == full.id }?.activeCount == 2)
        }
    }

    @Test func collectionScopeReturnsItsActivePrayers() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let collection = try CollectionStore(database: database).create(name: "Scope collection", now: now)
            let card = try cards.create(CardDraft(title: "In scope", boxId: collection.id), now: now)
            let p1 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            let p2 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p2"), now: now)
            try store.markAnswered(id: p2, now: now)
            #expect(ids(try launcher.items(for: .collection(collection.id))) == [p1])
        }
    }

    @Test func tagOptionsCountActivePrayersOnly() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let tags = TagStore(database: database)
            let mixed = try tags.createTag(name: "Mixed options", color: "#FF0000", now: now)
            let answeredOnly = try tags.createTag(name: "Answered only", color: "#00FF00", now: now)
            let card = try cards.create(CardDraft(title: "Tag options"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "live", tagIds: [mixed.id]), now: now)
            let done1 = try store.create(PrayerDraft(prayerCardId: card.id, title: "done1", tagIds: [mixed.id]), now: now)
            let done2 = try store.create(PrayerDraft(prayerCardId: card.id, title: "done2", tagIds: [answeredOnly.id]), now: now)
            try store.markAnswered(id: done1, now: now)
            try store.markAnswered(id: done2, now: now)
            let options = try launcher.tagOptions()
            #expect(options.first { $0.id == mixed.id }?.activeCount == 1)
            #expect(!options.contains { $0.id == answeredOnly.id })
        }
    }

    @Test func tagsScopeReturnsPrayersCarryingAnySelectedTag() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let tags = TagStore(database: database)
            let one = try tags.createTag(name: "Scope one", color: "#FF0000", now: now)
            let two = try tags.createTag(name: "Scope two", color: "#0000FF", now: now)
            let card = try cards.create(CardDraft(title: "Tag scope"), now: now)
            let p1 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1", tagIds: [one.id]), now: now)
            let p2 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p2", tagIds: [two.id]), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p3"), now: now)
            #expect(ids(try launcher.items(for: .tags([one.id, two.id]))) == [p1, p2])
            #expect(ids(try launcher.items(for: .tags([one.id]))) == [p1])
        }
    }

    @Test func prayersScopeKeepsOrderAndDropsAnswered() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let card = try cards.create(CardDraft(title: "Order prayersScope"), now: now)
            let p1 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            let p2 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p2"), now: now)
            let p3 = try store.create(PrayerDraft(prayerCardId: card.id, title: "p3"), now: now)
            try store.markAnswered(id: p2, now: now)
            let found = try launcher.items(for: .prayers([p3, p2, p1])).map(\.record.id)
            #expect(found == [p3, p1])
        }
    }

    @Test func cardsScopeIncludesSystemAndArchivedCards() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let archived = try cards.create(CardDraft(title: "Archived cardsScope"), now: now)
            let onSystem = try store.create(PrayerDraft(prayerCardId: 4, title: "on system"), now: now)
            let onArchived = try store.create(PrayerDraft(prayerCardId: archived.id, title: "on archived"), now: now)
            try cards.archive(id: archived.id, now: now)
            #expect(ids(try launcher.items(for: .cards([4, archived.id]))).isSuperset(of: [onSystem, onArchived]))
        }
    }

    @Test func activePrayersScopeIsNilWhenAllAnswered() throws {
        try withLauncher { _, database, cards in
            let store = PrayerStore(database: database)
            let card = try cards.create(CardDraft(title: "Active scope"), now: now)
            let live = try store.create(PrayerDraft(prayerCardId: card.id, title: "live"), now: now)
            let done = try store.create(PrayerDraft(prayerCardId: card.id, title: "done"), now: now)
            try store.markAnswered(id: done, now: now)
            let items = try store.prayers(cardId: card.id)
            #expect(PrayerTimeScope.activePrayers(in: items) == .prayers([live]))
            #expect(PrayerTimeScope.activePrayers(in: items.filter { $0.record.isAnswered }) == nil)
        }
    }
}
