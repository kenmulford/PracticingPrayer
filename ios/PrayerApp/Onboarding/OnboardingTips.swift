import SwiftUI
import TipKit

struct AddCardTip: Tip {
    @Parameter static var isActive: Bool = false

    let run: Int
    var id: String { "AddCardTip-\(run)" }
    var title: Text { Text("Create Your First Prayer Card") }
    var message: Text? { Text("Give it a name \u{2014} a person, a topic, or a place.") }
    var rules: [Rule] { #Rule(Self.$isActive) { $0 == true } }
}

struct AddPrayerTip: Tip {
    @Parameter static var isActive: Bool = false

    let run: Int
    var id: String { "AddPrayerTip-\(run)" }
    var title: Text { Text("Add a Prayer Request") }
    var message: Text? { Text("Add your first prayer request to this card.") }
    var rules: [Rule] { #Rule(Self.$isActive) { $0 == true } }
}

struct PrayerTimeTip: Tip {
    @Parameter static var isActive: Bool = false

    let run: Int
    var id: String { "PrayerTimeTip-\(run)" }
    var title: Text { Text("Prayer Time") }
    var message: Text? { Text("Pray through your requests one by one.") }
    var rules: [Rule] { #Rule(Self.$isActive) { $0 == true } }
}

private struct OnboardingTipModifier: ViewModifier {
    @Environment(OnboardingCoordinator.self) private var onboarding
    let stage: OnboardingStage

    func body(content: Content) -> some View {
        switch stage {
        case .addCard: content.popoverTip(onboarding.tips.currentTip as? AddCardTip)
        case .addPrayer: content.popoverTip(onboarding.tips.currentTip as? AddPrayerTip)
        case .prayerTime: content.popoverTip(onboarding.tips.currentTip as? PrayerTimeTip)
        case .welcome, .complete: content
        }
    }
}

extension View {
    func onboardingTip(_ stage: OnboardingStage) -> some View {
        modifier(OnboardingTipModifier(stage: stage))
    }
}
