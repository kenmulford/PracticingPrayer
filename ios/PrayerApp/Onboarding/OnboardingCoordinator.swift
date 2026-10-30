import Foundation
import Observation
import PrayerKit
import TipKit

enum OnboardingStage: String, Codable, Sendable {
    case welcome, addCard, addPrayer, prayerTime, complete

    // Legacy 1.7.2 steps (PrayerTime, ShareIntro, ...) and unknown values fall to the nearest live stage.
    init(step: String, isComplete: Bool) {
        if isComplete { self = .complete; return }
        switch step {
        case "Complete": self = .complete
        case "CreateCard", "NameCard": self = .addCard
        case "AddRequest", "NameRequest": self = .addPrayer
        case "None", "Welcome": self = .welcome
        default: self = .prayerTime
        }
    }

    var persistedStep: String {
        switch self {
        case .welcome: "Welcome"
        case .addCard: "CreateCard"
        case .addPrayer: "AddRequest"
        case .prayerTime: "PrayerTimeHighlight"
        case .complete: "Complete"
        }
    }

    var next: OnboardingStage {
        switch self {
        case .welcome: .addCard
        case .addCard: .addPrayer
        case .addPrayer: .prayerTime
        case .prayerTime, .complete: .complete
        }
    }

    fileprivate var order: Int {
        switch self {
        case .welcome: 0
        case .addCard: 1
        case .addPrayer: 2
        case .prayerTime: 3
        case .complete: 4
        }
    }
}

@Observable
final class OnboardingCoordinator {
    // Owned here, not by AppPreferences: TipKit keeps invalidated tips, so a replay needs fresh tip ids.
    static let tourRunKey = "OnboardingTourRun"

    private(set) var stage: OnboardingStage
    private(set) var run: Int
    private(set) var tips: TipGroup
    /// A share link opened the app; the welcome and tour stay out of the way without touching saved progress.
    private(set) var isDeepLinkSession = false

    var showsWelcome: Bool { stage == .welcome && !isDeepLinkSession }

    @ObservationIgnored private let preferences: AppPreferences
    @ObservationIgnored private let defaults: UserDefaults

    init(preferences: AppPreferences, defaults: UserDefaults = .standard, isSuppressed: Bool = false) {
        self.preferences = preferences
        self.defaults = defaults
        let run = defaults.integer(forKey: Self.tourRunKey)
        self.run = run
        tips = Self.makeTips(run: run)
        if isSuppressed {
            stage = .complete
        } else {
            // A -Key launch argument wins every read, so the stage is read once and held in memory.
            stage = OnboardingStage(step: preferences.onboardingStep, isComplete: preferences.onboardingComplete)
            if stage != .complete { preferences.onboardingStep = stage.persistedStep }
        }
    }

    func getStarted() { advance(from: .welcome) }

    func skip() { set(.complete) }

    func advance(from stage: OnboardingStage) {
        guard self.stage == stage else { return }
        set(stage.next)
    }

    func replay() {
        run += 1
        defaults.set(run, forKey: Self.tourRunKey)
        tips = Self.makeTips(run: run)
        preferences.onboardingComplete = false
        set(.welcome)
    }

    func markDeepLinkSession() {
        isDeepLinkSession = true
        syncTips()
    }

    func syncTips() {
        AddCardTip.isActive = !isDeepLinkSession && stage == .addCard
        AddPrayerTip.isActive = !isDeepLinkSession && stage == .addPrayer
        PrayerTimeTip.isActive = !isDeepLinkSession && stage == .prayerTime
        // An ordered group waits on every earlier tip, so a resume past one invalidates it.
        if stage.order > OnboardingStage.addCard.order { AddCardTip(run: run).invalidate(reason: .actionPerformed) }
        if stage.order > OnboardingStage.addPrayer.order { AddPrayerTip(run: run).invalidate(reason: .actionPerformed) }
        if stage.order > OnboardingStage.prayerTime.order { PrayerTimeTip(run: run).invalidate(reason: .actionPerformed) }
    }

    func watchTips() async {
        let run = run
        await withTaskGroup(of: Void.self) { group in
            group.addTask { await self.watch(AddCardTip(run: run), at: .addCard) }
            group.addTask { await self.watch(AddPrayerTip(run: run), at: .addPrayer) }
            group.addTask { await self.watch(PrayerTimeTip(run: run), at: .prayerTime) }
        }
    }

    private func watch<T: Tip>(_ tip: T, at stage: OnboardingStage) async {
        for await status in tip.statusUpdates {
            if case .invalidated = status {
                advance(from: stage)
                return
            }
        }
    }

    private func set(_ stage: OnboardingStage) {
        self.stage = stage
        preferences.onboardingStep = stage.persistedStep
        if stage == .complete { preferences.onboardingComplete = true }
    }

    private static func makeTips(run: Int) -> TipGroup {
        TipGroup(.ordered) {
            AddCardTip(run: run)
            AddPrayerTip(run: run)
            PrayerTimeTip(run: run)
        }
    }
}
