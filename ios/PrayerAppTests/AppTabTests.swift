import Foundation
import Testing
@testable import PrayerApp

@Suite struct AppTabTests {
    @Test func cardsTitleIsCollections() {
        #expect(String(localized: AppTab.cards.title) == "Collections")
        #expect(String(localized: AppTab.cards.label) == "Cards")
    }
}
