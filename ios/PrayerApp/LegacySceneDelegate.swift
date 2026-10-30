import OSLog
import SwiftUI
import UIKit

/// The 1.7.2 MAUI build persisted its scene session with the delegate class name `SceneDelegate`, and the
/// system restores that session after an in-place update. SwiftUI attaches its own window only to sessions it
/// creates, so this class (exported to the ObjC runtime under the old name) hosts the same root for restored ones.
@objc(SceneDelegate)
final class LegacySceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
        guard let windowScene = scene as? UIWindowScene else { return }
        connectionOptions.urlContexts.forEach { InboundShareHandler.shared.handle($0.url) }
        connectionOptions.userActivities.forEach { InboundShareHandler.shared.handle(userActivity: $0) }
        let window = UIWindow(windowScene: windowScene)
        window.rootViewController = UIHostingController(rootView: AppRoot())
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
