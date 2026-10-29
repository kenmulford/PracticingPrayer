import SQLite3
import XCTest

final class HomeListsUITests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    // MARK: - Fixture

    private func run(_ statements: [String], at path: String) {
        var handle: OpaquePointer?
        XCTAssertEqual(sqlite3_open(path, &handle), SQLITE_OK)
        defer { sqlite3_close(handle) }
        for statement in statements {
            var message: UnsafeMutablePointer<CChar>?
            let result = sqlite3_exec(handle, statement, nil, nil, &message)
            XCTAssertEqual(result, SQLITE_OK, "\(statement): \(message.map { String(cString: $0) } ?? "")")
            sqlite3_free(message)
        }
    }

    private func prepareDatabase(_ statements: [String] = []) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("HomeListsUITests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
        return copy
    }

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        let tab = app.tabBars.buttons["Home"].exists ? app.tabBars.buttons["Home"] : app.buttons["Home"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Home tab missing")
        tab.tap()
        return app
    }

    private func insertCard(_ title: String) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, NULL, PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, "
            + "0, NULL, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, "
            + "NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, 0 FROM PrayerRequest WHERE Id = 5"
    }

    private func insertOldInteraction(_ title: String, days: Int) -> String {
        "INSERT INTO PrayerInteraction (PrayerId, InteractionType, InteractionAt, CreatedAt, UpdatedAt) "
            + "SELECT Id, 'Prayed', (strftime('%s','now','-\(days) days') * 10000000) + 621355968000000000, 1, 1 "
            + "FROM PrayerRequest WHERE Title = '\(title)'"
    }

    // MARK: - Queries

    private func tile(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label CONTAINS %@", name)).firstMatch
    }

    private func row(_ app: XCUIApplication, _ title: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    private func swipeAction(_ app: XCUIApplication, on row: XCUIElement, leading: Bool, button: String) {
        XCTAssertTrue(row.waitForExistence(timeout: 20), "row missing")
        if leading { row.swipeRight() } else { row.swipeLeft() }
        let action = app.buttons[button].firstMatch
        XCTAssertTrue(action.waitForExistence(timeout: 5), "\(button) swipe action missing")
        action.tap()
    }

    // MARK: - Tests

    func testUnansweredMarkAnsweredRemovesRow() throws {
        let test = "HomeLists"
        let title = "Unanswered Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title)]))
        tile(app, "Unanswered").tap()
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 20), "prayer missing under Unanswered")

        swipeAction(app, on: row(app, title), leading: true, button: "Mark Answered")
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 3), "answered prayer should leave the list")
    }

    func testDeleteConfirmsAndCancelKeeps() throws {
        let test = "HomeListsDelete"
        let title = "Delete Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title)]))
        tile(app, "Unanswered").tap()

        swipeAction(app, on: row(app, title), leading: false, button: "Delete")
        let confirm = app.buttons["Delete"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5), "delete confirmation missing")
        app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.05, dy: 0.05)).tap()
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 5), "dismissing the dialog must keep the row")

        swipeAction(app, on: row(app, title), leading: false, button: "Delete")
        let delete = app.buttons["Delete"].firstMatch
        XCTAssertTrue(delete.waitForExistence(timeout: 5), "delete confirmation missing")
        delete.tap()
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 3), "confirmed delete should remove the row")
    }

    func testActiveCardsSectionHeaderShowsCardTitle() throws {
        let test = "HomeListsCards"
        let cardTitle = "Card \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard(cardTitle), insertRequest(for: cardTitle, "Prayer \(test)")]))
        tile(app, "Active Cards").tap()
        let header = app.staticTexts.matching(NSPredicate(format: "label == %@", cardTitle)).firstMatch
        XCTAssertTrue(header.waitForExistence(timeout: 20), "section header for the card missing")
    }

    func testOverdueListsOldInteraction() throws {
        let test = "HomeListsOverdue"
        let title = "Overdue Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title),
                insertOldInteraction(title, days: 60),
            ]))
        tile(app, "Overdue").tap()
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 20), "overdue prayer missing")
    }

    func testEmptyStates() throws {
        let app = launch(database: try prepareDatabase(["DELETE FROM PrayerRequest"]))
        tile(app, "Overdue").tap()
        XCTAssertTrue(app.staticTexts["All Prayed For"].firstMatch.waitForExistence(timeout: 20))
        app.navigationBars.buttons.firstMatch.tap()

        tile(app, "Unanswered").tap()
        XCTAssertTrue(app.staticTexts["No Unanswered Prayers"].firstMatch.waitForExistence(timeout: 20))
        app.navigationBars.buttons.firstMatch.tap()

        tile(app, "Active Cards").tap()
        XCTAssertTrue(app.staticTexts["No Active Cards"].firstMatch.waitForExistence(timeout: 20))
    }
}
