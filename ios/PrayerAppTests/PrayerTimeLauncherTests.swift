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

@MainActor
private func withAsyncLauncher(
    _ body: (PrayerTimeLauncher, PrayerDatabase, CardMutations) async throws -> Void
) async throws {
    let (database, copy) = try openFixture()
    defer {
        try? database.close()
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let preferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerTimeLauncherTests-\(UUID().uuidString)")!)
    preferences.archivedFolderId = 2
    try await body(
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

    @Test func collectionOptionsMarkLockedCollectionWhileLocked() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let collections = CollectionStore(database: database)
            let box = try collections.create(name: "Locked markLocked", now: now)
            let card = try cards.create(CardDraft(title: "Secret markLocked", boxId: box.id), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p2"), now: now)
            try collections.setProtection(
                id: box.id, protectAllCards: true, mode: .lockedVisible, now: now, ownerVerified: true)
            let locked = try launcher.collectionOptions().first { $0.id == box.id }
            #expect(locked?.isLocked == true)
            #expect(locked?.activeCount == 0)
            let unlocked = try launcher.collectionOptions(isSessionUnlocked: true).first { $0.id == box.id }
            #expect(unlocked?.isLocked == false)
            #expect(unlocked?.activeCount == 2)
            #expect(try launcher.items(for: .collection(box.id)).isEmpty)
            #expect(try launcher.items(for: .collection(box.id), isSessionUnlocked: true).count == 2)
        }
    }

    @Test func collectionOptionsOmitHiddenCollectionWhileLocked() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let collections = CollectionStore(database: database)
            let box = try collections.create(name: "Hidden omitHidden", now: now)
            let card = try cards.create(CardDraft(title: "Secret omitHidden", boxId: box.id), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            try collections.setProtection(
                id: box.id, protectAllCards: true, mode: .hidden, now: now, ownerVerified: true)
            #expect(!(try launcher.collectionOptions()).contains { $0.id == box.id })
            let unlocked = try launcher.collectionOptions(isSessionUnlocked: true).first { $0.id == box.id }
            #expect(unlocked?.activeCount == 1)
            #expect(unlocked?.isLocked == false)
        }
    }

    @Test func collectionOptionsCountOnlyUnmaskedPrayersWhileLocked() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let box = try CollectionStore(database: database).create(name: "Mixed countUnmasked", now: now)
            let open = try cards.create(CardDraft(title: "Open countUnmasked", boxId: box.id), now: now)
            let secret = try cards.create(
                CardDraft(title: "Secret countUnmasked", boxId: box.id, protectionMode: .lockedVisible), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: open.id, title: "o1"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: open.id, title: "o2"), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: secret.id, title: "s1"), now: now)
            let locked = try launcher.collectionOptions().first { $0.id == box.id }
            #expect(locked?.activeCount == 2)
            #expect(locked?.isLocked == false)
            #expect(try launcher.collectionOptions(isSessionUnlocked: true).first { $0.id == box.id }?.activeCount == 3)
        }
    }

    @Test func collectionOptionsIgnoreHiddenCardsWhileLocked() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let box = try CollectionStore(database: database).create(name: "Hidden ignoreHidden", now: now)
            let card = try cards.create(
                CardDraft(title: "Secret ignoreHidden", boxId: box.id, protectionMode: .hidden), now: now)
            _ = try store.create(PrayerDraft(prayerCardId: card.id, title: "p1"), now: now)
            #expect(!(try launcher.collectionOptions()).contains { $0.id == box.id })
        }
    }

    @Test func collectionOptionsListLockedCollectionWithNoActivePrayer() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let collections = CollectionStore(database: database)
            let box = try collections.create(name: "Locked noActive", now: now)
            let card = try cards.create(CardDraft(title: "Secret noActive", boxId: box.id), now: now)
            let prayer = try store.create(PrayerDraft(prayerCardId: card.id, title: "done"), now: now)
            try store.markAnswered(id: prayer, now: now)
            try collections.setProtection(
                id: box.id, protectAllCards: true, mode: .lockedVisible, now: now, ownerVerified: true)
            let locked = try launcher.collectionOptions().first { $0.id == box.id }
            #expect(locked?.isLocked == true)
            #expect(locked?.activeCount == 0)
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

    @Test func allScopeExcludesArchivedCards() throws {
        try withLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let kept = try cards.create(CardDraft(title: "Kept allScope"), now: now)
            let archived = try cards.create(CardDraft(title: "Archived allScope"), now: now)
            let keptPrayer = try store.create(PrayerDraft(prayerCardId: kept.id, title: "k"), now: now)
            let archivedPrayer = try store.create(PrayerDraft(prayerCardId: archived.id, title: "x"), now: now)
            try cards.archive(id: archived.id, now: now)
            let found = ids(try launcher.items(for: .all))
            #expect(found.contains(keptPrayer))
            #expect(!found.contains(archivedPrayer))
        }
    }

    @Test func allScopePromptsEvenWhenSessionIsUnlocked() async throws {
        let (database, copy) = try openFixture()
        defer {
            try? database.close()
            for suffix in ["", "-journal", "-wal", "-shm"] {
                try? FileManager.default.removeItem(atPath: copy.path + suffix)
            }
        }
        let preferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerTimeLauncherTests-\(UUID().uuidString)")!)
        let card = try CardMutations(database: database, preferences: preferences)
            .create(CardDraft(title: "Secret allScope", protectionMode: .lockedVisible), now: now)
        let prayer = try PrayerStore(database: database)
            .create(PrayerDraft(prayerCardId: card.id, title: "secret"), now: now)
        let authenticator = CountingOwnerAuthenticator(.success)
        let session = ConfidentialSession(authenticator: authenticator)
        let launcher = PrayerTimeLauncher(database: database)
        #expect(ids(try await launcher.items(for: .all, session: session)).contains(prayer))
        #expect(authenticator.calls == 1)
        #expect(session.isUnlocked)
        #expect(ids(try await launcher.items(for: .all, session: session)).contains(prayer))
        #expect(authenticator.calls == 2)
        authenticator.outcome = .failed
        #expect(!ids(try await launcher.items(for: .all, session: session)).contains(prayer))
        #expect(authenticator.calls == 3)
    }

    @Test func lockedCollectionStartPromptsWithoutActivePrayer() async throws {
        let (database, copy) = try openFixture()
        defer {
            try? database.close()
            for suffix in ["", "-journal", "-wal", "-shm"] {
                try? FileManager.default.removeItem(atPath: copy.path + suffix)
            }
        }
        let preferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerTimeLauncherTests-\(UUID().uuidString)")!)
        let collections = CollectionStore(database: database)
        let box = try collections.create(name: "Locked startNoActive", now: now)
        let card = try CardMutations(database: database, preferences: preferences)
            .create(CardDraft(title: "Secret startNoActive", boxId: box.id), now: now)
        let store = PrayerStore(database: database)
        let prayer = try store.create(PrayerDraft(prayerCardId: card.id, title: "done"), now: now)
        try store.markAnswered(id: prayer, now: now)
        try collections.setProtection(
            id: box.id, protectAllCards: true, mode: .lockedVisible, now: now, ownerVerified: true)
        let launcher = PrayerTimeLauncher(database: database)

        let allowing = CountingOwnerAuthenticator(.success)
        await #expect(throws: PrayerTimeLauncherError.noActivePrayers) {
            try await launcher.items(for: .collection(box.id), session: ConfidentialSession(authenticator: allowing))
        }
        #expect(allowing.calls == 1)

        let failing = CountingOwnerAuthenticator(.failed)
        let items = try await launcher.items(for: .collection(box.id), session: ConfidentialSession(authenticator: failing))
        #expect(items.isEmpty)
        #expect(failing.calls == 1)
    }

    @Test func lockedCollectionStartPromptsAndReturnsProtectedPrayers() async throws {
        let (database, copy) = try openFixture()
        defer {
            try? database.close()
            for suffix in ["", "-journal", "-wal", "-shm"] {
                try? FileManager.default.removeItem(atPath: copy.path + suffix)
            }
        }
        let preferences = AppPreferences(defaults: UserDefaults(suiteName: "PrayerTimeLauncherTests-\(UUID().uuidString)")!)
        let collections = CollectionStore(database: database)
        let box = try collections.create(name: "Locked startProtected", now: now)
        let card = try CardMutations(database: database, preferences: preferences)
            .create(CardDraft(title: "Secret startProtected", boxId: box.id), now: now)
        let prayer = try PrayerStore(database: database)
            .create(PrayerDraft(prayerCardId: card.id, title: "secret"), now: now)
        try collections.setProtection(
            id: box.id, protectAllCards: true, mode: .lockedVisible, now: now, ownerVerified: true)
        let authenticator = CountingOwnerAuthenticator(.success)
        let session = ConfidentialSession(authenticator: authenticator)
        let items = try await PrayerTimeLauncher(database: database).items(for: .collection(box.id), session: session)
        #expect(ids(items) == [prayer])
        #expect(authenticator.calls == 1)
    }

    @Test func lockedAllStartPromptsForLockedCardWithoutActivePrayer() async throws {
        try await withAsyncLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let locked = try cards.create(CardDraft(title: "Secret allLockedAnswered", protectionMode: .lockedVisible), now: now)
            let done = try store.create(PrayerDraft(prayerCardId: locked.id, title: "done"), now: now)
            try store.markAnswered(id: done, now: now)
            let open = try cards.create(CardDraft(title: "Open allLockedAnswered"), now: now)
            let openPrayer = try store.create(PrayerDraft(prayerCardId: open.id, title: "open"), now: now)
            let authenticator = CountingOwnerAuthenticator(.success)
            let items = try await launcher.items(for: .all, session: ConfidentialSession(authenticator: authenticator))
            #expect(ids(items).contains(openPrayer))
            #expect(authenticator.calls == 1)
        }
    }

    @Test func lockedAllStartIgnoresHiddenCollection() async throws {
        try await withAsyncLauncher { launcher, database, cards in
            let collections = CollectionStore(database: database)
            let box = try collections.create(name: "Hidden allHidden", now: now)
            let card = try cards.create(CardDraft(title: "Secret allHidden", boxId: box.id), now: now)
            let hidden = try PrayerStore(database: database)
                .create(PrayerDraft(prayerCardId: card.id, title: "hidden"), now: now)
            try collections.setProtection(
                id: box.id, protectAllCards: true, mode: .hidden, now: now, ownerVerified: true)
            let authenticator = CountingOwnerAuthenticator(.success)
            let items = try await launcher.items(for: .all, session: ConfidentialSession(authenticator: authenticator))
            #expect(ids(items) == ids(try launcher.items(for: .all)))
            #expect(!ids(items).contains(hidden))
            #expect(authenticator.calls == 0)
        }
    }

    @Test func lockedTagsStartPromptsForLockedArchivedCard() async throws {
        try await withAsyncLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let tag = try TagStore(database: database).createTag(name: "tagsArchived", color: "#FF0000", now: now)
            let open = try cards.create(CardDraft(title: "Open tagsArchived"), now: now)
            let openPrayer = try store.create(PrayerDraft(prayerCardId: open.id, title: "open", tagIds: [tag.id]), now: now)
            let locked = try cards.create(CardDraft(title: "Secret tagsArchived", protectionMode: .lockedVisible), now: now)
            let done = try store.create(PrayerDraft(prayerCardId: locked.id, title: "done", tagIds: [tag.id]), now: now)
            try store.markAnswered(id: done, now: now)
            try cards.archive(id: locked.id, now: now)
            let authenticator = CountingOwnerAuthenticator(.success)
            let items = try await launcher.items(for: .tags([tag.id]), session: ConfidentialSession(authenticator: authenticator))
            #expect(ids(items) == [openPrayer])
            #expect(authenticator.calls == 1)
        }
    }

    @Test func lockedCardsStartPromptsForMaskedCardWithoutActivePrayer() async throws {
        try await withAsyncLauncher { launcher, database, cards in
            let store = PrayerStore(database: database)
            let card = try cards.create(CardDraft(title: "Secret cardsMasked", protectionMode: .lockedVisible), now: now)
            let done = try store.create(PrayerDraft(prayerCardId: card.id, title: "done"), now: now)
            try store.markAnswered(id: done, now: now)
            let allowing = CountingOwnerAuthenticator(.success)
            await #expect(throws: PrayerTimeLauncherError.noActivePrayers) {
                try await launcher.items(for: .cards([card.id]), session: ConfidentialSession(authenticator: allowing))
            }
            #expect(allowing.calls == 1)
            let failing = CountingOwnerAuthenticator(.failed)
            let items = try await launcher.items(for: .cards([card.id]), session: ConfidentialSession(authenticator: failing))
            #expect(items.isEmpty)
            #expect(failing.calls == 1)
        }
    }

    @Test func lockedCardsStartSkipsPromptForUnmaskedCards() async throws {
        try await withAsyncLauncher { launcher, database, cards in
            let card = try cards.create(CardDraft(title: "Open cardsUnmasked"), now: now)
            let prayer = try PrayerStore(database: database)
                .create(PrayerDraft(prayerCardId: card.id, title: "p"), now: now)
            let authenticator = CountingOwnerAuthenticator(.success)
            let items = try await launcher.items(for: .cards([card.id]), session: ConfidentialSession(authenticator: authenticator))
            #expect(ids(items) == [prayer])
            #expect(authenticator.calls == 0)
        }
    }
}

@MainActor
private final class CountingOwnerAuthenticator: DeviceOwnerAuthenticating {
    var outcome: DeviceOwnerAuthOutcome
    private(set) var calls = 0

    init(_ outcome: DeviceOwnerAuthOutcome) {
        self.outcome = outcome
    }

    func authenticate(reason: String) async -> DeviceOwnerAuthOutcome {
        calls += 1
        return outcome
    }
}
