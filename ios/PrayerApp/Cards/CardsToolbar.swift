import PrayerKit
import SwiftUI

struct CardsToolbar: ToolbarContent {
    let tags: [PrayerTagRecord]
    @Binding var selectedTagIds: Set<Int64>
    let addCard: () -> Void
    let manageCollections: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) { filterMenu }
                .visibilityPriority(.high)
            ToolbarItemGroup(placement: .topBarTrailing) {
                moreMenu
                addButton
            }
            .visibilityPriority(.high)
        } else {
            ToolbarItemGroup(placement: .topBarTrailing) { filterMenu }
            ToolbarItem(placement: .topBarTrailing) { moreMenu }
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var filterMenu: some View {
        TagFilterMenu(tags: tags, selection: $selectedTagIds)
    }

    private var moreMenu: some View {
        Menu("More", systemImage: "ellipsis.circle") {
            Button("Manage Collections", systemImage: "folder", action: manageCollections)
        }
    }

    private var addButton: some View {
        Button(action: addCard) {
            Label("Add Card", systemImage: "plus")
        }
    }
}
