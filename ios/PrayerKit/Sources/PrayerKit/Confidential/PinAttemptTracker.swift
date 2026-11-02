import Foundation

/// Failed-PIN counting with a doubling lockout, ported from the MAUI PinAttemptTracker.
public struct PinAttemptTracker {
    public static let lockoutThreshold = 5
    private static let baseLockoutSeconds = 30.0
    private static let maxLockoutSeconds = 1800.0

    private let store: PinAttemptStore
    private let now: @Sendable () -> Date
    private let monotonic: @Sendable () -> Double

    /// `monotonic` counts seconds since boot, sleep included, so moving the wall clock cannot shorten a lockout.
    public init(
        store: PinAttemptStore = PinAttemptStore(), now: @escaping @Sendable () -> Date = { Date() },
        uptime monotonic: @escaping @Sendable () -> Double = {
            Double(clock_gettime_nsec_np(CLOCK_MONOTONIC_RAW)) / 1_000_000_000
        }
    ) {
        self.store = store
        self.now = now
        self.monotonic = monotonic
    }

    public func state() throws(PinAttemptStoreError) -> PinAttemptState {
        try store.read()
    }

    public func activeLockout(in state: PinAttemptState) -> Date? {
        guard let lockedUntil = state.lockedUntil else { return nil }
        let current = now()
        var until: Date? = current < lockedUntil ? lockedUntil : nil
        // A smaller reading means a reboot; the wall clock is then the only record.
        if let seconds = state.lockSeconds, let armed = state.armedMonotonic {
            let elapsed = monotonic() - armed
            if elapsed >= 0, elapsed < seconds {
                let monotonicEnd = current.addingTimeInterval(seconds - elapsed)
                until = max(until ?? monotonicEnd, monotonicEnd)
            }
        }
        return until
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
            state.lockSeconds = seconds
            state.armedMonotonic = monotonic()
        }
        try store.write(state)
        return state
    }

    public func reset() throws(PinAttemptStoreError) {
        try store.write(.cleared)
    }
}
