import SwiftUI

struct PrivacyCoverView: View {
    @ScaledMetric(relativeTo: .title3) private var iconSize = 96

    var body: some View {
        VStack(spacing: 16) {
            Image("PrivacyCoverIcon")
                .resizable()
                .scaledToFit()
                .frame(width: iconSize, height: iconSize)
            Text("Practicing Prayer")
                .font(.title3.weight(.semibold))
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(.systemBackground).ignoresSafeArea())
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(Text("Practicing Prayer"))
        .accessibilityIdentifier("PrivacyCover")
    }
}
