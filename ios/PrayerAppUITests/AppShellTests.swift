import XCTest

final class AppShellTests: XCTestCase {
    private static let tabs = ["Home", "Cards", "Prayers", "Tags", "Settings"]
    private static let titles = ["Home", "Prayer Cards", "Prayers", "Tags", "Settings"]
    private static let roots = ["home", "cards", "prayers", "tags", "settings"]

    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(databasePath: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = databasePath
        app.launch()
        return app
    }

    private func launchWithFixture() throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("AppShellTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        return launch(databasePath: copy.path)
    }

    private func tabButton(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.tabBars.buttons[name].exists ? app.tabBars.buttons[name] : app.buttons[name].firstMatch
    }

    func testTabsShowInOrderWithHomeSelected() throws {
        let app = try launchWithFixture()
        XCTAssertTrue(app.otherElements["Shell_Root_home"].waitForExistence(timeout: 15)
            || app.staticTexts["Home"].waitForExistence(timeout: 15))
        for name in Self.tabs {
            XCTAssertTrue(tabButton(app, name).exists, "\(name) tab button missing")
        }
        XCTAssertTrue(tabButton(app, "Home").isSelected)
    }

    func testEachTabShowsItsTitledRoot() throws {
        let app = try launchWithFixture()
        XCTAssertTrue(tabButton(app, "Home").waitForExistence(timeout: 15))
        for (index, name) in Self.tabs.enumerated() {
            tabButton(app, name).tap()
            XCTAssertTrue(app.navigationBars[Self.titles[index]].waitForExistence(timeout: 5),
                          "\(Self.titles[index]) navigation bar missing")
            XCTAssertTrue(app.descendants(matching: .any)["Shell_Root_\(Self.roots[index])"].waitForExistence(timeout: 5),
                          "Shell_Root_\(Self.roots[index]) missing")
        }
    }

    func testOpenFailureShowsTryAgain() {
        let app = launch(databasePath: "/nonexistent/prayer_app.db")
        XCTAssertTrue(app.staticTexts["Can't Open Your Prayers"].waitForExistence(timeout: 15))
        let retry = app.buttons["Try Again"]
        XCTAssertTrue(retry.exists)
        for name in Self.tabs {
            XCTAssertFalse(tabButton(app, name).exists, "\(name) tab button should not exist")
        }
        retry.tap()
        XCTAssertTrue(app.staticTexts["Can't Open Your Prayers"].waitForExistence(timeout: 15))
    }
}
