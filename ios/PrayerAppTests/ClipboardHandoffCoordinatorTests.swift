import Foundation
import Testing
import PrayerKit
@testable import PrayerApp

@Suite struct ClipboardHandoffCoordinatorTests {
    private final class FakePasteboard: PasteboardReading {
        var text: String?
        var detects: Bool
        var throwsOnDetect = false
        private(set) var detectCalls = 0
        private(set) var stringCalls = 0

        init(text: String?, detects: Bool) {
            self.text = text
            self.detects = detects
        }

        func hasProbableWebURL() async throws -> Bool {
            detectCalls += 1
            if throwsOnDetect { throw CancellationError() }
            return detects
        }

        func string() -> String? {
            stringCalls += 1
            return text
        }
    }

    private let link = "https://practicingprayerapp.com/share/r?v=2&d=abc"
    private let preferences = AppPreferences(defaults: UserDefaults(suiteName: "ClipboardHandoffTests-\(UUID().uuidString)")!)

    private func make(_ pasteboard: FakePasteboard) -> (ClipboardHandoffCoordinator, InboundShareHandler) {
        let inbound = InboundShareHandler()
        return (ClipboardHandoffCoordinator(preferences: preferences, pasteboard: pasteboard, inboundShare: inbound), inbound)
    }

    @Test func matchPromptsWithoutReadingString() async {
        let pasteboard = FakePasteboard(text: link, detects: true)
        let (coordinator, _) = make(pasteboard)
        await coordinator.run(isEligible: true)
        #expect(preferences.shareHandoffPrompted)
        #expect(coordinator.prompt == .importPrompt)
        #expect(!coordinator.isFinished)
        #expect(pasteboard.stringCalls == 0)
    }

    @Test func noMatchFinishesQuietly() async {
        let (coordinator, _) = make(FakePasteboard(text: nil, detects: false))
        await coordinator.run(isEligible: true)
        #expect(!preferences.shareHandoffPrompted)
        #expect(coordinator.prompt == nil)
        #expect(coordinator.isFinished)
    }

    @Test func throwingDetectionFinishesQuietly() async {
        let pasteboard = FakePasteboard(text: link, detects: true)
        pasteboard.throwsOnDetect = true
        let (coordinator, _) = make(pasteboard)
        await coordinator.run(isEligible: true)
        #expect(coordinator.prompt == nil)
        #expect(coordinator.isFinished)
    }

    @Test func alreadyPromptedSkipsDetection() async {
        preferences.shareHandoffPrompted = true
        let pasteboard = FakePasteboard(text: link, detects: true)
        let (coordinator, _) = make(pasteboard)
        await coordinator.run(isEligible: true)
        #expect(pasteboard.detectCalls == 0)
        #expect(coordinator.isFinished)
    }

    @Test func ineligibleSkipsDetection() async {
        let pasteboard = FakePasteboard(text: link, detects: true)
        let (coordinator, _) = make(pasteboard)
        await coordinator.run(isEligible: false)
        #expect(pasteboard.detectCalls == 0)
        #expect(coordinator.isFinished)
    }

    @Test func importHandsShareLinkToHandler() async {
        let (coordinator, inbound) = make(FakePasteboard(text: "\(link)\nPray for me", detects: true))
        await coordinator.run(isEligible: true)
        coordinator.importClipboard()
        #expect(inbound.isDeepLinkSession)
        #expect(coordinator.prompt == nil)
        #expect(coordinator.isFinished)
    }

    @Test func foreignURLShowsNothingFound() async {
        let (coordinator, inbound) = make(FakePasteboard(text: "https://example.com", detects: true))
        await coordinator.run(isEligible: true)
        coordinator.importClipboard()
        #expect(coordinator.prompt == .nothingFound)
        #expect(!inbound.isDeepLinkSession)
        coordinator.acknowledgeNothingFound()
        #expect(coordinator.prompt == nil)
        #expect(coordinator.isFinished)
    }

    @Test func cancelFinishes() async {
        let (coordinator, _) = make(FakePasteboard(text: link, detects: true))
        await coordinator.run(isEligible: true)
        coordinator.cancel()
        #expect(coordinator.prompt == nil)
        #expect(coordinator.isFinished)
    }
}
