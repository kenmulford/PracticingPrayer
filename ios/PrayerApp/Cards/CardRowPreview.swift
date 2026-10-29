import SwiftUI

struct CardRowPreview: View {
    let title: String
    let prayerTitles: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(verbatim: title).font(.headline)
            ForEach(prayerTitles, id: \.self) { prayer in
                Text(verbatim: prayer).font(.subheadline).foregroundStyle(.secondary)
            }
        }
        .padding()
    }
}
