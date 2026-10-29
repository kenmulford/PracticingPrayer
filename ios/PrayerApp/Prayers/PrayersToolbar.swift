import PrayerKit
import SwiftUI

struct PrayersToolbar: ToolbarContent {
    let tags: [PrayerTagRecord]
    @Binding var selectedTagIds: Set<Int64>
    let prayerTime: PrayerTimeScope?
    let startPrayerTime: (PrayerTimeScope) -> Void
    let newPrayer: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                prayerTimeButton
                filterMenu
                addButton
            }
            .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { prayerTimeButton }
            ToolbarItem(placement: .topBarTrailing) { filterMenu }
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

    private var addButton: some View {
        Button(action: newPrayer) {
            Label("New Prayer", systemImage: "plus")
        }
    }
}
