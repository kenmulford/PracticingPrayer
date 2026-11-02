import XCTest

final class PrayerTimeAutoTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch() throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("PrayerTimeAutoUITests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchArguments = ["-prayerSession", "-prayerSessionAuto", "-ResetTips"]
        app.launch()
        XCTAssertTrue(app.staticTexts["PrayerSession_Title"].waitForExistence(timeout: 20), "Session did not open")
        return app
    }

    func testPauseShowsResume() throws {
        let app = try launch()
        let button = app.buttons["PrayerSession_PauseResume"]
        XCTAssertTrue(button.waitForExistence(timeout: 5))
        XCTAssertEqual(button.label, "Pause")
        button.tap()
        XCTAssertEqual(button.label, "Resume")
    }

    func testIPadRotationUnchanged() throws {
        let app = try launch()
        XCUIDevice.shared.orientation = .landscapeLeft
        let frame = app.windows.firstMatch.frame
        XCTAssertGreaterThan(frame.width, frame.height)
        XCUIDevice.shared.orientation = .portrait
    }
}
