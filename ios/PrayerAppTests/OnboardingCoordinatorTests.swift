import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

@Suite struct OnboardingCoordinatorTests {
    private let preferences: AppPreferences
    private let defaults: UserDefaults

    init() {
        let suite = "OnboardingCoordinatorTests-\(UUID().uuidString)"
        preferences = AppPreferences(defaults: UserDefaults(suiteName: suite)!)
        defaults = UserDefaults(suiteName: "\(suite)-run")!
    }

    private func make(step: String? = nil, complete: Bool = false, suppressed: Bool = false) -> OnboardingCoordinator {
        if let step { preferences.onboardingStep = step }
        preferences.onboardingComplete = complete
        return OnboardingCoordinator(preferences: preferences, defaults: defaults, isSuppressed: suppressed)
    }

    @Test func firstLaunchStartsAtWelcome() {
        let coordinator = make()
        #expect(coordinator.stage == .welcome)
        #expect(preferences.onboardingStep == "Welcome")
    }

    @Test func legacyStepsReadAsPrayerTime() {
        for legacy in ["PrayerTime", "PrayerTimeActive", "ShareIntro", "SharePrayer"] {
            let coordinator = make(step: legacy)
            #expect(coordinator.stage == .prayerTime)
            #expect(preferences.onboardingStep == "PrayerTimeHighlight")
        }
    }

    @Test func nameStepsResume() {
        #expect(make(step: "NameCard").stage == .addCard)
        #expect(make(step: "NameRequest").stage == .addPrayer)
    }

    @Test func completeShowsNothing() {
        #expect(make(step: "AddRequest", complete: true).stage == .complete)
        #expect(make(step: "Complete").stage == .complete)
    }

    @Test func suppressedWritesNothing() {
        let coordinator = make(suppressed: true)
        #expect(coordinator.stage == .complete)
        #expect(preferences.onboardingStep == "None")
    }

    @Test func tourAdvancesInOrder() {
        let coordinator = make()
        coordinator.getStarted()
        #expect(preferences.onboardingStep == "CreateCard")
        coordinator.advance(from: .addCard)
        #expect(preferences.onboardingStep == "AddRequest")
        coordinator.advance(from: .addPrayer)
        #expect(preferences.onboardingStep == "PrayerTimeHighlight")
        coordinator.advance(from: .prayerTime)
        #expect(preferences.onboardingStep == "Complete")
        #expect(preferences.onboardingComplete)
    }

    @Test func staleAdvanceIsNoOp() {
        let coordinator = make(step: "AddRequest")
        coordinator.advance(from: .addCard)
        #expect(coordinator.stage == .addPrayer)
        #expect(preferences.onboardingStep == "AddRequest")
    }

    @Test func skipCompletes() {
        let coordinator = make()
        coordinator.skip()
        #expect(coordinator.stage == .complete)
        #expect(preferences.onboardingStep == "Complete")
        #expect(preferences.onboardingComplete)
    }

    @Test func replayRestarts() {
        let coordinator = make(complete: true)
        let before = coordinator.run
        coordinator.replay()
        #expect(coordinator.run == before + 1)
        #expect(defaults.integer(forKey: OnboardingCoordinator.tourRunKey) == before + 1)
        #expect(coordinator.stage == .welcome)
        #expect(preferences.onboardingStep == "Welcome")
        #expect(!preferences.onboardingComplete)
    }

    @Test func deepLinkSessionWritesNothing() {
        let coordinator = make()
        coordinator.markDeepLinkSession()
        #expect(!coordinator.showsWelcome)
        #expect(preferences.onboardingStep == "Welcome")
        #expect(!preferences.onboardingComplete)
    }

    @Test func deepLinkSessionDeactivatesTips() {
        let coordinator = make(step: "CreateCard")
        coordinator.syncTips()
        coordinator.markDeepLinkSession()
        #expect(!AddCardTip.isActive)
    }
}
