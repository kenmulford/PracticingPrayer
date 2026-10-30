import PrayerKit
import SwiftUI

struct PrayersToolbar: ToolbarContent {
    let tags: [PrayerTagRecord]
    @Binding var selectedTagIds: Set<Int64>
    let prayerTime: PrayerTimeScope?
    let startPrayerTime: (PrayerTimeScope) -> Void
    let newPrayer: () -> Void
    let isUnlocked: Bool
    let toggleConfidential: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                prayerTimeButton
                filterMenu
                moreMenu
                addButton
            }
            .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { prayerTimeButton }
            ToolbarItem(placement: .topBarTrailing) { filterMenu }
            ToolbarItem(placement: .topBarTrailing) { moreMenu }
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var prayerTimeButton: some View {
        Button("Prayer Time", systemImage: "hands.and.sparkles") {
            if let prayerTime { startPrayerTime(prayerTime) }
        }
        .disabled(prayerTime == nil)
    }

    private var filterMenu: some View {
        TagFilterMenu(tags: tags, selection: $selectedTagIds)
    }

    private var moreMenu: some View {
        Menu("More", systemImage: "ellipsis.circle") {
            if isUnlocked {
                Button("Hide Confidential", systemImage: "lock.open", action: toggleConfidential)
            } else {
                Button("Show Confidential", systemImage: "lock", action: toggleConfidential)
            }
        }
    }

    private var addButton: some View {
        Button(action: newPrayer) {
            Label("New Prayer", systemImage: "plus")
        }
    }
}
