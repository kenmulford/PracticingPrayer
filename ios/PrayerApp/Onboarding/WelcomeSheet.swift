import SwiftUI

struct WelcomeSheet: View {
    let getStarted: () -> Void
    let skip: () -> Void

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                Image("WelcomeIcon")
                    .resizable()
                    .scaledToFit()
                    .frame(width: 96, height: 96)
                    .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
                    .accessibilityHidden(true)
                Text("Welcome to Practicing Prayer")
                    .font(.largeTitle.bold())
                    .multilineTextAlignment(.center)
                    .accessibilityAddTraits(.isHeader)
                Text("Let's set up your first prayer card.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                Text("Got a shared prayer? Tap the link again to open it here.")
                    .font(.body)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                Button("Get Started", action: getStarted)
                    .buttonStyle(.glassProminent)
                    .controlSize(.large)
                    .padding(.top, 8)
                Button("Skip Tour", action: skip)
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
            }
            .padding(24)
            .frame(maxWidth: 700)
            .frame(maxWidth: .infinity)
        }
        .interactiveDismissDisabled()
    }
}
