import OSLog
import SwiftUI
import UIKit

/// The 1.7.2 MAUI build persisted its scene session with the delegate class name `SceneDelegate`, and the
/// system restores that session after an in-place update. SwiftUI attaches its own window only to sessions it
/// creates (`UIHostingSceneDelegate` leaves a restored session without a window), so this class (exported to the
/// ObjC runtime under the old name) hosts the same root for restored ones.
@objc(SceneDelegate)
final class LegacySceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
        guard let windowScene = scene as? UIWindowScene else { return }
        connectionOptions.urlContexts.forEach { InboundShareHandler.shared.handle($0.url) }
        connectionOptions.userActivities.forEach { InboundShareHandler.shared.handle(userActivity: $0) }
        let window = UIWindow(windowScene: windowScene)
        window.rootViewController = UIHostingController(rootView: LegacySceneRoot(scene: windowScene))
        self.window = window
        window.makeKeyAndVisible()
        Logger(subsystem: "com.multithreadedllc.prayercards", category: "UpgradeGate")
            .debug("legacy scene session connected")
    }

    func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
        URLContexts.forEach { InboundShareHandler.shared.handle($0.url) }
    }

    func scene(_ scene: UIScene, continue userActivity: NSUserActivity) {
        InboundShareHandler.shared.handle(userActivity: userActivity)
    }
}

/// A plain `UIHostingController` is never given a `scenePhase`, so the root view would stay at its default and the
/// privacy cover, pending-import pickup and relock would not run. The phase is derived from the scene's own
/// lifecycle notifications instead.
private struct LegacySceneRoot: View {
    let scene: UIScene
    @State private var phase: ScenePhase

    init(scene: UIScene) {
        self.scene = scene
        _phase = State(initialValue: Self.phase(for: scene.activationState))
    }

    var body: some View {
        AppRoot()
            .environment(\.scenePhase, phase)
            .onReceive(NotificationCenter.default.publisher(for: UIScene.didActivateNotification)) { update($0, .active) }
            .onReceive(NotificationCenter.default.publisher(for: UIScene.willDeactivateNotification)) { update($0, .inactive) }
            .onReceive(NotificationCenter.default.publisher(for: UIScene.didEnterBackgroundNotification)) { update($0, .background) }
            .onReceive(NotificationCenter.default.publisher(for: UIScene.willEnterForegroundNotification)) { update($0, .inactive) }
            // A notification posted before the subscriptions exist is lost, so the state is re-read once on appear.
            .onAppear { phase = Self.phase(for: scene.activationState) }
    }

    private func update(_ notification: Notification, _ new: ScenePhase) {
        if (notification.object as? UIScene) === scene { phase = new }
    }

    private static func phase(for state: UIScene.ActivationState) -> ScenePhase {
        switch state {
        case .foregroundActive: .active
        case .foregroundInactive: .inactive
        default: .background
        }
    }
}
