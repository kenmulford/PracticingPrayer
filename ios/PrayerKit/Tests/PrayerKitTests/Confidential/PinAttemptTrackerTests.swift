import Foundation
import Security
import Testing
@testable import PrayerKit

private final class MemoryStore: SecItemStore, @unchecked Sendable {
    var item: Data?
    var failure: OSStatus?

    func copyData(service: String, account: String) -> (status: OSStatus, data: Data?) {
        if let failure { return (failure, nil) }
        return item.map { (errSecSuccess, $0) } ?? (errSecItemNotFound, nil)
    }

    func update(service: String, account: String, data: Data) -> OSStatus {
        if let failure { return failure }
        guard item != nil else { return errSecItemNotFound }
        item = data
        return errSecSuccess
    }

    func add(service: String, account: String, data: Data) -> OSStatus {
        if let failure { return failure }
        item = data
        return errSecSuccess
    }
}

private final class Clock: @unchecked Sendable {
    var current = Date(timeIntervalSince1970: 1_800_000_000)
    var uptime = 5_000.0

    func advance(_ seconds: TimeInterval) {
        current = current.addingTimeInterval(seconds)
        uptime += seconds
    }
}

@Suite struct PinAttemptTrackerTests {
    private func make(_ store: MemoryStore = MemoryStore(), _ clock: Clock = Clock()) -> PinAttemptTracker {
        PinAttemptTracker(store: PinAttemptStore(store: store), now: { clock.current }, uptime: { clock.uptime })
    }

    @Test func missingItemReadsCleared() throws {
        #expect(try make().state() == .cleared)
    }

    @Test func firstFailureLeavesFourAttempts() throws {
        let tracker = make()
        let state = try tracker.recordFailure()
        #expect(state.failures == 1)
        #expect(tracker.attemptsLeft(in: state) == 4)
        #expect(tracker.activeLockout(in: state) == nil)
    }

    @Test func fifthFailureArmsThirtySeconds() throws {
        let clock = Clock()
        let tracker = make(MemoryStore(), clock)
        var state = PinAttemptState.cleared
        for _ in 0..<5 { state = try tracker.recordFailure() }
        #expect(state.lockedUntil == clock.current.addingTimeInterval(30))
        #expect(tracker.activeLockout(in: state) == state.lockedUntil)
        #expect(tracker.attemptsLeft(in: state) == 0)
    }

    @Test func furtherFailuresDoubleToCap() throws {
        let clock = Clock()
        let tracker = make(MemoryStore(), clock)
        var state = PinAttemptState.cleared
        for _ in 0..<6 { state = try tracker.recordFailure() }
        #expect(state.lockedUntil == clock.current.addingTimeInterval(60))
        for _ in 0..<5 { state = try tracker.recordFailure() }
        #expect(state.failures == 11)
        #expect(state.lockedUntil == clock.current.addingTimeInterval(1800))
    }

    @Test func stateSurvivesNewTracker() throws {
        let store = MemoryStore()
        let clock = Clock()
        for _ in 0..<5 { try make(store, clock).recordFailure() }
        let state = try make(store, clock).state()
        #expect(state.failures == 5)
        #expect(state.lockedUntil == clock.current.addingTimeInterval(30))
    }

    @Test func resetWritesZeroAndNull() throws {
        let store = MemoryStore()
        let tracker = make(store)
        for _ in 0..<5 { try tracker.recordFailure() }
        try tracker.reset()
        let json = String(decoding: try #require(store.item), as: UTF8.self)
        #expect(json.contains("\"lockedUntil\":null"))
        #expect(json.contains("\"failures\":0"))
    }

    @Test func keychainErrorThrows() {
        let store = MemoryStore()
        store.failure = errSecInteractionNotAllowed
        #expect(throws: PinAttemptStoreError.keychain(errSecInteractionNotAllowed)) { try make(store).state() }
        #expect(throws: PinAttemptStoreError.keychain(errSecInteractionNotAllowed)) { try make(store).recordFailure() }
    }

    @Test func lockoutEndsWhenClockPasses() throws {
        let clock = Clock()
        let tracker = make(MemoryStore(), clock)
        var state = PinAttemptState.cleared
        for _ in 0..<5 { state = try tracker.recordFailure() }
        clock.advance(31)
        #expect(tracker.activeLockout(in: state) == nil)
    }

    private func locked(_ clock: Clock) throws -> (PinAttemptTracker, PinAttemptState) {
        let tracker = make(MemoryStore(), clock)
        var state = PinAttemptState.cleared
        for _ in 0..<5 { state = try tracker.recordFailure() }
        return (tracker, state)
    }

    @Test func clockMovedBackwardsKeepsLockoutForMonotonicDuration() throws {
        let clock = Clock()
        let (tracker, state) = try locked(clock)
        clock.current = clock.current.addingTimeInterval(-3600)
        clock.uptime += 31
        #expect(tracker.activeLockout(in: state) != nil)
    }

    @Test func clockJumpedForwardPastLockoutKeepsItUntilUptimePasses() throws {
        let clock = Clock()
        let (tracker, state) = try locked(clock)
        clock.current = clock.current.addingTimeInterval(3600)
        clock.uptime += 10
        #expect(tracker.activeLockout(in: state) == clock.current.addingTimeInterval(20))
        clock.advance(21)
        #expect(tracker.activeLockout(in: state) == nil)
    }

    @Test func rebootFallsBackToWallClock() throws {
        let clock = Clock()
        let (tracker, state) = try locked(clock)
        clock.current = clock.current.addingTimeInterval(3600)
        clock.uptime = 3
        #expect(tracker.activeLockout(in: state) == nil)
        clock.current = clock.current.addingTimeInterval(-3590)
        #expect(tracker.activeLockout(in: state) == state.lockedUntil)
    }

    @Test func monotonicFieldsSurviveStoreRoundTrip() throws {
        let store = MemoryStore()
        let clock = Clock()
        for _ in 0..<5 { try make(store, clock).recordFailure() }
        clock.current = clock.current.addingTimeInterval(3600)
        clock.uptime += 5
        let tracker = make(store, clock)
        #expect(tracker.activeLockout(in: try tracker.state()) != nil)
    }

    @Test func recordWithoutMonotonicFieldsUsesWallClockOnly() throws {
        let clock = Clock()
        let tracker = make(MemoryStore(), clock)
        let state = PinAttemptState(failures: 5, lockedUntil: clock.current.addingTimeInterval(30))
        #expect(tracker.activeLockout(in: state) == state.lockedUntil)
        clock.advance(31)
        #expect(tracker.activeLockout(in: state) == nil)
    }
}
