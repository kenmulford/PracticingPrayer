import PrayerKit
import Testing
@testable import PrayerApp

private func box(isSystem: Bool) -> CardBoxRecord {
    CardBoxRecord(
        id: 1, name: "Box", isSystem: isSystem, systemKey: nil, sortOrder: 0,
        protectAllCards: false, cardProtectionMode: .unprotected,
        createdAtTicks: 0, updatedAtTicks: 0)
}

struct CollectionRowMenuTests {
    @Test func userCollectionShowsMenu() {
        #expect(CollectionRowMenu.isShown(for: box(isSystem: false)))
    }

    @Test func looseCardsShowsNoMenu() {
        #expect(!CollectionRowMenu.isShown(for: nil))
    }

    @Test func systemCollectionShowsNoMenu() {
        #expect(!CollectionRowMenu.isShown(for: box(isSystem: true)))
    }
}
