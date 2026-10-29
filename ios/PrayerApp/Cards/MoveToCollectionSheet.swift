import PrayerKit
import SwiftUI

struct MoveToCollectionSheet: View {
    let collections: [CardBoxRecord]
    let currentBoxId: Int64?
    let select: (Int64) -> Void

    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                row(id: 0) { Text("Loose Cards") }
                ForEach(collections, id: \.id) { box in
                    row(id: box.id) { Text(verbatim: box.name) }
                }
            }
            .navigationTitle("Move To")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
        }
    }

    private func row(id: Int64, @ViewBuilder name: () -> some View) -> some View {
        Button {
            select(id)
            dismiss()
        } label: {
            HStack {
                name()
                Spacer()
                if id == currentBoxId {
                    Image(systemName: "checkmark").accessibilityHidden(true)
                }
            }
        }
        .foregroundStyle(.primary)
        .accessibilityAddTraits(id == currentBoxId ? .isSelected : [])
    }
}
