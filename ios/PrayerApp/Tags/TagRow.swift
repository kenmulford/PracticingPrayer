import PrayerKit
import SwiftUI

struct TagRow: View {
    let summary: TagSummary
    @ScaledMetric(relativeTo: .body) private var dot = 12

    var body: some View {
        HStack {
            Image(systemName: "circle.fill")
                .resizable()
                .frame(width: dot, height: dot)
                .foregroundStyle(Color.tag(summary.tag.color))
                .accessibilityHidden(true)
            VStack(alignment: .leading) {
                Text(summary.tag.name)
                    .font(.body)
                if summary.tag.isSystem == true {
                    Text("System")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Text(summary.prayerCount, format: .number)
                .foregroundStyle(.secondary)
                .monospacedDigit()
        }
        .accessibilityElement(children: .combine)
    }
}
