import Foundation

/// Failed-PIN counting with a doubling lockout, ported from the MAUI PinAttemptTracker.
public struct PinAttemptTracker {
    public static let lockoutThreshold = 5
    private static let baseLockoutSeconds = 30.0
    private static let maxLockoutSeconds = 1800.0

    private let store: PinAttemptStore
    private let now: @Sendable () -> Date

    public init(store: PinAttemptStore = PinAttemptStore(), now: @escaping @Sendable () -> Date = { Date() }) {
        self.store = store
        self.now = now
    }

    public func state() throws(PinAttemptStoreError) -> PinAttemptState {
        try store.read()
    }

    public func activeLockout(in state: PinAttemptState) -> Date? {
        guard let lockedUntil = state.lockedUntil, now() < lockedUntil else { return nil }
        return lockedUntil
    }

    public func attemptsLeft(in state: PinAttemptState) -> Int {
        max(0, Self.lockoutThreshold - state.failures)
    }

    @discardableResult
    public func recordFailure() throws(PinAttemptStoreError) -> PinAttemptState {
        var state = try store.read()
        state.failures += 1
        if state.failures >= Self.lockoutThreshold {
            let exponent = Double(state.failures - Self.lockoutThreshold)
            let seconds = min(Self.baseLockoutSeconds * pow(2, exponent), Self.maxLockoutSeconds)
            state.lockedUntil = now().addingTimeInterval(seconds)
        }
        try store.write(state)
        return state
    }

    public func reset() throws(PinAttemptStoreError) {
        try store.write(.cleared)
    }
}
