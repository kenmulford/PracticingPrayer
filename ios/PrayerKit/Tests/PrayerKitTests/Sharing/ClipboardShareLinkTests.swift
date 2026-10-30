import Foundation
import Testing
@testable import PrayerKit

@Suite struct ClipboardShareLinkTests {
    private let base = "https://practicingprayerapp.com/share"

    @Test func cutsAtFirstWhitespace() {
        let url = ClipboardShareLink.importableURL(in: "  \(base)/r?v=2&d=abc\nPray")
        #expect(url?.absoluteString == "\(base)/r?v=2&d=abc")
    }

    @Test func rejectsNonShareText() {
        #expect(ClipboardShareLink.importableURL(in: nil) == nil)
        #expect(ClipboardShareLink.importableURL(in: "") == nil)
        #expect(ClipboardShareLink.importableURL(in: "hello") == nil)
        #expect(ClipboardShareLink.importableURL(in: "\(base)/x?d=abc") == nil)
        #expect(ClipboardShareLink.importableURL(in: "\(base)/r") == nil)
        #expect(ClipboardShareLink.importableURL(in: "https://example.com/share/r?d=abc") == nil)
    }

    @Test func cardWithTitleAndRequests() {
        #expect(ClipboardShareLink.importableURL(in: "\(base)/c?title=a&requests=b") != nil)
    }
}
