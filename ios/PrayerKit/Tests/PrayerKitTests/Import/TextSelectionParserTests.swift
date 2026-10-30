import Foundation
import Testing
@testable import PrayerKit

private let fixedNow = Date(timeIntervalSince1970: 1_777_636_800) // 2026-05-01 12:00 UTC
private let enUS = Locale(identifier: "en_US")

private func parse(_ text: String) -> ParseResult {
    TextSelectionParser.parse(text, now: fixedNow, locale: enUS)
}

@Suite struct TextSelectionParserTests {
    // MARK: markers
    @Test func numberedListStripsMarkers() {
        let r = parse("1. Mom\n2. Dad\n3. Sister")
        #expect(r.prayers.map(\.title) == ["Mom", "Dad", "Sister"])
    }

    @Test func numberedListWithCloseParen() {
        let r = parse("1) Mom\n2) Dad")
        #expect(r.prayers.count == 2)
        #expect(r.prayers[0].title == "Mom")
    }

    @Test func bulletedList() {
        let r = parse("- Mom\n- Dad\n* Sister\n\u{2022} Brother")
        #expect(r.prayers.count == 4)
        #expect(r.prayers[0].title == "Mom")
        #expect(r.prayers[2].title == "Sister")
        #expect(r.prayers[3].title == "Brother")
    }

    @Test func mixedMarkers() {
        let r = parse("1. Alpha\n- Beta\n2. Gamma")
        #expect(r.prayers.map(\.title) == ["Alpha", "Beta", "Gamma"])
    }

    // MARK: line endings
    @Test func crLfLineEndings() {
        #expect(parse("1. Mom\r\n2. Dad").prayers.map(\.title) == ["Mom", "Dad"])
    }

    @Test func crOnlyLineEndings() {
        #expect(parse("1. Mom\r2. Dad").prayers.map(\.title) == ["Mom", "Dad"])
    }

    // MARK: quotes and whitespace
    @Test func smartQuotesNormalized() {
        let r = parse("1. Mom\u{2019}s surgery\n2. \u{201C}Dad\u{201D}")
        #expect(r.prayers[0].title == "Mom's surgery")
        #expect(r.prayers[1].title == "\"Dad\"")
    }

    @Test func trimsAndCollapsesWhitespace() {
        let r = parse("1.   Mom    surgery   \n2.\tDad job")
        #expect(r.prayers[0].title == "Mom surgery")
        #expect(r.prayers[1].title == "Dad job")
    }

    // MARK: single-paragraph fallback
    @Test func noMarkersLongLineSynthesizesTitle() {
        let input = "Please pray for my family during this difficult time."
        let r = parse(input)
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Please pray for my family")
        #expect(r.prayers[0].notes == input)
    }

    @Test func onlyMarkersNoContentFallsBackToSingleParagraph() {
        #expect(parse("1.\n2.\n3.").prayers.count == 1)
    }

    // MARK: title cap
    @Test func titleOver100CharsOverflowsIntoNotes() {
        let r = parse("1. " + String(repeating: "a", count: 150))
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title.count == 100)
        #expect(r.prayers[0].notes?.count == 50)
    }

    @Test func titleExactly100CharsNoOverflow() {
        let r = parse("1. " + String(repeating: "a", count: 100))
        #expect(r.prayers[0].title.count == 100)
        #expect(r.prayers[0].notes == nil)
    }

    @Test func titleOneOverMaxOverflowsOneChar() {
        let r = parse(String(repeating: "a", count: 101))
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title.count == 100)
        #expect(r.prayers[0].notes?.count == 1)
    }

    @Test func titleAtMaxKeepsWholeText() {
        let r = parse(String(repeating: "a", count: 100))
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title.count == 100)
        #expect(r.prayers[0].notes == nil)
    }

    // MARK: suggested card title
    @Test func suggestedCardTitleIsImportedMonthDay() {
        #expect(parse("1. Mom").suggestedCardTitle == "Imported May 1")
    }

    // MARK: ALL-CAPS header
    @Test func allCapsHeaderBecomesCardTitleAndIsExcluded() {
        let r = parse("MISSIONS OUTREACH\nPray for the missionaries.")
        #expect(r.suggestedCardTitle == "Missions Outreach")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Pray for the missionaries.")
    }

    @Test func twoConsecutiveAllCapsHeadersUsesLast() {
        let r = parse("CORPORATE PRAYER\nMISSIONS OUTREACH\n  Pray for our Campus Outreach staff.")
        #expect(r.suggestedCardTitle == "Missions Outreach")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title.hasPrefix("Pray for our Campus"))
    }

    @Test func noHeaderFallsBackToImportedMonthDay() {
        let r = parse("Mom needs prayer.\nDad's surgery on Friday.")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.count == 2)
    }

    @Test func allCapsListWithNoFollowUpContentIsNotAHeader() {
        let r = parse("MOM\nDAD\nSISTER")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.map(\.title) == ["MOM", "DAD", "SISTER"])
    }

    @Test func headerWithListMarkerIsNotAHeader() {
        let r = parse("1. MISSIONS OUTREACH\nPray for staff.")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.count == 2)
        #expect(r.prayers[0].title == "MISSIONS OUTREACH")
    }

    @Test func veryShortAllCapsLineIsNotAHeader() {
        let r = parse("OK\nPray for us.")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.count == 2)
    }

    @Test func longAllCapsLineIsNotAHeader() {
        let r = parse("PLEASE PRAY FOR ALL OUR MISSIONARIES SERVING IN MANY DIFFERENT COUNTRIES THIS WEEK\nDetails follow.")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.count == 2)
    }

    @Test func headerWithBlankLineBeforeContentStillDetects() {
        let r = parse("MISSIONS OUTREACH\n\nPray for staff.")
        #expect(r.suggestedCardTitle == "Missions Outreach")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Pray for staff.")
    }

    @Test func headerWithEmbeddedDigitsStillDetects() {
        let r = parse("WEEK 2 PRAYERS\nPray for the team.")
        #expect(r.suggestedCardTitle == "Week 2 Prayers")
        #expect(r.prayers.count == 1)
    }

    @Test func onlyHeaderNoContentKeepsLineAsPrayer() {
        let r = parse("MISSIONS OUTREACH")
        #expect(r.suggestedCardTitle == "Imported May 1")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "MISSIONS OUTREACH")
    }

    @Test func headerWithAcronymPreservesShortUppercaseTokens() {
        #expect(parse("NPM PRAYER NIGHT\nPray for the team.").suggestedCardTitle == "NPM Prayer Night")
    }

    @Test func headerWithMultipleAcronymsPreservesAll() {
        #expect(parse("FBC NPM RETREAT\nDetails below.").suggestedCardTitle == "FBC NPM Retreat")
    }

    @Test func headerWithFourCharTokenTitleCasesIt() {
        #expect(parse("BABY DEDICATION\nPray for the family.").suggestedCardTitle == "Baby Dedication")
    }

    // MARK: rule 6
    @Test func shortLineNoDelimiterKeepsFullLineAsTitle() {
        let r = parse("1. Pray for Mom's surgery on Friday")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Pray for Mom's surgery on Friday")
        #expect(r.prayers[0].notes == nil)
    }

    @Test func longLineNoDelimiterTakesFirstFiveWords() {
        let r = parse("1. Pray for Mom's surgery on Friday this week")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Pray for Mom's surgery on")
        #expect(r.prayers[0].notes == "Pray for Mom's surgery on Friday this week")
    }

    @Test func lineWithCommaSplitsAtComma() {
        let r = parse("1. Sis is graduating from college this weekend, please pray")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Sis is graduating from college this weekend")
        #expect(r.prayers[0].notes == "Sis is graduating from college this weekend, please pray")
    }

    @Test func lineWithColonSplitsAtColon() {
        let r = parse("1. Aunt Susan: hip replacement next month")
        #expect(r.prayers[0].title == "Aunt Susan")
        #expect(r.prayers[0].notes == "Aunt Susan: hip replacement next month")
    }

    @Test func lineWithEmDashSplitsAtEmDash() {
        let r = parse("1. Mom \u{2014} surgery on Friday")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Mom")
        #expect(r.prayers[0].notes == "Mom \u{2014} surgery on Friday")
    }

    @Test func veryShortLineKeepsAsTitle() {
        let r = parse("1. Quick prayer")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Quick prayer")
        #expect(r.prayers[0].notes == nil)
    }

    // MARK: blank-line blocks
    @Test func blankLineSeparatedNameAndDetailsProducesOnePrayerPerBlock() {
        let r = parse(
            "Jim\nLooking for a new job, praying for interviews this week.\n\n"
                + "Frank\nWife is due this week with their third child.\n\n"
                + "John\nRecovering from surgery, please pray for healing.")
        #expect(r.prayers.count == 3)
        #expect(r.prayers[0].title == "Jim")
        #expect(r.prayers[0].notes == "Looking for a new job, praying for interviews this week.")
        #expect(r.prayers[1].title == "Frank")
        #expect(r.prayers[1].notes == "Wife is due this week with their third child.")
        #expect(r.prayers[2].title == "John")
        #expect(r.prayers[2].notes == "Recovering from surgery, please pray for healing.")
    }

    @Test func blankLineBlocksPreserveParagraphBreaksInNotes() {
        let r = parse("Frank\nWife is due this week with their third child.\nThey are nervous and tired.")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Frank")
        #expect(r.prayers[0].notes == "Wife is due this week with their third child.\nThey are nervous and tired.")
    }

    @Test func allCapsListNoBlankLinesStillOnePerLine() {
        #expect(parse("MOM\nDAD\nSISTER").prayers.map(\.title) == ["MOM", "DAD", "SISTER"])
    }

    @Test func twoLineBlockShortSecondLineDoesNotFold() {
        #expect(parse("Mom\nDad").prayers.map(\.title) == ["Mom", "Dad"])
    }

    @Test func twoLineBlockLongSecondLineFolds() {
        let r = parse("Frank\nWife is due this week with their third child.")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Frank")
        #expect(r.prayers[0].notes == "Wife is due this week with their third child.")
    }

    @Test func twoLineBlockSecondLineWithClauseDelimiterFolds() {
        let r = parse("Frank\nWife is due, third child")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Frank")
        #expect(r.prayers[0].notes == "Wife is due, third child")
    }

    @Test func allCapsHeaderInFirstBlockStillExtracted() {
        let r = parse("MISSIONS OUTREACH\n\nPray for staff.")
        #expect(r.suggestedCardTitle == "Missions Outreach")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Pray for staff.")
    }

    @Test func trailingBlankLinesIgnored() {
        let r = parse("Jim\nDetails about Jim's situation, lots of words here.\n\n\n")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Jim")
        #expect(r.prayers[0].notes == "Details about Jim's situation, lots of words here.")
    }

    @Test func leadingBlankLinesIgnored() {
        let r = parse("\n\nJim\nDetails about Jim's situation, lots of words here.")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Jim")
        #expect(r.prayers[0].notes == "Details about Jim's situation, lots of words here.")
    }

    // MARK: share-import list regression
    @Test func shareImportThreeBlocksTitlePlusDetails() {
        let r = parse(
            "Jim\nLooking for a new job, praying for interviews this week.\n\n"
                + "Frank\nWife is due this week with their third child. Praying for safe delivery!\n\n"
                + "John\nWork has been so busy he hasn't been focused on his family. He needs strength to work toward better work/life balance.")
        #expect(r.prayers.count == 3)
        #expect(r.prayers[0].title == "Jim")
        #expect(r.prayers[0].notes == "Looking for a new job, praying for interviews this week.")
        #expect(r.prayers[1].title == "Frank")
        #expect(r.prayers[1].notes == "Wife is due this week with their third child. Praying for safe delivery!")
        #expect(r.prayers[2].title == "John")
        #expect(
            r.prayers[2].notes
                == "Work has been so busy he hasn't been focused on his family. He needs strength to work toward better work/life balance.")
    }

    @Test func shareImportContiguousNumberedListSentenceShapedItems() throws {
        let r = parse(
            "\n\n"
                + "1. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Aliquam pharetra ipsum in metus finibus pulvinar. Nunc ac elementum elit. Sed consequat lorem ante, eu gravida.\n"
                + "2. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nulla quis arcu eleifend, accumsan felis vel, porta erat. Nulla molestie arcu eu nisl tristique, sed porta.")
        #expect(r.prayers.count == 2)
        #expect(r.prayers[0].title.hasPrefix("Lorem ipsum dolor sit amet"))
        #expect(try #require(r.prayers[0].notes).hasPrefix("Lorem ipsum dolor sit amet, consectetur adipiscing elit."))
        #expect(!(try #require(r.prayers[0].notes)).contains("1."))
        #expect(r.prayers[1].title.hasPrefix("Lorem ipsum dolor sit amet"))
        #expect(try #require(r.prayers[1].notes).hasPrefix("Lorem ipsum dolor sit amet, consectetur adipiscing elit."))
        #expect(!(try #require(r.prayers[1].notes)).contains("2."))
    }

    @Test func shareImportNumberedListLine2OverSixWords() {
        let r = parse("1. Mom\n2. James is interviewing on Tuesday this week\n3. Youth retreat")
        #expect(r.prayers.count == 3)
        #expect(r.prayers[0].title == "Mom")
        #expect(r.prayers[1].title == "James is interviewing on Tuesday")
        #expect(r.prayers[1].notes == "James is interviewing on Tuesday this week")
        #expect(r.prayers[2].title == "Youth retreat")
    }

    @Test func shareImportNumberedListLine2HasComma() {
        let r = parse("1. Aunt Mary, surgery\n2. James, job\n3. Retreat")
        #expect(r.prayers.count == 3)
        #expect(r.prayers[0].title == "Aunt Mary")
        #expect(r.prayers[0].notes == "Aunt Mary, surgery")
        #expect(r.prayers[1].title == "James")
        #expect(r.prayers[1].notes == "James, job")
        #expect(r.prayers[2].title == "Retreat")
    }

    @Test func shareImportMixedOrderedAndUnordered() {
        let r = parse("1. foo\n- bar\n2. baz")
        #expect(r.prayers.map(\.title) == ["foo", "bar", "baz"])
    }

    @Test func twoLineBlockLine1MarkerLine2SentenceShapedIsPerLine() {
        let r = parse("1. Mom\n2. James is interviewing on Tuesday this week")
        #expect(r.prayers.count == 2)
        #expect(r.prayers[0].title == "Mom")
        #expect(r.prayers[1].title == "James is interviewing on Tuesday")
    }

    @Test func titleDetailsFoldWithTrailingBulletStillFolds() throws {
        let r = parse("Frank\nWife is due this week with their third child.\n- praise for safe delivery")
        #expect(r.prayers.count == 1)
        #expect(r.prayers[0].title == "Frank")
        let notes = try #require(r.prayers[0].notes)
        #expect(notes.contains("Wife is due this week"))
        #expect(notes.contains("praise for safe delivery"))
    }
}
