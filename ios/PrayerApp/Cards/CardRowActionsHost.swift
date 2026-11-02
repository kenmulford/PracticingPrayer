import PrayerKit
import SwiftUI

/// Owns the row-action state and presentations so every card list shares one set of sheets and dialogs.
struct CardRowActionsHost<Content: View>: View {
    let onChange: () -> Void
    let onRemoved: (Int64) -> Void
    let content: (_ perform: @escaping (CardRowAction, CardListItem) -> Void) -> Content

    @Environment(AppServices.self) private var services
    @State private var cardToDelete: CardListItem?
    @State private var rowCard: CardListItem?
    @State private var isEditingRow = false
    @State private var isMovingRow = false
    @State private var rowError: String?
    @State private var prayScope: PrayerTimeScope?

    init(
        onChange: @escaping () -> Void,
        onRemoved: @escaping (Int64) -> Void,
        @ViewBuilder content: @escaping (_ perform: @escaping (CardRowAction, CardListItem) -> Void) -> Content
    ) {
        self.onChange = onChange
        self.onRemoved = onRemoved
        self.content = content
    }

    private var isSessionUnlocked: Bool { services.confidential.isUnlocked }

    var body: some View {
        content(rowAction)
            .modifier(
                CardDeleteConfirmation(
                    item: $cardToDelete, archivedFolderId: Int64(services.preferences.archivedFolderId)
                ) { card in
                    rowError = CardRowActionModel.perform(
                        .delete, cardId: card.card.id, database: services.database,
                        preferences: services.preferences, isSessionUnlocked: isSessionUnlocked)
                    if rowError == nil {
                        services.reconcileReminders()
                        onRemoved(card.card.id)
                    }
                    onChange()
                }
            )
            .sheet(isPresented: $isEditingRow, onDismiss: onChange) {
                CardEditorView(
                    card: rowCard?.card, database: services.database, preferences: services.preferences
                ) { _ in }
            }
            .sheet(isPresented: $isMovingRow) {
                MoveToCollectionSheet(
                    collections: Self.moveTargets(services.database), currentBoxId: rowCard?.card.boxId ?? 0,
                    select: moveRowCard)
            }
            .prayerTimeStart($prayScope)
            .alert("Couldn't Update Card", isPresented: rowErrorBinding) {
                Button("OK") {}
            } message: {
                Text(rowError ?? "")
            }
    }

    // Read at presentation time: a @State copy set in the same transaction as the sheet flag reached the sheet empty.
    static func moveTargets(_ database: PrayerDatabase) -> [CardBoxRecord] {
        (try? CollectionStore(database: database).collections()
            .filter { !$0.isSystem }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }) ?? []
    }

    private var rowErrorBinding: Binding<Bool> {
        Binding(get: { rowError != nil }, set: { if !$0 { rowError = nil } })
    }

    private func rowAction(_ action: CardRowAction, _ item: CardListItem) {
        switch action {
        case .pray:
            prayScope = .cards([item.card.id])
        case .edit:
            rowCard = item
            isEditingRow = true
        case .moveTo:
            rowCard = item
            isMovingRow = true
        case .delete:
            cardToDelete = item
        case .favorite, .unfavorite, .archive, .unarchive:
            rowError = CardRowActionModel.perform(
                action, cardId: item.card.id, database: services.database, preferences: services.preferences,
                isSessionUnlocked: isSessionUnlocked)
            if rowError == nil, action == .archive { onRemoved(item.card.id) }
            onChange()
        }
    }

    private func moveRowCard(toBox boxId: Int64) {
        guard let card = rowCard else { return }
        rowError = CardRowActionModel.move(
            cardId: card.card.id, toBox: boxId, database: services.database, preferences: services.preferences,
            isSessionUnlocked: isSessionUnlocked)
        onChange()
    }
}
