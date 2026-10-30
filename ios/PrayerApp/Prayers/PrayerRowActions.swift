import PrayerKit
import SwiftUI

struct PrayerRowActions: ViewModifier {
    let item: PrayerListItem
    let setAnswered: (Bool) -> Void
    let pray: (() -> Void)?
    let edit: (() -> Void)?
    let requestDelete: () -> Void

    @Environment(AppServices.self) private var services

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
                if let pray { Button("Pray", systemImage: "hands.and.sparkles", action: pray).disabled(isAnswered) }
                if (try? ShareItemBuilder.isShareable(cardId: item.record.prayerCardId, database: services.database)) == true {
                    ShareMenuItem(item: try? ShareItemBuilder.prayer(item.record))
                }
                statusButton
                if let edit { Button("Edit", systemImage: "pencil", action: edit) }
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
        pray: (() -> Void)? = nil,
        edit: (() -> Void)? = nil,
        requestDelete: @escaping () -> Void
    ) -> some View {
        modifier(PrayerRowActions(
            item: item, setAnswered: setAnswered, pray: pray, edit: edit, requestDelete: requestDelete))
    }
}

extension View {
    func deletePrayerDialog(
        isPresented: Binding<Bool>, title: String, confirm: @escaping () -> Void
    ) -> some View {
        confirmationDialog(
            "Delete “\(title)”?", isPresented: isPresented, titleVisibility: .visible
        ) {
            Button("Delete Prayer", role: .destructive, action: confirm)
            Button("Cancel", role: .cancel) {}
        }
    }
}
