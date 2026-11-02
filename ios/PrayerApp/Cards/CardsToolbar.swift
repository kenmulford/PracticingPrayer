import PrayerKit
import SwiftUI

struct CardsSelectState {
    let isSelecting: Bool
    let canSelect: Bool
    let hasSelection: Bool
    let hasMutable: Bool
    let isWorking: Bool
    let start: () -> Void
    let done: () -> Void
    let moveTo: () -> Void
    let archive: () -> Void
    let delete: () -> Void
}

struct CardsToolbar: ToolbarContent {
    let tags: [PrayerTagRecord]
    @Binding var selectedTagIds: Set<Int64>
    let addCard: () -> Void
    let addCollection: () -> Void
    let manageCollections: () -> Void
    let isUnlocked: Bool
    let toggleConfidential: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                filterMenu
                moreMenu
            }
            .visibilityPriority(.high)
            ToolbarItem(placement: .topBarTrailing) { addMenu }
                .visibilityPriority(.high)
        } else {
            ToolbarItemGroup(placement: .topBarTrailing) { filterMenu }
            ToolbarItem(placement: .topBarTrailing) { moreMenu }
            ToolbarItem(placement: .topBarTrailing) { addMenu }
        }
    }

    private var filterMenu: some View {
        TagFilterMenu(tags: tags, selection: $selectedTagIds)
    }

    private var moreMenu: some View {
        Menu("More", systemImage: "ellipsis.circle") {
            Button("Manage Collections", systemImage: "folder", action: manageCollections)
            if isUnlocked {
                Button("Hide Confidential", systemImage: "lock.open", action: toggleConfidential)
            } else {
                Button("Show Confidential", systemImage: "lock", action: toggleConfidential)
            }
        }
    }

    private var addMenu: some View {
        Menu("Add", systemImage: "plus") {
            Button("New Card", systemImage: "rectangle.stack.badge.plus", action: addCard)
            Button("New Collection", systemImage: "folder.badge.plus", action: addCollection)
        }
        .accessibilityIdentifier("Cards_Add")
    }
}

struct CollectionCardsToolbar: ToolbarContent {
    let canPray: Bool
    let pray: () -> Void
    let addCard: () -> Void
    let select: CardsSelectState

    var body: some ToolbarContent {
        if select.isSelecting {
            doneItem
            if #available(iOS 27, *) {
                ToolbarItemGroup(placement: .bottomBar) { bulkButtons }
                    .visibilityPriority(.high)
            } else {
                ToolbarItemGroup(placement: .bottomBar) { bulkButtons }
            }
        } else if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                prayButton
                moreMenu
            }
            .visibilityPriority(.high)
            ToolbarItem(placement: .topBarTrailing) { addButton }
                .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { prayButton }
            ToolbarItem(placement: .topBarTrailing) { moreMenu }
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var doneItem: some ToolbarContent {
        ToolbarItem(placement: .confirmationAction) {
            Button("Done", action: select.done).disabled(select.isWorking)
        }
    }

    @ViewBuilder
    private var bulkButtons: some View {
        Button("Move To…", systemImage: "folder", action: select.moveTo)
            .disabled(!select.hasSelection || select.isWorking)
        Button("Archive", systemImage: "archivebox", action: select.archive)
            .disabled(!select.hasMutable || select.isWorking)
        Button("Delete", systemImage: "trash", role: .destructive, action: select.delete)
            .disabled(!select.hasMutable || select.isWorking)
    }

    private var prayButton: some View {
        Button("Pray", systemImage: "hands.and.sparkles", action: pray)
            .disabled(!canPray)
    }

    private var moreMenu: some View {
        Menu("More", systemImage: "ellipsis.circle") {
            Button("Select Cards", systemImage: "checkmark.circle", action: select.start)
                .disabled(!select.canSelect)
        }
    }

    private var addButton: some View {
        Button(action: addCard) {
            Label("Add Card", systemImage: "plus")
        }
    }
}
