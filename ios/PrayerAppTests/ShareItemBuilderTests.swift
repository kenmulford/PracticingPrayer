import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private func prayer(_ id: Int64, _ title: String, details: String? = nil, answered: Bool = false) -> PrayerRequestRecord {
    PrayerRequestRecord(
        id: id, prayerCardId: 1, title: title, details: details, canNotify: false, prayerFrequency: .daily,
        isAnswered: answered, answeredAtTicks: nil, notifyHour: nil, notifyMinute: nil, notifyDayOfWeek: nil,
        notifyDayOfMonth: nil, notifyDateTicks: nil, isImported: nil, createdAtTicks: 0, updatedAtTicks: 0)
}

private func card(title: String = "Family", mode: CardProtectionMode? = nil) -> PrayerCardRecord {
    PrayerCardRecord(
        id: 1, title: title, canNotify: false, prayerFrequency: .daily, isAnswered: false, isFavorite: false,
        isSystem: nil, isImported: nil, protectionMode: mode, boxId: nil, preArchiveBoxId: nil, systemKey: nil,
        createdAtTicks: 0, updatedAtTicks: 0)
}

private let footer = "(Shared via Practicing Prayer)"

private func text(_ item: ShareItem?) -> String {
    if case .text(let value)? = item?.content { return value }
    return ""
}

@Suite struct ShareItemBuilderTests {
    @Test func smallPrayerSharesText() throws {
        let item = try ShareItemBuilder.prayer(prayer(1, "T", details: "D"))
        let body = text(item)
        let link = try #require(body.components(separatedBy: "\n\n").first)
        #expect(link.hasPrefix("https://practicingprayerapp.com/share/r?v=2&d=z."))
        #expect(body == "\(link)\n\nT\nD\n\n\(footer)")
        #expect(item.title == "T")
    }

    @Test func smartQuotesNormalized() throws {
        let body = text(try ShareItemBuilder.prayer(prayer(1, "God\u{2019}s \u{201C}plan\u{201D}", details: "Mom\u{2019}s")))
        #expect(body.contains("\nGod's \"plan\"\nMom's\n"))
    }

    @Test func smallCardCountsActiveNonBlank() throws {
        let prayers = [
            prayer(1, "One"), prayer(2, "Two"), prayer(3, "Done", answered: true), prayer(4, "   "),
        ]
        let item = try #require(try ShareItemBuilder.card(card(), prayers: prayers))
        let body = text(item)
        #expect(body.contains("/share/c?v=2&d=z."))
        #expect(body.hasSuffix("\n\nFamily\n+ 2 requests\n\n\(footer)"))
    }

    @Test func cardWithOnePrayerIsSingular() throws {
        let body = text(try ShareItemBuilder.card(card(), prayers: [prayer(1, "One")]))
        #expect(body.contains("\n+ 1 request\n"))
    }

    @Test func cardWithoutShareablePrayersIsNil() throws {
        #expect(try ShareItemBuilder.card(card(), prayers: [prayer(1, "Done", answered: true)]) == nil)
    }

    @Test func largeCardSharesFile() throws {
        var seed: UInt64 = 42
        let letters = Array("abcdefghijklmnopqrstuvwxyz0123456789")
        let prayers = (0..<80).map { index -> PrayerRequestRecord in
            let title = String((0..<40).map { _ -> Character in
                seed = seed &* 6364136223846793005 &+ 1442695040888963407
                return letters[Int(seed >> 33) % letters.count]
            })
            return prayer(Int64(index), title)
        }
        let item = try #require(try ShareItemBuilder.card(card(), prayers: prayers))
        guard case .file(let file) = item.content else {
            Issue.record("expected a file")
            return
        }
        #expect(file.title == "Family")
        guard case .payload(.card(let title, let requests)) = ShareCodec.decode(file: file.data) else {
            Issue.record("file did not decode")
            return
        }
        #expect(title == "Family")
        #expect(requests.count == 80)
    }

    @Test func writeKeepsOneFile() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("ShareItemBuilderTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        try Data([1]).write(to: directory.appendingPathComponent("old.prayercard"))

        let url = try PrayerCardFile(title: "Family", data: Data([2, 3])).write(to: directory)

        #expect(url.lastPathComponent == "Family.prayercard")
        #expect(try FileManager.default.contentsOfDirectory(atPath: directory.path) == ["Family.prayercard"])
        #expect(try Data(contentsOf: url) == Data([2, 3]))
    }

    @Test func sanitizedFileNameFallsBack() {
        #expect(ShareItemBuilder.sanitizedFileName("/:") == "shared-prayer")
        #expect(ShareItemBuilder.sanitizedFileName("a/b:c") == "abc")
    }

    @Test func protectedCardIsNotShareable() {
        #expect(!ShareItemBuilder.isShareable(card(mode: .lockedVisible), box: nil))
        #expect(!ShareItemBuilder.isShareable(card(mode: .hidden), box: nil))
        #expect(ShareItemBuilder.isShareable(card(mode: .unprotected), box: nil))
    }
}
