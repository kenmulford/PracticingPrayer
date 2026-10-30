import XCTest

final class PinFallbackTests: XCTestCase {
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
            .appendingPathComponent("PinFallbackTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchEnvironment["CONFIDENTIAL_AUTH_STUB"] = "passcodeNotSet"
        app.launchEnvironment["CONFIDENTIAL_PIN_STORE"] = "memory"
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

    private func tapMoreItem(_ app: XCUIApplication, _ name: String) {
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10))
        more.tap()
        let item = app.buttons[name].firstMatch
        XCTAssertTrue(item.waitForExistence(timeout: 5), "\(name) missing")
        item.tap()
    }

    func testSetThenEnterPinRevealsLockedCard() throws {
        let app = try launch()
        let title = "PinFallbackReveal"
        createLockedCard(app, title: title)

        tapMoreItem(app, "Show Confidential")
        XCTAssertTrue(app.navigationBars["Set PIN"].waitForExistence(timeout: 10), "Set PIN sheet missing")
        for id in ["SetPin_Pin", "SetPin_Confirm"] {
            let field = app.secureTextFields[id]
            field.tap()
            field.typeText("123456")
        }
        app.buttons["SetPin_Save"].firstMatch.tap()
        XCTAssertTrue(app.staticTexts[title].waitForExistence(timeout: 10), "title not revealed after Set PIN")

        tapMoreItem(app, "Hide Confidential")
        XCTAssertTrue(app.staticTexts[title].waitForNonExistence(timeout: 10), "title still visible after Hide")

        tapMoreItem(app, "Show Confidential")
        XCTAssertTrue(app.navigationBars["Enter PIN"].waitForExistence(timeout: 10), "Enter PIN sheet missing")
        let entry = app.secureTextFields["EnterPin_Field"]
        entry.typeText("000000")
        XCTAssertTrue(app.staticTexts["Incorrect PIN. 4 attempts left."].waitForExistence(timeout: 10))
        entry.typeText("123456")
        XCTAssertTrue(app.staticTexts[title].waitForExistence(timeout: 10), "title not revealed after Enter PIN")
    }
}
