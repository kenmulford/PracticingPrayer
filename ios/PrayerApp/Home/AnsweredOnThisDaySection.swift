import PrayerKit
import SwiftUI

struct AnsweredOnThisDaySection: View {
    let prayers: [PrayerRequestRecord]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Answered on This Day")
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            ForEach(prayers, id: \.id) { record in
                VStack(alignment: .leading, spacing: 2) {
                    Text(record.title)
                    if let year = answeredYear(record) {
                        Text(year)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
            }
        }
    }

    private func answeredYear(_ record: PrayerRequestRecord) -> String? {
        guard let ticks = record.answeredAtTicks else { return nil }
        return (try? Date(ticks: ticks))?.formatted(.dateTime.year())
    }
}
