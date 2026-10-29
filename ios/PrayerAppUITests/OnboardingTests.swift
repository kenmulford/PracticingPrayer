import XCTest

final class OnboardingTests: XCTestCase {
    private static let welcomeTitle = "Welcome to Practicing Prayer"
    private static let addCardTitle = "Create Your First Prayer Card"
    private static let addPrayerTitle = "Add a Prayer Request"
    private static let prayerTimeMessage = "Pray through your requests one by one."

    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(databasePath: String, step: String, complete: Bool) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = databasePath
        app.launchArguments = [
            "-ResetTips", "-CollectionsBannerDismissed", "YES",
            "-OnboardingStep", step, "-OnboardingComplete", complete ? "YES" : "NO",
        ]
        app.launch()
        return app
    }

    private func launchWithFixture(step: String, complete: Bool) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("OnboardingTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        return launch(databasePath: copy.path, step: step, complete: complete)
    }

    private func text(_ app: XCUIApplication, _ label: String) -> XCUIElement {
        app.staticTexts[label].firstMatch
    }

    private func openTab(_ app: XCUIApplication, _ name: String) {
        let tab = app.tabBars.buttons[name].exists ? app.tabBars.buttons[name] : app.buttons[name].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 15))
        tab.tap()
    }

    func testFirstLaunchShowsWelcome() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
    }

    func testSkipTourShowsNoTip() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        app.buttons["Skip Tour"].tap()
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForNonExistence(timeout: 5))
        openTab(app, "Cards")
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10))
        XCTAssertFalse(text(app, Self.addCardTitle).waitForExistence(timeout: 3))
    }

    func testGetStartedShowsAddCardTip() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        app.buttons["Get Started"].tap()
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10))
        XCTAssertTrue(text(app, Self.addCardTitle).waitForExistence(timeout: 10))
    }

    func testResumeAtAddRequestShowsAddPrayerTip() throws {
        let app = try launchWithFixture(step: "AddRequest", complete: false)
        openTab(app, "Cards")
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10))
        app.buttons["Add Card"].firstMatch.tap()
        let field = app.textFields["CardEditor_Title"]
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.typeText("OnboardingTests Resume Card")
        app.buttons["CardEditor_Confirm"].tap()
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label BEGINSWITH %@", "OnboardingTests Resume Card")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(text(app, Self.addPrayerTitle).waitForExistence(timeout: 10))
    }

    func testLegacyStepShowsPrayerTimeTip() throws {
        let app = try launchWithFixture(step: "ShareIntro", complete: false)
        XCTAssertTrue(text(app, Self.prayerTimeMessage).waitForExistence(timeout: 15))
    }

    func testCompleteShowsNoSheet() throws {
        let app = try launchWithFixture(step: "Complete", complete: true)
        XCTAssertTrue(app.buttons["Home_PrayerTimeButton"].waitForExistence(timeout: 15))
        XCTAssertFalse(text(app, Self.welcomeTitle).exists)
    }

    func testLoadFailureShowsNoSheet() {
        let app = launch(databasePath: "/nonexistent/prayer_app.db", step: "None", complete: false)
        XCTAssertTrue(app.buttons["Try Again"].waitForExistence(timeout: 15))
        XCTAssertFalse(text(app, Self.welcomeTitle).exists)
    }

    func testTakeTheTourAgain() throws {
        let app = try launchWithFixture(step: "Complete", complete: true)
        openTab(app, "Settings")
        let row = app.descendants(matching: .any)["settings.row.help"]
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(app.buttons["Take the Tour Again"].waitForExistence(timeout: 5))
        app.buttons["Take the Tour Again"].tap()
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 10))
    }
}
