import Foundation
import Testing
@testable import PrayerKit

private func repoRoot() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url
}

private func counts(of file: String) throws -> RowCounts {
    let source = repoRoot().appendingPathComponent("contracts/db/\(file)")
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("pk_rowcounts_\(UUID().uuidString).db").path
    try FileManager.default.copyItem(atPath: source.path, toPath: path)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }
    let database = try PrayerDatabase(path: path)
    defer { try? database.close() }
    return try database.rowCounts()
}

@Test func longUpgradedFixtureCounts() throws {
    #expect(try counts(of: "long-upgraded.db") == RowCounts(cards: 4, prayers: 7, tags: 6, collections: 2))
}

@Test func freshInstallFixtureCounts() throws {
    #expect(try counts(of: "fresh-install.db") == RowCounts(cards: 1, prayers: 0, tags: 1, collections: 2))
}
