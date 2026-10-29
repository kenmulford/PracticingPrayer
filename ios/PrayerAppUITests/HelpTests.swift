import XCTest

final class HelpTests: XCTestCase {
    private static let questions = [
        "How do I create a prayer card?",
        "How do I add a prayer request?",
        "What is Quick Add?",
        "How do I organize prayers with tags?",
        "Can I move a prayer to a different card?",
        "What does the star icon do?",
        "How does Prayer Time work?",
        "What is auto-mode in Prayer Time?",
        "Can I filter Prayer Time by tags?",
        "How do I set up prayer reminders?",
        "Why am I not receiving notifications?",
        "How do I back up my data?",
        "Is my data private?",
    ]
    private static let favoritesQuestion = "What does the star icon do?"
    private static let remindersQuestion = "How do I set up prayer reminders?"
    private static let favoritesAnswer =
        "The star marks a card as a favorite. Swipe a card to the right in the Cards list, or touch and hold the card and choose Favorite. Favorited cards sort to the top of their collection so they're always easy to find."
    private static let remindersAnswer =
        "Open a prayer request and choose a reminder frequency: Never, Daily, Weekly, Monthly, or Yearly. Choose a time, and optionally a day of the week. The app will send a notification at your chosen time."

    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        fixtureCopy = nil
        super.tearDown()
    }

    private func launchWithFixture() throws -> XCUIApplication {
        if fixtureCopy == nil {
            let source = URL(fileURLWithPath: #filePath)
                .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
                .appendingPathComponent("contracts/db/long-upgraded.db")
            let copy = FileManager.default.temporaryDirectory
                .appendingPathComponent("HelpTests-\(UUID().uuidString).db")
            try FileManager.default.copyItem(at: source, to: copy)
            fixtureCopy = copy
        }
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = fixtureCopy?.path
        app.launch()
        return app
    }

    private func openHelp(_ app: XCUIApplication) {
        let row = app.descendants(matching: .any)["settings.row.help"]
        if !row.exists {
            let tab = app.buttons["Settings"].firstMatch
            XCTAssertTrue(tab.waitForExistence(timeout: 15))
            tab.tap()
        }
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        XCTAssertTrue(app.navigationBars["Help"].waitForExistence(timeout: 5))
    }

    private func element(_ app: XCUIApplication, _ text: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label == %@", text)).firstMatch
    }

    func testHelpListsThirteenCollapsedQuestions() throws {
        let app = try launchWithFixture()
        openHelp(app)
        for question in Self.questions {
            XCTAssertTrue(element(app, question).waitForExistence(timeout: 5), "\(question) missing")
        }
        XCTAssertFalse(element(app, Self.favoritesAnswer).exists)
        XCTAssertFalse(element(app, Self.remindersAnswer).exists)
    }

    func testCorrectedAnswersShowWhenExpanded() throws {
        let app = try launchWithFixture()
        openHelp(app)
        element(app, Self.favoritesQuestion).tap()
        XCTAssertTrue(element(app, Self.favoritesAnswer).waitForExistence(timeout: 5))
        element(app, Self.remindersQuestion).tap()
        XCTAssertTrue(element(app, Self.remindersAnswer).waitForExistence(timeout: 5))
        XCTAssertTrue(element(app, Self.favoritesAnswer).exists)
    }

    func testRowsCollapseAndResetOnReturn() throws {
        let app = try launchWithFixture()
        openHelp(app)
        let favorites = element(app, Self.favoritesQuestion)
        favorites.tap()
        XCTAssertTrue(element(app, Self.favoritesAnswer).waitForExistence(timeout: 5))
        favorites.tap()
        XCTAssertTrue(element(app, Self.favoritesAnswer).waitForNonExistence(timeout: 5))

        element(app, Self.remindersQuestion).tap()
        XCTAssertTrue(element(app, Self.remindersAnswer).waitForExistence(timeout: 5))
        app.navigationBars["Help"].buttons.element(boundBy: 0).tap()
        openHelp(app)
        XCTAssertTrue(element(app, Self.remindersQuestion).waitForExistence(timeout: 5))
        XCTAssertFalse(element(app, Self.remindersAnswer).exists)
    }
}
