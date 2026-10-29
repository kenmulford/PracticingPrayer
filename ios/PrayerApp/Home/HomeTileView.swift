import SwiftUI

struct HomeTileView: View {
    let title: LocalizedStringResource
    let systemImage: String
    let color: Color
    let value: Text
    var caption: LocalizedStringResource? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Image(systemName: systemImage)
                .font(.title2)
                .foregroundStyle(color)
            value
            Text(title)
                .font(.subheadline)
                .foregroundStyle(.secondary)
            if let caption {
                Text(caption)
                    .font(.caption)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
        .background(.background.secondary, in: .rect(cornerRadius: 12))
        .accessibilityElement(children: .combine)
        .accessibilityLabel(Text(title))
        .accessibilityValue(value)
    }
}
