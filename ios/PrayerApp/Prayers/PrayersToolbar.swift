import PrayerKit
import SwiftUI

struct PrayersToolbar: ToolbarContent {
    let tags: [PrayerTagRecord]
    @Binding var selectedTagIds: Set<Int64>
    let newPrayer: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                filterMenu
                addButton
            }
            .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { filterMenu }
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var filterMenu: some View {
        TagFilterMenu(tags: tags, selection: $selectedTagIds)
    }

    private var addButton: some View {
        Button(action: newPrayer) {
            Label("New Prayer", systemImage: "plus")
        }
    }
}
