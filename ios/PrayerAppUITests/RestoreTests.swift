import XCTest

final class RestoreTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private let repoRoot = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

    private func openBackup(importing archive: URL? = nil) throws -> XCUIApplication {
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("RestoreTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(
            at: repoRoot.appendingPathComponent("contracts/db/long-upgraded.db"), to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        if let archive { app.launchEnvironment["PRAYER_RESTORE_IMPORT_URL"] = archive.path }
        app.launch()
        let tab = app.buttons["Settings"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 15))
        tab.tap()
        let row = app.descendants(matching: .any)["settings.row.backup"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        return app
    }

    func testRestoreConfirmAlert() throws {
        let app = try openBackup()
        app.buttons["backup.restore"].firstMatch.tap()
        XCTAssertTrue(app.alerts["Restore Backup?"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.alerts.buttons["Restore"].exists)
        app.alerts.buttons["Cancel"].tap()
        XCTAssertFalse(app.alerts["Restore Backup?"].exists)
    }

    func testInvalidFileShowsRestoreFailed() throws {
        let app = try openBackup(importing: repoRoot.appendingPathComponent("ios/PrayerAppUITests/Fixtures/not-a-backup.pcrd"))
        app.buttons["backup.restore"].firstMatch.tap()
        app.alerts.buttons["Restore"].tap()
        XCTAssertTrue(app.alerts["Restore Failed"].waitForExistence(timeout: 10))
        app.alerts.buttons["OK"].tap()
    }

    func testRestoreLandsOnHome() throws {
        let app = try openBackup(importing: repoRoot.appendingPathComponent("contracts/backup/maui-made.pcrd"))
        app.buttons["backup.restore"].firstMatch.tap()
        app.alerts.buttons["Restore"].tap()
        XCTAssertTrue(app.buttons["Home"].firstMatch.waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Home"].firstMatch.isSelected)
    }
}
