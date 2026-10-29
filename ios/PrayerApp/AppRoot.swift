import SwiftUI

struct AppRoot: View {
    var body: some View {
        #if DEBUG
        if ProcessInfo.processInfo.arguments.contains(UpgradeGateView.argument) {
            UpgradeGateView()
        } else {
            ContentView()
        }
        #else
        ContentView()
        #endif
    }
}
