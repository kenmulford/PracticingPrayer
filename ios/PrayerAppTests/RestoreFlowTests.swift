import Foundation
import PrayerKit
import Testing

@testable import PrayerApp

private let repoRoot = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
private let passphrase = "correct horse battery"

private struct Harness {
    let directory: URL
    let services: AppServices
    let flow: RestoreFlow

    init() async throws {
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("RestoreFlowTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let live = directory.appendingPathComponent("live.db")
        try FileManager.default.copyItem(at: repoRoot.appendingPathComponent("contracts/db/fresh-install.db"), to: live)
        services = AppServices(databasePath: live.path)
        await services.open()
        flow = RestoreFlow(services: services, importDirectory: directory.appendingPathComponent("imports"))
    }

    func protectedExport() throws -> URL {
        let source = directory.appendingPathComponent("source.db")
        try FileManager.default.copyItem(at: repoRoot.appendingPathComponent("contracts/db/long-upgraded.db"), to: source)
        let database = try PrayerDatabase(path: source.path)
        defer { try? database.close() }
        try database.dbQueue.write { db in
            try db.execute(sql: "UPDATE PrayerCard SET ProtectionMode = 1 WHERE Id = 2")
        }
        return try BackupExporter.export(
            database: database, passphrase: passphrase, to: directory.appendingPathComponent("exports"))
    }

    func hasCardTwo() throws -> Bool {
        try services.database.dbQueue.read { db in
            try Int.fetchOne(db, sql: "SELECT COUNT(*) FROM PrayerCard WHERE Id = 2") == 1
        }
    }
}

@Suite struct RestoreFlowTests {
    @Test func invalidFileFailsBeforeSwap() async throws {
        let harness = try await Harness()
        let bad = harness.directory.appendingPathComponent("bad.pcrd")
        try Data("not a backup".utf8).write(to: bad)
        let generation = harness.services.databaseGeneration

        await harness.flow.picked(.success(bad))

        #expect(harness.flow.failure == .invalidBackup)
        #expect(harness.flow.phase == .idle)
        #expect(harness.services.databaseGeneration == generation)
    }

    @Test func confidentialArchiveAsksPassphrase() async throws {
        let harness = try await Harness()
        await harness.flow.picked(.success(try harness.protectedExport()))
        #expect(harness.flow.phase == .promptingPassphrase)
    }

    @Test func validArchiveRestores() async throws {
        let harness = try await Harness()
        await harness.flow.picked(.success(repoRoot.appendingPathComponent("contracts/backup/maui-made.pcrd")))
        #expect(harness.flow.phase == .idle)
        #expect(harness.flow.failure == nil)
        #expect(harness.services.phase == .open)
    }

    @Test func wrongOrNoPassphraseRestoresMainOnly() async throws {
        let harness = try await Harness()
        await harness.flow.picked(.success(try harness.protectedExport()))
        await harness.flow.restore(passphrase: nil)
        #expect(try harness.hasCardTwo() == false)
        #expect(harness.flow.failure == nil)
        #expect(harness.flow.phase == .idle)
        #expect(harness.flow.skippedProtectedCards == false)
    }

    @Test func wrongPassphraseFlagsSkippedProtectedCards() async throws {
        let harness = try await Harness()
        await harness.flow.picked(.success(try harness.protectedExport()))
        await harness.flow.restore(passphrase: "not the right passphrase")
        #expect(try harness.hasCardTwo() == false)
        #expect(harness.flow.skippedProtectedCards)
    }

    @Test func swapHoldsRestoringPhase() async throws {
        let harness = try await Harness()
        let services = harness.services
        let swap = Task { try await services.reopenDatabase { _ in Thread.sleep(forTimeInterval: 0.5) } }
        try await Task.sleep(for: .milliseconds(150))
        #expect(services.phase == .restoring)
        try await swap.value
        #expect(services.phase == .open)
    }

    @Test func correctPassphraseRestoresConfidential() async throws {
        let harness = try await Harness()
        await harness.flow.picked(.success(try harness.protectedExport()))
        await harness.flow.restore(passphrase: passphrase)
        #expect(try harness.hasCardTwo())
        #expect(harness.flow.failure == nil)
        #expect(harness.flow.skippedProtectedCards == false)
    }
}
