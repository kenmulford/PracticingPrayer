import Foundation
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

private let shareBase = "https://practicingprayerapp.com/share"
private let fixtureVariable = "PP_WRITE_SHARE_FIXTURES"

private struct ExpectedFixture: Decodable {
    struct Request: Decodable {
        let title: String
        let notes: String?
    }

    let result: String
    let kind: String?
    let title: String?
    let requests: [Request]?

    var decodeResult: ShareDecodeResult {
        guard result == "payload", let requests else { return .newerVersion }
        let prayers = requests.map { SharedPrayer(title: $0.title, notes: $0.notes) }
        if kind == "request" { return .payload(.request(prayers[0])) }
        return .payload(.card(title: title ?? "", requests: prayers))
    }
}

private let swiftRequest = SharedPrayer(title: "Safe travel for Anna\u{2019}s trip", notes: "Flight lands \u{201C}Friday\u{201D}")
private let swiftCardTitle = "Church family"
private let swiftCardRequests = [
    SharedPrayer(title: "Pastor\u{2019}s sabbatical", notes: nil),
    SharedPrayer(title: "New roof", notes: "Estimate due \u{201C}soon\u{201D}"),
]

private func gzipped(_ json: String) throws -> Data {
    try Gzip.compress(Data(json.utf8))
}

private func link(path: String, payload: Data, prefix: String = "z.") -> String {
    "\(shareBase)/\(path)?v=2&d=\(prefix)\(ShareCodec.base64URL(payload))"
}

@Suite struct ShareCodecTests {
    static let fixtureNames = [
        "request.url", "card.url", "oversize.prayercard",
        "swift-request.url", "swift-card.url",
        "legacy-request.url", "legacy-card.url", "uncompressed.url", "trailing-junk.url", "v3.url",
    ]

    @Test(arguments: fixtureNames)
    func fixturesDecode(name: String) throws {
        let dir = repoRoot().appendingPathComponent("contracts/share")
        let input = try Data(contentsOf: dir.appendingPathComponent(name))
        let stem = (name as NSString).deletingPathExtension
        let expectedData = try Data(contentsOf: dir.appendingPathComponent("\(stem).expected.json"))
        let expected = try JSONDecoder().decode(ExpectedFixture.self, from: expectedData).decodeResult

        if name.hasSuffix(".prayercard") {
            #expect(ShareCodec.decode(file: input) == expected)
        } else {
            let text = try #require(String(data: input, encoding: .utf8))
            #expect(ShareCodec.decode(link: text.trimmingCharacters(in: .whitespacesAndNewlines)) == expected)
        }
    }

    @Test func encodeRequestRoundTrips() throws {
        let payload = SharePayload.request(SharedPrayer(title: "Mom\u{2019}s knee", notes: "Rest"))
        guard case .link(let url) = try ShareCodec.encode(payload) else {
            Issue.record("expected a link")
            return
        }
        #expect(url.hasPrefix("\(shareBase)/r?v=2&d=z."))
        #expect(ShareCodec.decode(link: url) == .payload(.request(SharedPrayer(title: "Mom's knee", notes: "Rest"))))
    }

    @Test func encodeCardRoundTrips() throws {
        let requests = [SharedPrayer(title: "One", notes: "a"), SharedPrayer(title: "Two", notes: nil)]
        guard case .link(let url) = try ShareCodec.encode(.card(title: "Card", requests: requests)) else {
            Issue.record("expected a link")
            return
        }
        #expect(url.hasPrefix("\(shareBase)/c?v=2&d=z."))
        #expect(url.count <= ShareCodec.maxLinkLength)
        #expect(ShareCodec.decode(link: url) == .payload(.card(title: "Card", requests: requests)))
    }

    @Test func oversizeCardEncodesFile() throws {
        let requests = (0..<100).map { _ in SharedPrayer(title: UUID().uuidString, notes: UUID().uuidString) }
        let encoded = try ShareCodec.encode(.card(title: "Big", requests: requests))
        guard case .file(let data) = encoded else {
            Issue.record("expected a file")
            return
        }
        #expect(ShareCodec.decode(file: data) == .payload(.card(title: "Big", requests: requests)))
    }

    @Test func nilNotesEncodeEmptyString() throws {
        let payload = SharePayload.request(SharedPrayer(title: "T", notes: nil))
        let json = String(decoding: try ShareCodec.json(for: payload), as: UTF8.self)
        #expect(json.contains("\"notes\":\"\""))
        guard case .link(let url) = try ShareCodec.encode(payload) else {
            Issue.record("expected a link")
            return
        }
        #expect(ShareCodec.decode(link: url) == .payload(.request(SharedPrayer(title: "T", notes: nil))))
    }

    @Test func invalidPayloads() throws {
        let good = try gzipped(#"{"title":"T","notes":""}"#)
        var flipped = [UInt8](good)
        flipped[flipped.count - 8] ^= 0xFF

        #expect(ShareCodec.decode(link: "\(shareBase)/r?v=2&d=A") == .invalid)
        #expect(ShareCodec.decode(link: "\(shareBase)/r?v=2&d=z.A") == .invalid)
        #expect(ShareCodec.decode(link: link(path: "r", payload: Data("hello world".utf8))) == .invalid)
        #expect(ShareCodec.decode(link: link(path: "r", payload: try gzipped("not json"))) == .invalid)
        #expect(ShareCodec.decode(link: link(path: "r", payload: Data(flipped))) == .invalid)
        #expect(ShareCodec.decode(file: Data()) == .invalid)
        #expect(ShareCodec.decode(file: Data([1, 2, 3])) == .invalid)
        #expect(ShareCodec.decode(file: Data(flipped)) == .invalid)
        #expect(ShareCodec.decode(file: try gzipped("not json")) == .invalid)
    }

    @Test func emptyPayloads() throws {
        #expect(ShareCodec.decode(file: try gzipped(#"{"title":"C","requests":[]}"#)) == .empty)
        #expect(ShareCodec.decode(file: try gzipped(#"{"title":"C","requests":[{"title":" ","notes":"x"}]}"#)) == .empty)
        #expect(ShareCodec.decode(file: try gzipped(#"{"title":"  ","notes":"x"}"#)) == .empty)
    }

    @Test func notAShareLink() {
        #expect(ShareCodec.decode(link: "\(shareBase)/x?v=2&d=abc") == .notAShareLink)
        #expect(ShareCodec.decode(link: "https://example.com/share/r?v=2&d=abc") == .notAShareLink)
        #expect(ShareCodec.decode(link: "example.com") == .notAShareLink)
        #expect(ShareCodec.decode(link: "\(shareBase)/r?v=2") == .notAShareLink)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment[fixtureVariable] != nil))
    func writeSwiftFixtures() throws {
        let path = try #require(ProcessInfo.processInfo.environment[fixtureVariable])
        let dir = URL(fileURLWithPath: path)
        for (name, payload) in [
            ("swift-request.url", SharePayload.request(swiftRequest)),
            ("swift-card.url", SharePayload.card(title: swiftCardTitle, requests: swiftCardRequests)),
        ] {
            guard case .link(let url) = try ShareCodec.encode(payload) else {
                Issue.record("expected a link for \(name)")
                continue
            }
            try Data((url + "\n").utf8).write(to: dir.appendingPathComponent(name))
        }
    }
}
