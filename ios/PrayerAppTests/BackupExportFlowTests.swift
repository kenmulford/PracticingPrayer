import Foundation
import PrayerKit
import Testing

@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

private struct Harness {
    let directory: URL
    let exportDirectory: URL
    let flow: BackupExportFlow
    let database: PrayerDatabase

    init(protectCard: Bool = false, exportDirectory customExport: URL? = nil) throws {
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("BackupExportFlowTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let live = directory.appendingPathComponent("live.db")
        try FileManager.default.copyItem(at: fixtureURL, to: live)
        database = try PrayerDatabase(path: live.path)
        if protectCard {
            try database.dbQueue.write { db in
                try db.execute(sql: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 2")
            }
        }
        exportDirectory = customExport ?? directory.appendingPathComponent("exports", isDirectory: true)
        flow = BackupExportFlow(database: database, exportDirectory: exportDirectory)
    }

    func exportedFiles() -> [String] {
        (try? FileManager.default.contentsOfDirectory(atPath: exportDirectory.path)) ?? []
    }
}

@Test func noProtectedCardsGoesStraightToSharing() async throws {
    let harness = try Harness()
    await harness.flow.backUpNow()
    guard case .sharing(let url) = harness.flow.phase else {
        Issue.record("expected .sharing, got \(harness.flow.phase)")
        return
    }
    #expect(url.pathExtension == "pcrd")
}

@Test func protectedCardsAskFirst() async throws {
    let harness = try Harness(protectCard: true)
    await harness.flow.backUpNow()
    #expect(harness.flow.phase == .choosing)
    #expect(harness.exportedFiles().isEmpty)
}

@Test func cancelWritesNothing() async throws {
    let harness = try Harness(protectCard: true)
    await harness.flow.backUpNow()
    harness.flow.cancel()
    #expect(harness.flow.phase == .idle)
    #expect(harness.flow.isBusy == false)
    #expect(harness.exportedFiles().isEmpty)
}

@Test func failureShowsAlert() async throws {
    let blocker = FileManager.default.temporaryDirectory
        .appendingPathComponent("BackupExportFlowTests-file-\(UUID().uuidString)")
    try Data([1]).write(to: blocker)
    defer { try? FileManager.default.removeItem(at: blocker) }
    let harness = try Harness(protectCard: true, exportDirectory: blocker)
    await harness.flow.continueUnencrypted()
    #expect(harness.flow.phase == .idle)
    #expect(harness.flow.showsFailure)
}

@Test func finishSharingDeletesTheExport() async throws {
    let harness = try Harness()
    await harness.flow.backUpNow()
    guard case .sharing(let url) = harness.flow.phase else {
        Issue.record("expected .sharing")
        return
    }
    #expect(FileManager.default.fileExists(atPath: url.path))
    harness.flow.finishSharing()
    #expect(!FileManager.default.fileExists(atPath: url.path))
    #expect(harness.flow.phase == .idle)
}

@Test func shortPassphraseFailsWithoutWritingAFile() async throws {
    let harness = try Harness(protectCard: true)
    await harness.flow.protect(passphrase: "short")
    #expect(harness.flow.showsFailure)
    #expect(harness.exportedFiles().isEmpty)
}
