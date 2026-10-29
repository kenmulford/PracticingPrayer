import Testing
@testable import PrayerKit

@Suite struct TextNormalizationTests {
    @Test func curlyQuotesBecomeAscii() {
        #expect(TextNormalization.normalizeQuotes("\u{2018}a\u{2019} \u{201C}b\u{201D}") == "'a' \"b\"")
    }

    @Test func collapseWhitespaceCollapsesAndTrims() {
        #expect(TextNormalization.collapseWhitespace("  a \t\n b  ") == "a b")
    }
}
