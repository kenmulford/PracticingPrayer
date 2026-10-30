import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let shareDirectory = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/share")

private func fixtureLink(_ name: String) throws -> URL {
    let text = try String(contentsOf: shareDirectory.appendingPathComponent(name), encoding: .utf8)
    return try #require(URL(string: text.trimmingCharacters(in: .whitespacesAndNewlines)))
}

private struct Unreadable: Error {}

@Suite struct InboundShareHandlerTests {
    private func head(_ handler: InboundShareHandler) -> InboundShareHandler.Content? {
        handler.queue.first?.content
    }

    @Test func requestLinkQueuesPayload() throws {
        let handler = InboundShareHandler()
        handler.handle(try fixtureLink("request.url"))
        #expect(head(handler) == .payload(.request(SharedPrayer(
            title: "Heal my friend's knee", notes: "Surgery next week; pray for \"steady hands\""))))
        #expect(handler.isDeepLinkSession)
    }

    @Test func cardLinkQueuesCard() throws {
        let handler = InboundShareHandler()
        handler.handle(try fixtureLink("card.url"))
        guard case .payload(.card) = head(handler) else {
            Issue.record("expected a card payload, got \(String(describing: head(handler)))")
            return
        }
    }

    @Test func legacyQueryLink() throws {
        let handler = InboundShareHandler()
        handler.handle(try fixtureLink("legacy-request.url"))
        guard case .payload = head(handler) else {
            Issue.record("expected a payload, got \(String(describing: head(handler)))")
            return
        }
    }

    @Test func prayercardFile() throws {
        let handler = InboundShareHandler()
        handler.handle(shareDirectory.appendingPathComponent("oversize.prayercard"))
        guard case .payload(.card) = head(handler) else {
            Issue.record("expected a card payload, got \(String(describing: head(handler)))")
            return
        }
        #expect(!handler.isDeepLinkSession)
    }

    @Test func corruptFile() {
        let handler = InboundShareHandler(readFile: { _ in Data("not gzip".utf8) })
        handler.handle(URL(fileURLWithPath: "/tmp/bad.prayercard"))
        #expect(head(handler) == .failure(.invalidFile))
    }

    @Test func emptyFile() {
        let handler = InboundShareHandler(readFile: { _ in Data() })
        handler.handle(URL(fileURLWithPath: "/tmp/empty.prayercard"))
        #expect(head(handler) == .failure(.invalidFile))
    }

    @Test func unreadableFile() {
        let handler = InboundShareHandler(readFile: { _ in throw Unreadable() })
        handler.handle(URL(fileURLWithPath: "/tmp/locked.prayercard"))
        #expect(head(handler) == .failure(.invalidFile))
    }

    @Test func invalidRequestLink() throws {
        let handler = InboundShareHandler()
        handler.handle(try #require(URL(string: "https://practicingprayerapp.com/share/r?v=2&d=z.AAAA")))
        #expect(head(handler) == .failure(.invalidRequestLink))
    }

    @Test func invalidCardLink() throws {
        let handler = InboundShareHandler()
        handler.handle(try #require(URL(string: "https://practicingprayerapp.com/share/c?v=2&d=z.AAAA")))
        #expect(head(handler) == .failure(.invalidCardLink))
    }

    @Test func newerVersionLink() throws {
        let handler = InboundShareHandler()
        handler.handle(try #require(URL(string: "https://practicingprayerapp.com/share/r?v=3&d=x")))
        #expect(head(handler) == .failure(.newerVersion))
    }

    @Test func emptyPayload() throws {
        let encoding = try ShareCodec.encode(.request(SharedPrayer(title: " ", notes: nil)))
        guard case .link(let link) = encoding else {
            Issue.record("expected a link encoding")
            return
        }
        let handler = InboundShareHandler()
        handler.handle(try #require(URL(string: link)))
        #expect(handler.queue.isEmpty)
    }

    @Test func foreignLink() throws {
        let handler = InboundShareHandler()
        handler.handle(try #require(URL(string: "https://example.com/share/r?title=x")))
        #expect(handler.queue.isEmpty)
        #expect(!handler.isDeepLinkSession)
    }

    @Test func browsingActivity() throws {
        let handler = InboundShareHandler()
        let activity = NSUserActivity(activityType: NSUserActivityTypeBrowsingWeb)
        activity.webpageURL = try fixtureLink("request.url")
        handler.handle(userActivity: activity)
        #expect(handler.queue.count == 1)
    }

    @Test func queueIsFifo() throws {
        let handler = InboundShareHandler()
        handler.handle(try fixtureLink("request.url"))
        handler.handle(try fixtureLink("card.url"))
        #expect(handler.queue.count == 2)
        handler.dismissHead()
        guard case .payload(.card) = head(handler) else {
            Issue.record("expected the card to be the head")
            return
        }
    }

    @Test func fileOverTheCapIsRejectedWithoutReadingItAll() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("cap-\(UUID().uuidString).prayercard")
        try Data(count: ShareCodec.maxFileBytes + 1).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        #expect(throws: (any Error).self) { try InboundShareHandler.readCapped(url) }
        #expect(try InboundShareHandler.readCapped(shareDirectory.appendingPathComponent("oversize.prayercard")).count <= ShareCodec.maxFileBytes)
    }

    @Test func suggestedTitleTravelsWithThePayload() {
        let handler = InboundShareHandler()
        handler.enqueue(.card(title: "Imported May 3", requests: []), cardTitle: "Imported May 3")
        #expect(handler.queue.first?.cardTitle == "Imported May 3")
    }
}
