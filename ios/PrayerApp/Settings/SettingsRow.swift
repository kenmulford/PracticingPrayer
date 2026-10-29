import SwiftUI

struct SettingsRow: View {
    let title: LocalizedStringResource
    let systemImage: String
    let tint: Color

    @ScaledMetric(relativeTo: .body) private var tile = 30

    var body: some View {
        Label {
            Text(title)
        } icon: {
            Image(systemName: systemImage)
                .font(.subheadline)
                .foregroundStyle(.white)
                .frame(width: tile, height: tile)
                .background(tint, in: RoundedRectangle(cornerRadius: 8))
                .accessibilityHidden(true)
        }
        .accessibilityElement(children: .combine)
    }
}
