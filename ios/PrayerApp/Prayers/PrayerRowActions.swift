import PrayerKit
import SwiftUI

struct PrayerRowActions: ViewModifier {
    let item: PrayerListItem
    let setAnswered: (Bool) -> Void
    let pray: () -> Void
    let edit: () -> Void
    let requestDelete: () -> Void

    private var isAnswered: Bool { item.record.isAnswered }

    func body(content: Content) -> some View {
        content
            .swipeActions(edge: .leading) {
                statusButton.tint(isAnswered ? .orange : .green)
            }
            .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                deleteButton
            }
            .contextMenu {
                Button("Pray", systemImage: "hands.and.sparkles", action: pray)
                if case .link(let link)? = try? ShareCodec.encode(request: item.record), let url = URL(string: link) {
                    ShareLink(item: url) { Label("Share", systemImage: "square.and.arrow.up") }
                }
                statusButton
                Button("Edit", systemImage: "pencil", action: edit)
                deleteButton
            }
    }

    private var statusButton: some View {
        Button(
            isAnswered ? "Mark Active" : "Mark Answered",
            systemImage: isAnswered ? "arrow.uturn.backward.circle" : "checkmark.circle"
        ) { setAnswered(!isAnswered) }
    }

    private var deleteButton: some View {
        Button("Delete", systemImage: "trash", role: .destructive, action: requestDelete)
    }
}

extension View {
    func prayerRowActions(
        _ item: PrayerListItem,
        setAnswered: @escaping (Bool) -> Void,
        pray: @escaping () -> Void,
        edit: @escaping () -> Void,
        requestDelete: @escaping () -> Void
    ) -> some View {
        modifier(PrayerRowActions(
            item: item, setAnswered: setAnswered, pray: pray, edit: edit, requestDelete: requestDelete))
    }
}
