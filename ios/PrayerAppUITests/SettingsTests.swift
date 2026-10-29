import XCTest

final class SettingsTests: XCTestCase {
    private static let rows: [(id: String, title: String)] = [
        ("appSettings", "App Settings"),
        ("collections", "Collections"),
        ("backup", "Backup & Restore"),
        ("help", "Help"),
        ("about", "About"),
    ]

    private var fixtureCopy: URL?
    private var suite: String?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        fixtureCopy = nil
        suite = nil
        super.tearDown()
    }

    private func launchWithFixture(arguments: [String] = [], usesSuite: Bool = false) throws -> XCUIApplication {
        if fixtureCopy == nil {
            let source = URL(fileURLWithPath: #filePath)
                .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
                .appendingPathComponent("contracts/db/long-upgraded.db")
            let copy = FileManager.default.temporaryDirectory
                .appendingPathComponent("SettingsTests-\(UUID().uuidString).db")
            try FileManager.default.copyItem(at: source, to: copy)
            fixtureCopy = copy
        }
        if usesSuite && suite == nil { suite = "SettingsTests-\(UUID().uuidString)" }
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = fixtureCopy?.path
        if usesSuite { app.launchEnvironment["PRAYER_DEFAULTS_SUITE"] = suite }
        app.launchArguments = arguments
        app.launch()
        return app
    }

    private func openSettings(_ app: XCUIApplication) {
        let tab = app.buttons["Settings"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 15))
        tab.tap()
    }

    private func openAppSettings(_ app: XCUIApplication) {
        openSettings(app)
        let row = app.descendants(matching: .any)["settings.row.appSettings"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        XCTAssertTrue(app.navigationBars["App Settings"].waitForExistence(timeout: 5))
    }

    private func control(_ app: XCUIApplication, _ id: String) -> XCUIElement {
        app.descendants(matching: .any)[id]
    }

    func testRootRowsPushTheirRoutes() throws {
        let app = try launchWithFixture()
        openSettings(app)
        for row in Self.rows {
            let element = control(app, "settings.row.\(row.id)")
            XCTAssertTrue(element.waitForExistence(timeout: 5), "\(row.id) row missing")
            element.tap()
            XCTAssertTrue(app.navigationBars[row.title].waitForExistence(timeout: 5), "\(row.title) bar missing")
            app.navigationBars[row.title].buttons.element(boundBy: 0).tap()
        }
    }

    func testFreshInstallShowsDefaults() throws {
        let app = try launchWithFixture(usesSuite: true)
        openAppSettings(app)
        XCTAssertEqual(control(app, "appSettings.allowNotifications").value as? String, "1")
        XCTAssertTrue(control(app, "appSettings.reminderTime").exists)
        XCTAssertTrue(app.staticTexts["Overdue After 30 Days"].exists)
        XCTAssertEqual(control(app, "appSettings.allowLandscape").value as? String, "1")
        XCTAssertTrue(app.staticTexts["Developer"].exists)
    }

    func testUpgradedValuesShowUnchanged() throws {
        let app = try launchWithFixture(arguments: [
            "-AllowNotifications", "YES", "-OverdueDayThreshold", "45", "-PrayerTimeLandscape", "NO",
            "-DefaultNotifyHour", "7", "-DefaultNotifyMinute", "30",
        ])
        openAppSettings(app)
        XCTAssertTrue(app.staticTexts["Overdue After 45 Days"].exists)
        XCTAssertEqual(control(app, "appSettings.allowLandscape").value as? String, "0")
        let time = control(app, "appSettings.reminderTime")
        XCTAssertTrue(time.exists)
        XCTAssertTrue(time.debugDescription.contains("7:30"), "reminder time is not 7:30")
    }

    func testChangesPersistAcrossRelaunch() throws {
        var app = try launchWithFixture(usesSuite: true)
        openAppSettings(app)
        app.steppers.firstMatch.buttons["Increment"].tap()
        control(app, "appSettings.allowLandscape").switches.firstMatch.tap()
        control(app, "appSettings.allowNotifications").switches.firstMatch.tap()
        XCTAssertTrue(app.staticTexts["Overdue After 31 Days"].exists)
        app.terminate()

        app = try launchWithFixture(usesSuite: true)
        openAppSettings(app)
        XCTAssertTrue(app.staticTexts["Overdue After 31 Days"].exists)
        XCTAssertEqual(control(app, "appSettings.allowLandscape").value as? String, "0")
        XCTAssertEqual(control(app, "appSettings.allowNotifications").value as? String, "0")
        XCTAssertFalse(control(app, "appSettings.reminderTime").exists)
    }

    func testStepperBoundsDisableButtons() throws {
        var app = try launchWithFixture(arguments: ["-OverdueDayThreshold", "1"])
        openAppSettings(app)
        XCTAssertFalse(app.steppers.firstMatch.buttons["Decrement"].isEnabled)
        app.terminate()

        app = try launchWithFixture(arguments: ["-OverdueDayThreshold", "365"])
        openAppSettings(app)
        XCTAssertFalse(app.steppers.firstMatch.buttons["Increment"].isEnabled)
    }
}
