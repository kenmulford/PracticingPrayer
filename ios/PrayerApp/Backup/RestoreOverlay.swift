import SwiftUI

struct RestoreOverlay: View {
    var body: some View {
        ProgressView("Restoring\u{2026}")
            .controlSize(.large)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(.background)
            .interactiveDismissDisabled()
            .accessibilityIdentifier("restore.overlay")
    }
}
