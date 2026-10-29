import PrayerKit
import SwiftUI

struct PrayerListRow: View {
    let prayer: PrayerRequestRecord
    let cardTitle: String
    let isImported: Bool

    private var answeredDate: String? {
        guard prayer.isAnswered else { return nil }
        return prayer.answeredAtTicks.flatMap { try? Date(ticks: $0) }?
            .formatted(date: .abbreviated, time: .omitted)
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading) {
                Text(verbatim: prayer.title)
                    .font(.body)
                HStack(spacing: 4) {
                    Text(verbatim: cardTitle)
                    if isImported {
                        Image(systemName: "tray.and.arrow.down")
                    }
                }
                .font(.subheadline)
                .foregroundStyle(.secondary)
            }
            if prayer.isAnswered {
                Spacer()
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
                if let answeredDate {
                    Text(verbatim: answeredDate)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(Text(verbatim: accessibilityText))
    }

    private var accessibilityText: String {
        var parts = [prayer.title, cardTitle]
        if let answeredDate { parts.append(String(localized: "Answered \(answeredDate)")) }
        if isImported { parts.append(String(localized: "Imported")) }
        return parts.joined(separator: ", ")
    }
}
