import PrayerKit
import SwiftUI

struct AnsweredPrayersDisclosure<Row: View>: View {
    let prayers: [PrayerRequestRecord]
    private let row: (PrayerRequestRecord) -> Row
    @State private var isExpanded = false

    init(prayers: [PrayerRequestRecord], @ViewBuilder row: @escaping (PrayerRequestRecord) -> Row) {
        self.prayers = prayers
        self.row = row
    }

    var body: some View {
        DisclosureGroup(isExpanded: $isExpanded) {
            ForEach(prayers, id: \.id) { row($0) }
        } label: {
            HStack {
                Label {
                    Text("Answered")
                } icon: {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundStyle(.green)
                }
                Spacer()
                Text(prayers.count, format: .number)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            .accessibilityElement(children: .combine)
        }
    }
}

struct PrayerRow: View {
    let prayer: PrayerRequestRecord

    private var answeredDate: Date? {
        prayer.answeredAtTicks.flatMap { try? Date(ticks: $0) }
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading) {
                Text(verbatim: prayer.title)
                if prayer.isAnswered, let answeredDate {
                    Text(answeredDate.formatted(date: .abbreviated, time: .omitted))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            if prayer.isAnswered {
                Spacer()
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
                    .accessibilityLabel("Answered")
            }
        }
        .accessibilityElement(children: .combine)
    }
}
