import XCTest

final class QuickAddTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launchWithFixture() throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("QuickAddTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launch()
        return app
    }

    private func openQuickAdd(_ app: XCUIApplication) {
        let button = app.buttons["Quick Add"].firstMatch
        XCTAssertTrue(button.waitForExistence(timeout: 15))
        button.tap()
        XCTAssertTrue(app.buttons["QuickAdd_Cancel"].firstMatch.waitForExistence(timeout: 5))
    }

    private func unansweredCount(_ app: XCUIApplication) -> Int? {
        let tile = app.buttons["Unanswered"].firstMatch
        guard tile.waitForExistence(timeout: 10) else { return nil }
        return Int(String(describing: tile.value ?? "").filter(\.isNumber))
    }

    func testSavingNewPrayerClosesSheetAndReloadsHome() throws {
        let app = try launchWithFixture()
        let before = try XCTUnwrap(unansweredCount(app))
        openQuickAdd(app)

        let cardTitle = app.textFields["Card Title"]
        XCTAssertTrue(cardTitle.waitForExistence(timeout: 5))
        cardTitle.tap()
        cardTitle.typeText("UITest Card")
        let prayerTitle = app.textFields["Title"].firstMatch
        prayerTitle.tap()
        prayerTitle.typeText("UITest Prayer")

        let save = app.buttons["QuickAdd_Confirm"].firstMatch
        XCTAssertTrue(save.isEnabled)
        save.tap()

        XCTAssertTrue(app.buttons["QuickAdd_Cancel"].firstMatch.waitForNonExistence(timeout: 10))
        let expectation = expectation(
            for: NSPredicate { _, _ in self.unansweredCount(app) == before + 1 },
            evaluatedWith: nil)
        wait(for: [expectation], timeout: 10)
    }

    func testCancelWithEditsAsksToDiscard() throws {
        let app = try launchWithFixture()
        openQuickAdd(app)

        let prayerTitle = app.textFields["Title"].firstMatch
        XCTAssertTrue(prayerTitle.waitForExistence(timeout: 5))
        prayerTitle.tap()
        prayerTitle.typeText("Unsaved")

        app.buttons["QuickAdd_Cancel"].firstMatch.tap()
        // On iPad the dialog is a popover that omits the cancel-role "Keep Editing" button.
        let discard = app.buttons["Discard Changes"]
        XCTAssertTrue(discard.waitForExistence(timeout: 5))
        discard.tap()
        XCTAssertTrue(app.buttons["QuickAdd_Cancel"].firstMatch.waitForNonExistence(timeout: 5))
    }
}
