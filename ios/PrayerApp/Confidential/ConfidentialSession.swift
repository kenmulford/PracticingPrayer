import Accessibility
import Foundation
import Observation

@Observable
final class ConfidentialSession {
    private(set) var isUnlocked = false
    /// Handles `.passcodeNotSet`; the PIN flow installs it.
    @ObservationIgnored var pinFallback: (() async -> Bool)?

    @ObservationIgnored private let authenticator: any DeviceOwnerAuthenticating
    @ObservationIgnored private var inFlight: Task<Bool, Never>?

    init(authenticator: any DeviceOwnerAuthenticating) {
        self.authenticator = authenticator
    }

    func authenticate(reason: LocalizedStringResource) async -> Bool {
        if isUnlocked { return true }
        if let inFlight { return await inFlight.value }
        let task = Task { [self] in
            let ok = await evaluate(reason: reason)
            inFlight = nil
            if ok { unlock() }
            return ok
        }
        inFlight = task
        return await task.value
    }

    /// Prompts even when already unlocked; use for exposed views that must re-verify the device owner.
    func verifyOwner(reason: LocalizedStringResource) async -> Bool {
        isUnlocked ? await evaluate(reason: reason) : await authenticate(reason: reason)
    }

    func toggle() {
        if isUnlocked {
            relock()
        } else {
            Task { _ = await authenticate(reason: "Show confidential cards") }
        }
    }

    func relock() {
        isUnlocked = false
    }

    private func unlock() {
        guard !isUnlocked else { return }
        isUnlocked = true
        AccessibilityNotification.Announcement(String(localized: "Confidential cards unlocked")).post()
    }

    private func evaluate(reason: LocalizedStringResource) async -> Bool {
        switch await authenticator.authenticate(reason: String(localized: reason)) {
        case .success: return true
        case .failed: return false
        case .passcodeNotSet:
            guard let pinFallback else { return false }
            return await pinFallback()
        }
    }
}
