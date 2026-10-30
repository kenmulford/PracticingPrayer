import Foundation
import Testing
@testable import PrayerKit

@Suite struct InboundShareRouteTests {
    private func route(_ string: String) -> InboundShareRoute {
        InboundShareRoute(url: URL(string: string)!)
    }

    @Test func requestPath() {
        #expect(route("https://practicingprayerapp.com/share/r?v=2&d=x") == .link(.request))
    }

    @Test func cardPathTrailingSlash() {
        #expect(route("https://practicingprayerapp.com/share/c/?v=2&d=x") == .link(.card))
    }

    @Test func uppercaseHost() {
        #expect(route("https://PracticingPrayerApp.COM/share/r") == .link(.request))
    }

    @Test func prayercardUppercaseExtension() {
        #expect(route("file:///tmp/Friends.PRAYERCARD") == .file)
    }

    @Test func foreignHost() {
        #expect(route("https://example.com/share/r?title=x") == .ignored)
    }

    @Test func unknownPath() {
        #expect(route("https://practicingprayerapp.com/about") == .ignored)
    }

    @Test func appScheme() {
        #expect(route("practicingprayer://share/r") == .ignored)
    }
}
