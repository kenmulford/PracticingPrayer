import PrayerKit
import SwiftUI
import TipKit

@main
struct PracticingPrayerApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    init() {
        #if DEBUG
        if ProcessInfo.processInfo.arguments.contains("-ResetTips") {
            try? Tips.resetDatastore()
        }
        #endif
        try? Tips.configure()
    }

    var body: some Scene {
        WindowGroup {
            AppRoot()
        }
    }
}
