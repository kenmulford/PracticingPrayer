import PrayerKit
import Testing
@testable import PrayerApp

private func box(protectAll: Bool, mode: CardProtectionMode, isSystem: Bool = false) -> CardBoxRecord {
    CardBoxRecord(
        id: 1, name: "Box", isSystem: isSystem, systemKey: nil, sortOrder: 0,
        protectAllCards: protectAll, cardProtectionMode: mode,
        createdAtTicks: 0, updatedAtTicks: 0)
}

struct CollectionProtectionTests {
    @Test func lockedBoxIsLockedWhileLocked() {
        let state = CollectionProtection.rootState(
            of: box(protectAll: true, mode: .lockedVisible), isSessionUnlocked: false)
        #expect(state == .locked)
    }

    @Test func hiddenBoxIsHiddenWhileLocked() {
        let state = CollectionProtection.rootState(
            of: box(protectAll: true, mode: .hidden), isSessionUnlocked: false)
        #expect(state == .hidden)
    }

    @Test func protectedBoxesAreNormalWhileUnlocked() {
        for mode in [CardProtectionMode.lockedVisible, .hidden] {
            let state = CollectionProtection.rootState(
                of: box(protectAll: true, mode: mode), isSessionUnlocked: true)
            #expect(state == .normal)
        }
    }

    @Test func boxWithoutProtectAllIsNormal() {
        let state = CollectionProtection.rootState(
            of: box(protectAll: false, mode: .lockedVisible), isSessionUnlocked: false)
        #expect(state == .normal)
    }

    @Test func looseCardsIsNormal() {
        #expect(CollectionProtection.rootState(of: nil, isSessionUnlocked: false) == .normal)
    }

    @Test func systemBoxIsNormal() {
        let state = CollectionProtection.rootState(
            of: box(protectAll: true, mode: .hidden, isSystem: true), isSessionUnlocked: false)
        #expect(state == .normal)
    }
}
