import SwiftUI

struct HelpView: View {
    var body: some View {
        List {
            ForEach(FaqItem.all) { item in
                FaqRow(item: item)
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .frame(maxWidth: 700)
        .frame(maxWidth: .infinity)
        .background(Color(.systemGroupedBackground))
        .navigationTitle("Help")
        .navigationBarTitleDisplayMode(.inline)
    }
}

private struct FaqRow: View {
    let item: FaqItem
    @State private var isExpanded = false

    var body: some View {
        DisclosureGroup(isExpanded: $isExpanded) {
            Text(item.answer)
                .font(.body)
        } label: {
            Text(item.question)
                .font(.headline)
        }
    }
}
