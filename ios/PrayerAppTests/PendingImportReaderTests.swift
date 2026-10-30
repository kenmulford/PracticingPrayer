import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let goldenURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/app-group/pending-import.golden.json")

@Suite struct PendingImportReaderTests {
    private func withContainer(_ body: (URL, PendingImportReader) throws -> Void) throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("PendingImportReaderTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        try body(dir, PendingImportReader(containerURL: { dir }))
    }

    private func payloadURL(_ dir: URL) -> URL { dir.appendingPathComponent("pending-import.json") }
    private func logURL(_ dir: URL) -> URL { dir.appendingPathComponent("import-log.txt") }

    private func logLines(_ dir: URL) throws -> [String] {
        try String(contentsOf: logURL(dir), encoding: .utf8).split(separator: "\n").map(String.init)
    }

    private func seedGolden(_ dir: URL) throws {
        try FileManager.default.copyItem(at: goldenURL, to: payloadURL(dir))
    }

    @Test func goldenFilePicksUpDeletesAndLogsOk() throws {
        try withContainer { dir, reader in
            try seedGolden(dir)
            #expect(reader.takePending() == "Pray for Jos\u{00E9}\nPray for Mar\u{00ED}a")
            #expect(!FileManager.default.fileExists(atPath: payloadURL(dir).path))
            let last = try #require(try logLines(dir).last)
            #expect(last.range(of: #"^\S+Z 74 ok$"#, options: .regularExpression) != nil)
        }
    }

    @Test func oldTimestampStillPicksUp() throws {
        try withContainer { dir, reader in
            try Data(#"{"raw":"Pray","ts":"2020-01-01T00:00:00.000Z"}"#.utf8).write(to: payloadURL(dir))
            #expect(reader.takePending() == "Pray")
        }
    }

    @Test func noFileDoesNothing() throws {
        try withContainer { dir, reader in
            #expect(reader.takePending() == nil)
            #expect(!FileManager.default.fileExists(atPath: logURL(dir).path))
        }
    }

    @Test func nilContainerDoesNothing() {
        #expect(PendingImportReader(containerURL: { nil }).takePending() == nil)
    }

    @Test func whitespaceRawLogsEmpty() throws {
        try withContainer { dir, reader in
            try Data(#"{"raw":"  \n","ts":"x"}"#.utf8).write(to: payloadURL(dir))
            #expect(reader.takePending() == nil)
            #expect(!FileManager.default.fileExists(atPath: payloadURL(dir).path))
            #expect(try #require(try logLines(dir).last).hasSuffix(" empty"))
        }
    }

    @Test func invalidJsonLogsParseFail() throws {
        try withContainer { dir, reader in
            try Data("not json".utf8).write(to: payloadURL(dir))
            #expect(reader.takePending() == nil)
            #expect(!FileManager.default.fileExists(atPath: payloadURL(dir).path))
            #expect(try #require(try logLines(dir).last).hasSuffix(" parse-fail"))
        }
    }

    @Test func unreadableFileLogsIoFail() throws {
        try withContainer { dir, reader in
            try FileManager.default.createDirectory(at: payloadURL(dir), withIntermediateDirectories: false)
            #expect(reader.takePending() == nil)
            #expect(!FileManager.default.fileExists(atPath: payloadURL(dir).path))
            #expect(try #require(try logLines(dir).last).hasSuffix("- io-fail"))
        }
    }

    @Test func trimsLogBeforeAppend() throws {
        try withContainer { dir, reader in
            let seeded = (0..<250).map { "2026-01-01T00:00:00.000Z 1 ok\($0)" }.joined(separator: "\n") + "\n"
            try seeded.write(to: logURL(dir), atomically: true, encoding: .utf8)
            try seedGolden(dir)
            _ = reader.takePending()
            let lines = try logLines(dir)
            #expect(lines.count == 201)
            #expect(lines.first == "2026-01-01T00:00:00.000Z 1 ok50")
        }
    }

    @Test func pickupLeavesNoClaimedFileAndAFreshWriteSurvives() throws {
        try withContainer { dir, reader in
            try seedGolden(dir)
            _ = reader.takePending()
            #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).sorted() == ["import-log.txt"])
            try Data(#"{"raw":"Second","ts":"x"}"#.utf8).write(to: payloadURL(dir))
            #expect(reader.takePending() == "Second")
        }
    }

    @Test func secondPickupFindsNothing() throws {
        try withContainer { dir, reader in
            try seedGolden(dir)
            _ = reader.takePending()
            let before = try logLines(dir).count
            #expect(reader.takePending() == nil)
            #expect(try logLines(dir).count == before)
        }
    }

    @Test func pickUpBuildsCardPayload() throws {
        try withContainer { dir, reader in
            try seedGolden(dir)
            let now = Date(timeIntervalSince1970: 1_777_809_600) // 2026-05-03 12:00 UTC
            let pending = try #require(
                PendingImport.pickUp(reader: reader, now: now, locale: Locale(identifier: "en_US")))
            #expect(
                pending.payload
                    == .card(
                        title: "Imported May 3",
                        requests: [
                            SharedPrayer(title: "Pray for Jos\u{00E9}", notes: nil),
                            SharedPrayer(title: "Pray for Mar\u{00ED}a", notes: nil),
                        ]))
            #expect(pending.cardTitle == "Imported May 3")
        }
    }
}
