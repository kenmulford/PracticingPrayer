import XCTest

final class ConfirmImportTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    func testExistingCardAddBackSaves() throws {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("ConfirmImportTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchArguments = ["-confirmImportHarness"]
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launch()

        let existing = app.buttons["Existing Card"]
        XCTAssertTrue(existing.waitForExistence(timeout: 15))
        existing.tap()
        app.buttons["QuickAdd_CardRow"].tap()
        let general = app.staticTexts["General"].firstMatch
        XCTAssertTrue(general.waitForExistence(timeout: 5))
        general.tap()

        XCTAssertTrue(app.staticTexts["Already on This Card (1)"].waitForExistence(timeout: 5))
        app.buttons["ConfirmImport_AddBack"].tap()
        app.buttons["QuickAdd_Confirm"].tap()
        let cancel = app.buttons["QuickAdd_Cancel"]
        XCTAssertTrue(cancel.waitForNonExistence(timeout: 10))
    }
}
