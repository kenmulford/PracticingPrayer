import SwiftUI

@Observable
final class ShareStatus {
    var failed = false
}

struct ShareStatusView: View {
    let status: ShareStatus

    var body: some View {
        VStack(spacing: 16) {
            ProgressView()
            Text(status.failed ? "Couldn't save. Try again." : "Saving to Practicing Prayer…")
                .font(.body)
                .multilineTextAlignment(.center)
        }
        .padding()
        .foregroundStyle(Color("OnAccent"))
        .tint(Color("OnAccent"))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color("AccentColor").ignoresSafeArea())
    }
}
