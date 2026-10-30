import UIKit
import UserNotifications

final class AppDelegate: NSObject, UIApplicationDelegate {
    #if DEBUG
    static let forceLegacySceneArgument = "-forceLegacyScene"

    // Reproduces the restored-session path of an in-place 1.7.2 upgrade on a fresh install.
    func application(
        _ application: UIApplication,
        configurationForConnecting connectingSceneSession: UISceneSession,
        options: UIScene.ConnectionOptions
    ) -> UISceneConfiguration {
        let configuration = UISceneConfiguration(name: nil, sessionRole: connectingSceneSession.role)
        if ProcessInfo.processInfo.arguments.contains(Self.forceLegacySceneArgument) {
            configuration.delegateClass = LegacySceneDelegate.self
        }
        return configuration
    }
    #endif

    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        UNUserNotificationCenter.current().delegate = NotificationTapHandler.shared
        return true
    }
}
