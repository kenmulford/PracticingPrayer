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
            "-ExpandedSectionIds", "0",
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

    private func launchEmpty(step: String) -> XCUIApplication {
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("OnboardingTests-empty-\(UUID().uuidString).db")
        fixtureCopy = copy
        return launch(databasePath: copy.path, step: step, complete: false)
    }

    private func createCard(_ app: XCUIApplication, named title: String) {
        let field = app.textFields["CardEditor_Title"]
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.typeText(title)
        app.buttons["CardEditor_Confirm"].firstMatch.tap()
    }

    private func emptyStateButton(_ app: XCUIApplication, _ label: String) -> XCUIElement? {
        let matches = app.buttons.matching(NSPredicate(format: "label == %@", label)).allElementsBoundByIndex
        return matches.max { $0.frame.minY < $1.frame.minY }
    }

    func testFirstLaunchShowsWelcome() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
    }

    func testSkipTourShowsNoTip() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        // iOS 27.2 keeps the dismissed sheet's text in the accessibility tree, so dismissal is proven by the Cards tab
        // opening the Cards screen, which a sheet that was still up would block.
        app.buttons["Skip Tour"].firstMatch.tap()
        openTab(app, "Cards")
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10))
        XCTAssertFalse(text(app, Self.addCardTitle).waitForExistence(timeout: 3))
    }

    func testGetStartedShowsAddCardTip() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        app.buttons["Get Started"].firstMatch.tap()
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
        app.buttons["CardEditor_Confirm"].firstMatch.tap()
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label BEGINSWITH %@", "OnboardingTests Resume Card")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(text(app, Self.addPrayerTitle).waitForExistence(timeout: 10))
    }

    func testEmptyStateAddCardAdvancesTourToAddPrayerTip() throws {
        let app = launchEmpty(step: "None")
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        app.buttons["Get Started"].firstMatch.tap()
        XCTAssertTrue(text(app, "No Prayer Cards").waitForExistence(timeout: 10))
        XCTAssertTrue(text(app, Self.addCardTitle).waitForExistence(timeout: 10))
        let add = try XCTUnwrap(emptyStateButton(app, "Add Card"))
        add.tap()
        createCard(app, named: "OnboardingTests Empty Card")
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label BEGINSWITH %@", "OnboardingTests Empty Card")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(text(app, Self.addPrayerTitle).waitForExistence(timeout: 10))
    }

    func testEmptyCardTipSitsAboveEmptyStateAndNewPrayerAdvances() throws {
        let app = launchEmpty(step: "CreateCard")
        openTab(app, "Cards")
        let add = try XCTUnwrap(emptyStateButton(app, "Add Card"))
        add.tap()
        createCard(app, named: "OnboardingTests Overlap Card")
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "label BEGINSWITH %@", "OnboardingTests Overlap Card")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        let tip = text(app, Self.addPrayerTitle)
        let empty = text(app, "No Prayers")
        XCTAssertTrue(tip.waitForExistence(timeout: 10))
        XCTAssertTrue(empty.waitForExistence(timeout: 5))
        XCTAssertLessThanOrEqual(tip.frame.maxY, empty.frame.minY, "the tip overlaps the empty state")
        let newPrayer = try XCTUnwrap(emptyStateButton(app, "New Prayer"))
        newPrayer.tap()
        XCTAssertTrue(tip.waitForNonExistence(timeout: 10), "New Prayer left the tip up")
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

    func testCloseButtonRemovesInlineTip() throws {
        let app = try launchWithFixture(step: "None", complete: false)
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 15))
        app.buttons["Get Started"].firstMatch.tap()
        XCTAssertTrue(text(app, Self.addCardTitle).waitForExistence(timeout: 10))
        app.buttons["Close"].firstMatch.tap()
        XCTAssertTrue(text(app, Self.addCardTitle).waitForNonExistence(timeout: 5))
    }

    func testTakeTheTourAgainShowsAddCardTipOnCards() throws {
        let app = try launchWithFixture(step: "Complete", complete: true)
        openTab(app, "Cards")
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10))
        XCTAssertFalse(text(app, Self.addCardTitle).waitForExistence(timeout: 3))
        openTab(app, "Settings")
        let row = app.descendants(matching: .any)["settings.row.help"]
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(app.buttons["Take the Tour Again"].waitForExistence(timeout: 5))
        app.buttons["Take the Tour Again"].tap()
        XCTAssertTrue(text(app, Self.welcomeTitle).waitForExistence(timeout: 10))
        app.buttons["Get Started"].firstMatch.tap()
        openTab(app, "Cards")
        XCTAssertTrue(text(app, Self.addCardTitle).waitForExistence(timeout: 10))
    }
}
