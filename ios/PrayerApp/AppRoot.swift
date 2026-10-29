import SwiftUI

struct AppRoot: View {
    @State private var services = AppServices()

    var body: some View {
        #if DEBUG
        if ProcessInfo.processInfo.arguments.contains(UpgradeGateView.argument) {
            UpgradeGateView()
        } else {
            AppShell().environment(services)
        }
        #else
        AppShell().environment(services)
        #endif
    }
}
