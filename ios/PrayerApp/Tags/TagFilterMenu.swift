import PrayerKit
import SwiftUI

struct TagFilterMenu: View {
    let tags: [PrayerTagRecord]
    @Binding var selection: Set<Int64>

    var body: some View {
        Menu {
            if !selection.isEmpty {
                Button("Clear Filter") { selection = [] }
            }
            ForEach(tags, id: \.id) { tag in
                Toggle(isOn: binding(for: tag.id)) {
                    Label {
                        Text(verbatim: tag.name)
                    } icon: {
                        Image(systemName: "circle.fill")
                            .foregroundStyle(Color.tag(tag.color))
                            .accessibilityHidden(true)
                    }
                }
            }
        } label: {
            Image(systemName: selection.isEmpty
                ? "line.3.horizontal.decrease.circle" : "line.3.horizontal.decrease.circle.fill")
        }
        .accessibilityLabel(Text("Filter"))
        .accessibilityValue(Text(verbatim: Self.selectedNames(selection, in: tags).joined(separator: ", ")))
        .disabled(tags.isEmpty)
    }

    private func binding(for id: Int64) -> Binding<Bool> {
        Binding(
            get: { selection.contains(id) },
            set: { isOn in
                if isOn { selection.insert(id) } else { selection.remove(id) }
            })
    }

    static func pruned(_ selection: Set<Int64>, to tags: [PrayerTagRecord]) -> Set<Int64> {
        selection.intersection(tags.map(\.id))
    }

    static func selectedNames(_ selection: Set<Int64>, in tags: [PrayerTagRecord]) -> [String] {
        tags.filter { selection.contains($0.id) }.map(\.name)
    }
}
