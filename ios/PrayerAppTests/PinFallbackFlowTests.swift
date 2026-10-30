import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

@MainActor
private final class SpyPresenter: PinSheetPresenting {
    private(set) var presented = 0
    private(set) var dismissed = 0
    func present(_ flow: PinFallbackFlow) { presented += 1 }
    func dismiss() { dismissed += 1 }
}

private final class FixedClock: @unchecked Sendable {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
}

@MainActor
private struct Harness {
    let store = InMemorySecItemStore()
    let clock = FixedClock()
    let presenter = SpyPresenter()
    let flow: PinFallbackFlow
    let tracker: PinAttemptTracker

    init(existingPin: String? = nil) throws {
        let store = store
        let now = clock.now
        tracker = PinAttemptTracker(store: PinAttemptStore(store: store), now: { now })
        flow = PinFallbackFlow(record: PinRecord(store: store), tracker: tracker, presenter: presenter)
        if let existingPin { try PinRecord(store: store).write(PinHasher.hash(existingPin)) }
    }

    func start() async -> Task<Bool, Never> {
        let task = Task { await flow.run() }
        while flow.sheet == nil { await Task.yield() }
        return task
    }

    func failures() throws -> Int { try tracker.state().failures }
}

@MainActor
@Suite struct PinFallbackFlowTests {
    @Test func noRecordPresentsSetPin() async throws {
        let h = try Harness()
        let task = await h.start()
        #expect(h.flow.sheet == .setPin)
        #expect(h.presenter.presented == 1)
        h.flow.cancel()
        #expect(await task.value == false)
    }

    @Test func existingRecordPresentsEnterPin() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        #expect(h.flow.sheet == .enterPin)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func saveWritesVerifiableRecordAndUnlocks() async throws {
        let h = try Harness()
        let task = await h.start()
        await h.flow.save(pin: "654321")
        #expect(await task.value)
        #expect(PinHasher.verify("654321", storedRecord: try PinRecord(store: h.store).read()))
        #expect(h.flow.sheet == nil)
        #expect(h.presenter.dismissed == 1)
    }

    @Test func correctPinUnlocksAndClearsFailures() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        await h.flow.submit(pin: "000000")
        #expect(try h.failures() == 1)
        await h.flow.submit(pin: "123456")
        #expect(await task.value)
        #expect(try h.failures() == 0)
    }

    @Test func wrongPinShowsFourAttemptsLeft() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        await h.flow.submit(pin: "000000")
        #expect(h.flow.message == .incorrect(attemptsLeft: 4))
        #expect(h.flow.sheet == .enterPin)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func fifthWrongPinLocksAndRefusesSubmit() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        for _ in 0..<5 { await h.flow.submit(pin: "000000") }
        #expect(h.flow.lockedUntil == h.clock.now.addingTimeInterval(30))
        #expect(h.flow.message == nil)
        await h.flow.submit(pin: "123456")
        #expect(try h.failures() == 5)
        #expect(h.flow.sheet == .enterPin)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func concurrentSubmitsRecordOneAttempt() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        async let first: Void = h.flow.submit(pin: "000000")
        async let second: Void = h.flow.submit(pin: "000000")
        _ = await (first, second)
        #expect(try h.failures() == 1)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func cancelLeavesRecordAndCount() async throws {
        let h = try Harness(existingPin: "123456")
        let before = try PinRecord(store: h.store).read()
        let task = await h.start()
        await h.flow.submit(pin: "000000")
        h.flow.cancel()
        #expect(await task.value == false)
        #expect(try PinRecord(store: h.store).read() == before)
        #expect(try h.failures() == 1)
        #expect(h.presenter.dismissed == 1)
    }

    @Test func keychainFailureShowsVerifyFailed() async throws {
        let h = try Harness(existingPin: "123456")
        let task = await h.start()
        h.store.failure = errSecInteractionNotAllowed
        await h.flow.submit(pin: "123456")
        #expect(h.flow.message == .verifyFailed)
        #expect(h.flow.sheet == .enterPin)
        h.store.failure = nil
        #expect(try h.failures() == 0)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func readFailureOnStartShowsEnterPinWithVerifyFailed() async throws {
        let h = try Harness()
        h.store.failure = errSecInteractionNotAllowed
        let task = await h.start()
        #expect(h.flow.sheet == .enterPin)
        #expect(h.flow.message == .verifyFailed)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func saveFailureKeepsSheet() async throws {
        let h = try Harness()
        let task = await h.start()
        h.store.failure = errSecInteractionNotAllowed
        await h.flow.save(pin: "123456")
        #expect(h.flow.sheet == .setPin)
        #expect(h.flow.message == .verifyFailed)
        h.flow.cancel()
        _ = await task.value
    }

    @Test func validation() {
        #expect(PinFallbackFlow.canSave(pin: "123456", confirm: "123456"))
        #expect(!PinFallbackFlow.canSave(pin: "12345", confirm: "12345"))
        #expect(!PinFallbackFlow.canSave(pin: "123456", confirm: "123457"))
        #expect(PinFallbackFlow.isMismatch(pin: "123456", confirm: "123457"))
        #expect(!PinFallbackFlow.isMismatch(pin: "123456", confirm: "12345"))
        #expect(!PinFallbackFlow.isMismatch(pin: "123456", confirm: "123456"))
        #expect(PinFallbackFlow.sanitized("12a34567") == "123456")
    }
}
