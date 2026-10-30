import Foundation
import Testing
@testable import PrayerApp

private let goldenURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/app-group/pending-import.golden.json")

@Suite struct AppGroupWriterTests {
    private struct Wire: Decodable {
        let raw: String
        let ts: String
    }

    private func withContainer(_ body: (URL, AppGroupWriter) throws -> Void) throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("AppGroupWriterTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        try body(dir, AppGroupWriter(containerURL: { dir }))
    }

    @Test func writtenFileMatchesGolden() throws {
        var golden = try Data(contentsOf: goldenURL)
        #expect(golden.last == 0x0A)
        golden.removeLast()
        let expected = try JSONDecoder().decode(Wire.self, from: golden)

        try withContainer { dir, writer in
            try writer.writePendingImport(rawText: expected.raw)
            let written = try String(contentsOf: dir.appendingPathComponent("pending-import.json"), encoding: .utf8)
            let normalized = written.replacingOccurrences(
                of: #""ts":"[^"]*""#, with: "\"ts\":\"\(expected.ts)\"", options: .regularExpression)
            #expect(Data(normalized.utf8) == golden)

            let log = try String(contentsOf: dir.appendingPathComponent("import-log.txt"), encoding: .utf8)
            #expect(log.hasSuffix(" write-ok\n"))
        }
    }

    @Test func emptyTextThrowsAndWritesNothing() throws {
        try withContainer { dir, writer in
            #expect(throws: IntentError.self) { try writer.writePendingImport(rawText: " \n\t") }
            #expect(!FileManager.default.fileExists(atPath: dir.appendingPathComponent("pending-import.json").path))
        }
    }

    @Test func unavailableContainerThrows() {
        let writer = AppGroupWriter(containerURL: { nil })
        do {
            try writer.writePendingImport(rawText: "hello")
            Issue.record("expected appGroupUnavailable")
        } catch let error as IntentError {
            guard case .appGroupUnavailable = error else {
                Issue.record("wrong error \(error)")
                return
            }
        } catch {
            Issue.record("wrong error \(error)")
        }
    }

    @Test func oversizedPayloadThrowsAndKeepsPriorFile() throws {
        try withContainer { dir, writer in
            let payloadURL = dir.appendingPathComponent("pending-import.json")
            let prior = Data("prior".utf8)
            try prior.write(to: payloadURL)

            do {
                try writer.writePendingImport(rawText: String(repeating: "a", count: 262_145))
                Issue.record("expected payloadTooLarge")
            } catch let error as IntentError {
                guard case .payloadTooLarge = error else {
                    Issue.record("wrong error \(error)")
                    return
                }
            }

            #expect(try Data(contentsOf: payloadURL) == prior)
            let log = try String(contentsOf: dir.appendingPathComponent("import-log.txt"), encoding: .utf8)
            #expect(log.hasSuffix(" - io-fail\n"))
        }
    }

    @Test func decomposedTextIsWrittenComposed() throws {
        try withContainer { dir, writer in
            try writer.writePendingImport(rawText: "e\u{0301}")
            let data = try Data(contentsOf: dir.appendingPathComponent("pending-import.json"))
            let wire = try JSONDecoder().decode(Wire.self, from: data)
            #expect(wire.raw == "\u{00E9}")
        }
    }
}
