import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private final class RecordingCenter: ReminderCenter, @unchecked Sendable {
    private let lock = NSLock()
    private var removals = 0
    var removeAllCount: Int { lock.withLock { removals } }

    func removeAllPendingRequests() async { lock.withLock { removals += 1 } }
    func add(_ request: ReminderRequest) async throws {}
}

private func isolatedPreferences() -> AppPreferences {
    AppPreferences(defaults: UserDefaults(suiteName: "AppServicesTests-\(UUID().uuidString)")!)
}

private func tempPath(_ name: String) -> String {
    FileManager.default.temporaryDirectory.appendingPathComponent("AppServicesTests-\(UUID().uuidString)-\(name)").path
}

private let upgradedFixture = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@Suite struct AppServicesTests {
    @Test func freshInstallCreatesTheDatabaseAndSeedsBoxes() async throws {
        let path = tempPath("prayer_app.db")
        defer { try? FileManager.default.removeItem(atPath: path) }
        let preferences = isolatedPreferences()
        let services = AppServices(preferences: preferences, databasePath: path, reminderCenter: RecordingCenter())

        await services.open()

        #expect(services.phase == .open)
        let keys = try await services.database.dbQueue.read { try String.fetchAll($0, sql: "SELECT SystemKey FROM CardBox ORDER BY Id") }
        #expect(keys == ["system", "archived"])
        #expect(preferences.archivedFolderId == 2)
    }

    @Test func openRunsSchemaUpkeepOnAnUpgradedDatabase() async throws {
        let path = tempPath("upgraded.db")
        try FileManager.default.copyItem(at: upgradedFixture, to: URL(fileURLWithPath: path))
        defer { try? FileManager.default.removeItem(atPath: path) }
        let services = AppServices(preferences: isolatedPreferences(), databasePath: path, reminderCenter: RecordingCenter())
        await services.open()

        let columns = try await services.database.dbQueue.read { db in try db.columns(in: "PrayerRequest").map(\.name) }
        #expect(columns.contains("NotifyDate"))
        let seeded = try await services.database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT COUNT(*) FROM CardBox WHERE SystemKey = 'archived'") }
        #expect(seeded == 1)
    }

    @Test func openSetsArchivedFolderIdFromTheArchivedBox() async throws {
        let path = tempPath("archived.db")
        try FileManager.default.copyItem(at: upgradedFixture, to: URL(fileURLWithPath: path))
        defer { try? FileManager.default.removeItem(atPath: path) }
        let preferences = isolatedPreferences()
        preferences.archivedFolderId = 0
        let services = AppServices(preferences: preferences, databasePath: path, reminderCenter: RecordingCenter())
        await services.open()

        let id = try await services.database.dbQueue.read { try Int.fetchOne($0, sql: "SELECT Id FROM CardBox WHERE SystemKey = 'archived'") }
        #expect(id != nil)
        #expect(preferences.archivedFolderId == id)
    }

    @Test func reopenRefreshesArchivedFolderId() async throws {
        let path = tempPath("reopen.db")
        try FileManager.default.copyItem(at: upgradedFixture, to: URL(fileURLWithPath: path))
        defer { try? FileManager.default.removeItem(atPath: path) }
        let preferences = isolatedPreferences()
        let services = AppServices(preferences: preferences, databasePath: path, reminderCenter: RecordingCenter())
        await services.open()
        preferences.archivedFolderId = 0

        try await services.reopenDatabase { _ in }

        #expect(preferences.archivedFolderId != 0)
    }

    @Test func openReconcilesRemindersOnce() async throws {
        let path = tempPath("reconcile.db")
        try FileManager.default.copyItem(at: upgradedFixture, to: URL(fileURLWithPath: path))
        defer { try? FileManager.default.removeItem(atPath: path) }
        let center = RecordingCenter()
        let preferences = isolatedPreferences()
        let services = AppServices(preferences: preferences, databasePath: path, reminderCenter: center)
        await services.open()

        // Wait for the launch reconcile: the first run clears MAUI-era requests and sets the migrated flag.
        for _ in 0..<200 where !preferences.nativeRemindersMigrated { try await Task.sleep(for: .milliseconds(25)) }

        #expect(preferences.nativeRemindersMigrated)
        #expect(center.removeAllCount >= 1)
    }
}
