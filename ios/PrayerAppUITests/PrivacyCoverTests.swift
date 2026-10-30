import XCTest

final class PrivacyCoverTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    func testCoverAbsentWhileActive() throws {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("PrivacyCoverTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launch()
        XCTAssertTrue(app.tabBars.buttons["Home"].waitForExistence(timeout: 15)
            || app.buttons["Home"].firstMatch.waitForExistence(timeout: 15))
        XCTAssertFalse(app.otherElements["PrivacyCover_Root"].exists)
    }
}
