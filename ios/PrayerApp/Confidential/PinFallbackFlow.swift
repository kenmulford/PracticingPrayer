import Accessibility
import Foundation
import Observation
import PrayerKit

/// The PIN sheets' state machine: runs when device-owner authentication reports no device passcode.
@Observable
final class PinFallbackFlow {
    enum Sheet: Equatable { case setPin, enterPin }
    enum Message: Equatable { case incorrect(attemptsLeft: Int), verifyFailed }

    private(set) var sheet: Sheet?
    private(set) var message: Message?
    private(set) var lockedUntil: Date?

    @ObservationIgnored private let record: PinRecord
    @ObservationIgnored private let tracker: PinAttemptTracker
    @ObservationIgnored private let presenter: (any PinSheetPresenting)?
    @ObservationIgnored private var continuation: CheckedContinuation<Bool, Never>?
    @ObservationIgnored private var isSubmitting = false

    init(record: PinRecord, tracker: PinAttemptTracker, presenter: (any PinSheetPresenting)? = nil) {
        self.record = record
        self.tracker = tracker
        self.presenter = presenter
    }

    var messageText: String? {
        switch message {
        case .incorrect(let attemptsLeft)?: String(localized: "Incorrect PIN. \(attemptsLeft) attempts left.")
        case .verifyFailed?: String(localized: "Couldn't verify PIN. Try again.")
        case nil: nil
        }
    }

    func run() async -> Bool {
        guard continuation == nil else { return false }
        message = nil
        do {
            sheet = try record.read() == nil ? .setPin : .enterPin
        } catch {
            sheet = .enterPin
            message = .verifyFailed
        }
        refreshLockout()
        presenter?.present(self)
        announceMessage()
        return await withCheckedContinuation { continuation = $0 }
    }

    func save(pin: String) async {
        let hashed = await Task.detached { PinHasher.hash(pin) }.value
        do {
            try record.write(hashed)
            resolve(true)
        } catch {
            show(.verifyFailed)
        }
    }

    func submit(pin: String) async {
        // The detached verify suspends; a second submit would pass the lockout check before the first records.
        guard !isSubmitting else { return }
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            let state = try tracker.state()
            if tracker.activeLockout(in: state) != nil {
                lockedUntil = tracker.activeLockout(in: state)
                return
            }
            let stored = try record.read()
            let verified = await Task.detached { PinHasher.verify(pin, storedRecord: stored) }.value
            if verified {
                try tracker.reset()
                resolve(true)
                return
            }
            let failed = try tracker.recordFailure()
            if let until = tracker.activeLockout(in: failed) {
                message = nil
                lockedUntil = until
                AccessibilityNotification.Announcement(
                    String(localized: "Too many attempts. Try again later.")).post()
            } else {
                show(.incorrect(attemptsLeft: tracker.attemptsLeft(in: failed)))
            }
        } catch {
            show(.verifyFailed)
        }
    }

    func cancel() {
        resolve(false)
    }

    func refreshLockout() {
        do {
            lockedUntil = tracker.activeLockout(in: try tracker.state())
        } catch {
            lockedUntil = nil
            show(.verifyFailed)
        }
    }

    static func sanitized(_ text: String) -> String {
        String(text.filter { $0.isASCII && $0.isNumber }.prefix(6))
    }

    static func canSave(pin: String, confirm: String) -> Bool {
        isSixDigits(pin) && pin == confirm
    }

    static func isMismatch(pin: String, confirm: String) -> Bool {
        isSixDigits(confirm) && pin != confirm
    }

    private static func isSixDigits(_ text: String) -> Bool {
        text.count == 6 && text.allSatisfy { $0.isASCII && $0.isNumber }
    }

    private func show(_ newMessage: Message) {
        message = newMessage
        announceMessage()
    }

    private func announceMessage() {
        if let messageText { AccessibilityNotification.Announcement(messageText).post() }
    }

    private func resolve(_ result: Bool) {
        guard let continuation else { return }
        self.continuation = nil
        sheet = nil
        message = nil
        lockedUntil = nil
        presenter?.dismiss()
        continuation.resume(returning: result)
    }
}
