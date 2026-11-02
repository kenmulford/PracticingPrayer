import Testing
@testable import PrayerApp

@Suite struct CardTileTests {
    @Test func parseDropsInvalidTokens() {
        #expect(ExpandedCardIds.parse("7,x,,3") == [3, 7])
    }

    @Test func formatSortsAscending() {
        #expect(ExpandedCardIds.format([7, 3]) == "3,7")
    }
}
