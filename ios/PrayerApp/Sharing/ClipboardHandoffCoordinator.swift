import Foundation
import Observation
import PrayerKit

/// Offers, once on first launch, to import a share link left on the clipboard by the install flow.
@Observable
final class ClipboardHandoffCoordinator {
    enum Prompt { case importPrompt, nothingFound }

    private(set) var prompt: Prompt?
    private(set) var isFinished = false

    @ObservationIgnored private let preferences: AppPreferences
    @ObservationIgnored private let pasteboard: any PasteboardReading
    @ObservationIgnored private let inboundShare: InboundShareHandler
    @ObservationIgnored private var hasRun = false

    init(preferences: AppPreferences, pasteboard: any PasteboardReading, inboundShare: InboundShareHandler) {
        self.preferences = preferences
        self.pasteboard = pasteboard
        self.inboundShare = inboundShare
    }

    func run(isEligible: Bool) async {
        guard !hasRun else { return }
        hasRun = true
        guard isEligible, !preferences.shareHandoffPrompted,
              (try? await pasteboard.hasProbableWebURL()) == true else {
            isFinished = true
            return
        }
        preferences.shareHandoffPrompted = true
        prompt = .importPrompt
    }

    func importClipboard() {
        if let url = ClipboardShareLink.importableURL(in: pasteboard.string()) {
            inboundShare.handle(url)
            finish()
        } else {
            prompt = .nothingFound
        }
    }

    func cancel() {
        finish()
    }

    func acknowledgeNothingFound() {
        finish()
    }

    private func finish() {
        prompt = nil
        isFinished = true
    }
}
