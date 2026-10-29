import PrayerKit
import SwiftUI
import Testing
@testable import PrayerApp

private func tag(_ id: Int64, _ name: String) -> PrayerTagRecord {
    PrayerTagRecord(id: id, name: name, color: nil, isSystem: false, createdAtTicks: 0, updatedAtTicks: 0)
}

struct TagFilterMenuTests {
    @Test func prunedDropsDeletedTag() {
        #expect(TagFilterMenu.pruned([1, 9], to: [tag(1, "Work")]) == [1])
    }

    @Test func selectedNamesFollowMenuOrder() {
        let tags = [tag(3, "Work"), tag(2, "Family")]
        #expect(TagFilterMenu.selectedNames([2, 3], in: tags) == ["Work", "Family"])
    }

    @Test func tagColorFallsBackForNilAndInvalidHex() {
        let fallback = Color(hex: UserColorStore.defaultPaletteHexValues[0])
        #expect(Color.tag(nil) == fallback)
        #expect(Color.tag("zz") == fallback)
    }
}
