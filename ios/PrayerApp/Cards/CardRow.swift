import PrayerKit
import SwiftUI

struct CardRow: View {
    let item: CardListItem

    private var count: Int { item.activePrayerCount ?? 0 }

    var body: some View {
        HStack {
            if item.isMasked {
                Image(systemName: "lock.fill")
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                Text("Protected")
            } else {
                Text(verbatim: item.card.title)
            }
            if item.card.isFavorite {
                Image(systemName: "star.fill").foregroundStyle(.yellow)
            }
            Spacer()
            if count > 0 {
                Text(count, format: .number)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        var parts = [item.isMasked ? String(localized: "Protected") : item.card.title]
        if item.card.isFavorite { parts.append(String(localized: "Favorite")) }
        if count > 0 { parts.append(String(localized: "\(count) active prayers")) }
        return parts.joined(separator: ", ")
    }
}
