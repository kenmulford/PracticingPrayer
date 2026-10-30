import Foundation
import Testing
@testable import PrayerKit

struct ImportBreadcrumbLogTests {
    private static let fixedNow = Date(timeIntervalSince1970: 1_777_809_600) // 2026-05-03T12:00:00Z

    private func makeContainer() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("breadcrumb-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func contents(_ container: URL) throws -> String {
        try String(contentsOf: container.appendingPathComponent(ImportBreadcrumbLog.fileName), encoding: .ascii)
    }

    @Test func lineFormatWithoutCount() throws {
        let container = try makeContainer()
        ImportBreadcrumbLog(containerURL: container).append(.noAttachment, byteCount: nil, now: Self.fixedNow)
        #expect(try contents(container) == "2026-05-03T12:00:00.000Z - no-attachment\n")
    }

    @Test func lineFormatWithCount() throws {
        let container = try makeContainer()
        ImportBreadcrumbLog(containerURL: container).append(.writeOk, byteCount: 73, now: Self.fixedNow)
        #expect(try contents(container) == "2026-05-03T12:00:00.000Z 73 write-ok\n")
    }

    @Test func appendsDoNotTruncate() throws {
        let container = try makeContainer()
        let log = ImportBreadcrumbLog(containerURL: container)
        log.append(.ioFail, byteCount: nil, now: Self.fixedNow)
        log.append(.writeOk, byteCount: 5, now: Self.fixedNow)
        #expect(try contents(container).split(separator: "\n").count == 2)
    }

    @Test func missingContainerIsSilent() {
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("missing-\(UUID().uuidString)")
        ImportBreadcrumbLog(containerURL: missing).append(.writeOk, byteCount: 1, now: Self.fixedNow)
        #expect(!FileManager.default.fileExists(atPath: missing.path))
    }
}
