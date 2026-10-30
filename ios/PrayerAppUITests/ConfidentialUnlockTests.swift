import XCTest

final class ConfidentialUnlockTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(authStub: String) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("ConfidentialUnlockTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchEnvironment["CONFIDENTIAL_AUTH_STUB"] = authStub
        app.launchArguments = [
            "-ResetTips", "-CollectionsBannerDismissed", "YES", "-ExpandedSectionIds", "0,1,2,3,4,5,6,7,8",
            "-ArchivedFolderId", "2",
        ]
        app.launch()
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
        return app
    }

    private func createLockedCard(_ app: XCUIApplication, title: String) {
        app.buttons["Add Card"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForExistence(timeout: 10))
        let field = app.textFields["CardEditor_Title"]
        field.tap()
        field.typeText(title)
        app.buttons["Locked"].firstMatch.tap()
        app.buttons["CardEditor_Confirm"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForNonExistence(timeout: 10))
    }

    private func showConfidential(_ app: XCUIApplication) {
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10))
        more.tap()
        let show = app.buttons["Show Confidential"].firstMatch
        XCTAssertTrue(show.waitForExistence(timeout: 5), "Show Confidential missing")
        show.tap()
    }

    func testStubSuccessRevealsLockedCardAndFlipsMenu() throws {
        let app = try launch(authStub: "success")
        createLockedCard(app, title: "ConfidentialUnlockReveal")
        XCTAssertFalse(app.staticTexts["ConfidentialUnlockReveal"].exists, "title must be masked while locked")
        showConfidential(app)
        XCTAssertTrue(app.staticTexts["ConfidentialUnlockReveal"].waitForExistence(timeout: 10), "title not revealed")
        app.buttons["More"].firstMatch.tap()
        XCTAssertTrue(app.buttons["Hide Confidential"].firstMatch.waitForExistence(timeout: 5))
    }

    func testStubCancelKeepsCardProtected() throws {
        let app = try launch(authStub: "cancel")
        createLockedCard(app, title: "ConfidentialUnlockCancel")
        showConfidential(app)
        XCTAssertFalse(app.staticTexts["ConfidentialUnlockCancel"].waitForExistence(timeout: 3))
        XCTAssertTrue(app.staticTexts["Protected"].firstMatch.exists)
    }
}
