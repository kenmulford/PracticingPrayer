import XCTest

final class TagPickerTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launchPicker() throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("TagPickerTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchArguments = ["-tagPickerHarness"]
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launch()
        let tags = app.buttons["Tags"]
        XCTAssertTrue(tags.waitForExistence(timeout: 15))
        tags.tap()
        XCTAssertTrue(app.navigationBars["Tags"].waitForExistence(timeout: 5))
        return app
    }

    /// The count lives on the harness form behind the pushed picker, so pop, read, and push again.
    private func count(_ app: XCUIApplication) -> String {
        app.navigationBars["Tags"].buttons.firstMatch.tap()
        let label = app.staticTexts["TagPickerHarness_Count"]
        XCTAssertTrue(label.waitForExistence(timeout: 5))
        let value = label.label
        app.buttons["Tags"].tap()
        XCTAssertTrue(app.navigationBars["Tags"].waitForExistence(timeout: 5))
        return value
    }

    func testCreateRowCreatesAndSelects() throws {
        let app = try launchPicker()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("Grief400")
        let create = app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Create' AND label CONTAINS 'Grief400'")).firstMatch
        XCTAssertTrue(create.waitForExistence(timeout: 5))
        create.tap()
        let row = app.buttons["Grief400"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        XCTAssertTrue(row.isSelected)
        XCTAssertEqual(count(app), "1")
    }

    func testExactMatchShowsNoCreateRow() throws {
        let app = try launchPicker()
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 5))
        search.tap()
        search.typeText("family")
        XCTAssertTrue(app.buttons["Family"].waitForExistence(timeout: 5))
        XCTAssertEqual(app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Create'")).count, 0)
    }

    func testToggleSelection() throws {
        let app = try launchPicker()
        let urgent = app.buttons["Urgent"]
        XCTAssertTrue(urgent.waitForExistence(timeout: 5))
        urgent.tap()
        XCTAssertEqual(count(app), "1")
        urgent.tap()
        XCTAssertEqual(count(app), "0")
    }
}
