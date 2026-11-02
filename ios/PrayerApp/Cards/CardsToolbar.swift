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
    let manageCollections: () -> Void
    let isUnlocked: Bool
    let toggleConfidential: () -> Void
    let select: CardsSelectState

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            if select.isSelecting {
                doneItem
                ToolbarItemGroup(placement: .bottomBar) { bulkButtons }
                    .visibilityPriority(.high)
            } else {
                ToolbarItemGroup(placement: .topBarTrailing) { filterMenu }
                    .visibilityPriority(.high)
                ToolbarItemGroup(placement: .topBarTrailing) {
                    moreMenu
                    addButton
                }
                .visibilityPriority(.high)
            }
        } else {
            if select.isSelecting {
                doneItem
                ToolbarItemGroup(placement: .bottomBar) { bulkButtons }
            } else {
                ToolbarItemGroup(placement: .topBarTrailing) { filterMenu }
                ToolbarItem(placement: .topBarTrailing) { moreMenu }
                ToolbarItem(placement: .topBarTrailing) { addButton }
            }
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

    private var filterMenu: some View {
        TagFilterMenu(tags: tags, selection: $selectedTagIds)
    }

    private var moreMenu: some View {
        Menu("More", systemImage: "ellipsis.circle") {
            Button("Select Cards", systemImage: "checkmark.circle", action: select.start)
                .disabled(!select.canSelect)
            Button("Manage Collections", systemImage: "folder", action: manageCollections)
            if isUnlocked {
                Button("Hide Confidential", systemImage: "lock.open", action: toggleConfidential)
            } else {
                Button("Show Confidential", systemImage: "lock", action: toggleConfidential)
            }
        }
    }

    private var addButton: some View {
        Button(action: addCard) {
            Label("Add Card", systemImage: "plus")
        }
    }
}
