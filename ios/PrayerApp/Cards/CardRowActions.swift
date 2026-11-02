import PrayerKit
import SwiftUI

struct CardRowActions: ViewModifier {
    let item: CardListItem
    let archivedFolderId: Int64
    let perform: (CardRowAction) -> Void

    @Environment(AppServices.self) private var services
    @State private var share = ShareItemBuilder.RowShare()

    func body(content: Content) -> some View {
        let model = CardRowActionModel(item: item, archivedFolderId: archivedFolderId)
        if model.menuActions.isEmpty {
            content
        } else {
            content
                .swipeActions(edge: .leading) {
                    ForEach(model.leadingActions, id: \.self) { button($0).tint(.yellow) }
                }
                .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                    ForEach(model.trailingActions, id: \.self) { action in
                        if action == .delete {
                            Button(action.title, systemImage: action.systemImage, role: .destructive) { perform(action) }
                        } else {
                            button(action).tint(.indigo)
                        }
                    }
                }
                .task(id: item) { share = .card(item.card, database: services.database) }
                .contextMenu {
                    ForEach(model.menuActions, id: \.self) { action in
                        if action == .delete {
                            Button(action.title, systemImage: action.systemImage, role: .destructive) { perform(action) }
                        } else {
                            button(action).disabled(action == .pray && !model.canPray)
                            if action == .pray, share.isShown { ShareMenuItem(item: share.item) }
                        }
                    }
                } preview: {
                    CardRowPreview(
                        title: item.card.title,
                        prayerTitles: CardRowActionModel.previewTitles(
                            cardId: item.card.id, database: services.database, preferences: services.preferences,
                            isSessionUnlocked: services.confidential.isUnlocked))
                }
        }
    }

    private func button(_ action: CardRowAction) -> some View {
        Button(action.title, systemImage: action.systemImage) { perform(action) }
    }
}

struct CardDeleteConfirmation: ViewModifier {
    @Binding var item: CardListItem?
    let archivedFolderId: Int64
    let confirm: (CardListItem) -> Void

    func body(content: Content) -> some View {
        if #available(iOS 27, *) {
            content.confirmationDialog("Delete Card", item: $item, titleVisibility: .visible) { card in
                actions(card)
            } message: { card in
                Text(verbatim: message(card))
            }
        } else {
            content.confirmationDialog(
                "Delete Card", isPresented: presented, titleVisibility: .visible, presenting: item
            ) { card in
                actions(card)
            } message: { card in
                Text(verbatim: message(card))
            }
        }
    }

    private var presented: Binding<Bool> {
        Binding(get: { item != nil }, set: { if !$0 { item = nil } })
    }

    @ViewBuilder
    private func actions(_ card: CardListItem) -> some View {
        Button("Delete", role: .destructive) { confirm(card) }
        Button("Cancel", role: .cancel) {}
    }

    private func message(_ card: CardListItem) -> String {
        CardRowActionModel(item: card, archivedFolderId: archivedFolderId).deleteMessage
    }
}
