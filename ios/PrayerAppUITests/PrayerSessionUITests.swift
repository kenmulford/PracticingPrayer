import XCTest

final class PrayerSessionUITests: XCTestCase {
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
            .appendingPathComponent("PrayerSessionUITests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchArguments = ["-prayerSession", "-ResetTips"]
        app.launch()
        XCTAssertTrue(app.staticTexts["PrayerSession_Title"].waitForExistence(timeout: 20), "Session did not open")
        return app
    }

    func testNextAndPreviousUpdateTitle() throws {
        let app = try launch()
        let title = app.staticTexts["PrayerSession_Title"]
        XCTAssertEqual(title.label, "1 of 3")
        app.buttons["Next"].tap()
        XCTAssertEqual(title.label, "2 of 3")
        app.buttons["Previous"].tap()
        XCTAssertEqual(title.label, "1 of 3")
    }

    func testCompletionDoneDismisses() throws {
        let app = try launch()
        for _ in 0..<3 { app.buttons["Next"].tap() }
        XCTAssertTrue(app.staticTexts["You've prayed through all your requests!"].waitForExistence(timeout: 5))
        app.buttons["Done"].tap()
        XCTAssertTrue(app.staticTexts["PrayerSession_Title"].waitForNonExistence(timeout: 5))
    }

    func testCloseDismisses() throws {
        let app = try launch()
        app.buttons["Close"].tap()
        XCTAssertTrue(app.staticTexts["PrayerSession_Title"].waitForNonExistence(timeout: 5))
    }
}
