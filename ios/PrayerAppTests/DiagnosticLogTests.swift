import Foundation
import Testing
@testable import PrayerApp

struct DiagnosticLogTests {
    private struct SampleError: Error {}

    private func freshDirectory() throws -> URL {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("DiagnosticLogTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    @Test func messageEntryAppends() throws {
        let directory = try freshDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let log = DiagnosticLog(directory: directory)

        log.log(category: "Test", message: "hello")

        let text = try String(contentsOf: log.logURL, encoding: .utf8)
        #expect(text.split(separator: "\n").count == 1)
        #expect(text.range(of: #"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[Test\] hello"#, options: .regularExpression) != nil)
    }

    @Test func errorEntryEndsWithDelimiter() throws {
        let directory = try freshDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let log = DiagnosticLog(directory: directory)

        log.log(category: "Test", error: SampleError())

        let text = try String(contentsOf: log.logURL, encoding: .utf8)
        #expect(text.hasSuffix("---\n"))
        #expect(text.contains("[Test] SampleError"))
    }

    @Test func trimKeepsLastHundred() throws {
        let directory = try freshDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let log = DiagnosticLog(directory: directory)
        for index in 1...105 {
            log.log(category: "Entry\(index)", error: SampleError())
        }

        log.trim()

        let text = try String(contentsOf: log.logURL, encoding: .utf8)
        let entries = text.components(separatedBy: "---\n").filter { !$0.isEmpty }
        #expect(entries.count == DiagnosticLog.maxEntries)
        #expect(entries[0].contains("[Entry6] "))
        #expect(entries[99].contains("[Entry105] "))
    }

    @Test func missingFileHasNoEntries() throws {
        let directory = try freshDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let log = DiagnosticLog(directory: directory)

        #expect(!log.hasEntries)
        log.trim()
        #expect(!FileManager.default.fileExists(atPath: log.logURL.path))
    }

    @Test func unreadableLogHasNoEntries() throws {
        let directory = try freshDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let log = DiagnosticLog(directory: directory)
        try FileManager.default.createDirectory(at: log.logURL, withIntermediateDirectories: false)

        #expect(!log.hasEntries)
        log.trim()
        #expect(!log.hasEntries)
    }
}
