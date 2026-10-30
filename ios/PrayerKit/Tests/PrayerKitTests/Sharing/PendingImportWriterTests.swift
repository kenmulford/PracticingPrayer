import Foundation
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<6 { url.deleteLastPathComponent() }
    return url
}

struct PendingImportWriterTests {
    private static let fixedNow = Date(timeIntervalSince1970: 1_777_809_600) // 2026-05-03T12:00:00Z

    private func makeContainer() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("pending-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func pendingBytes(_ container: URL) throws -> Data {
        try Data(contentsOf: container.appendingPathComponent(PendingImportWriter.fileName))
    }

    private func log(_ container: URL) -> String {
        (try? String(contentsOf: container.appendingPathComponent(ImportBreadcrumbLog.fileName), encoding: .ascii)) ?? ""
    }

    @Test func matchesGolden() throws {
        let container = try makeContainer()
        let ok = PendingImportWriter(containerURL: container)
            .write("Pray for Jos\u{00E9}\nPray for Mar\u{00ED}a", now: Self.fixedNow)
        #expect(ok)
        var golden = try Data(contentsOf: repoRoot().appendingPathComponent("contracts/app-group/pending-import.golden.json"))
        #expect(golden.last == 0x0A)
        golden.removeLast()
        #expect(try pendingBytes(container) == golden)
        #expect(log(container).hasSuffix(" \(golden.count) write-ok\n"))
    }

    @Test func nfdWrittenAsNFC() throws {
        let container = try makeContainer()
        let nfd = "Jose\u{0301}"
        #expect(PendingImportWriter(containerURL: container).write(nfd, now: Self.fixedNow))
        let json = try #require(String(data: try pendingBytes(container), encoding: .utf8))
        #expect(json.unicodeScalars.contains("\u{00E9}"))
        #expect(!json.unicodeScalars.contains("\u{0301}"))
    }

    @Test func oversizedAfterNormalizationWritesNothing() throws {
        let container = try makeContainer()
        let text = String(repeating: "a", count: SharePayloadClassifier.maxPayloadBytes + 1)
        #expect(!PendingImportWriter(containerURL: container).write(text, now: Self.fixedNow))
        #expect(!FileManager.default.fileExists(atPath: container.appendingPathComponent(PendingImportWriter.fileName).path))
        #expect(log(container).hasSuffix(" - oversized\n"))
    }

    @Test func unwritableContainerLogsIoFail() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("notadir-\(UUID().uuidString)")
        try Data("x".utf8).write(to: file)
        #expect(!PendingImportWriter(containerURL: file).write("hello", now: Self.fixedNow))
    }
}
