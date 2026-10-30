import SQLite3
import XCTest

final class CardRowActionsTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    // MARK: - Fixture

    private func prepareDatabase(_ statements: [String] = []) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("CardRowActionsTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        var handle: OpaquePointer?
        XCTAssertEqual(sqlite3_open(copy.path, &handle), SQLITE_OK)
        defer { sqlite3_close(handle) }
        for statement in statements {
            var message: UnsafeMutablePointer<CChar>?
            let result = sqlite3_exec(handle, statement, nil, nil, &message)
            XCTAssertEqual(result, SQLITE_OK, "\(statement): \(message.map { String(cString: $0) } ?? "")")
            sqlite3_free(message)
        }
        return copy
    }

    private func launch(database: URL, arguments: [String]) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = arguments
        app.launch()
        openCardsTab(app)
        return app
    }

    private func openCardsTab(_ app: XCUIApplication) {
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
    }

    private func insertCard(_ title: String, favorite: Bool = false, protection: Int = 0, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, \(favorite ? 1 : 0), CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, \(protection), \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    // MARK: - Queries

    private func element(_ app: XCUIApplication, label: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    private func element(_ app: XCUIApplication, labelStartingWith prefix: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    private func header(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        element(app, labelStartingWith: "\(name) · ")
    }

    private func waitForList(_ app: XCUIApplication) {
        XCTAssertTrue(header(app, "Archived").waitForExistence(timeout: 20), "card list did not load")
    }

    private func tap(_ app: XCUIApplication, row title: String) {
        let row = element(app, labelStartingWith: title)
        XCTAssertTrue(row.waitForExistence(timeout: 10), "\(title) row missing")
        row.tap()
    }

    private func button(_ app: XCUIApplication, _ label: String) -> XCUIElement {
        app.buttons[label].firstMatch
    }

    // MARK: - Tests

    private func rowArguments() -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", "YES", "-ExpandedSectionIds", "0,1", "-ArchivedFolderId", "2"]
    }

    func testLeadingSwipeShowsFavorite() throws {
        let database = try prepareDatabase([insertCard("RowSwipeFavorite")])
        let app = launch(database: database, arguments: rowArguments())
        waitForList(app)
        let row = element(app, labelStartingWith: "RowSwipeFavorite")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        // A full swipe runs the action itself, so drag only far enough to reveal the button.
        row.coordinate(withNormalizedOffset: CGVector(dx: 0.1, dy: 0.5))
            .press(forDuration: 0.1, thenDragTo: row.coordinate(withNormalizedOffset: CGVector(dx: 0.4, dy: 0.5)))
        XCTAssertTrue(app.buttons["Favorite"].waitForExistence(timeout: 5))
    }

    func testDeleteConfirmsBeforeRemoving() throws {
        let database = try prepareDatabase([insertCard("RowSwipeDelete")])
        let app = launch(database: database, arguments: rowArguments())
        waitForList(app)
        let row = element(app, labelStartingWith: "RowSwipeDelete")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.swipeLeft()
        let swipeDelete = app.buttons["Delete"].firstMatch
        XCTAssertTrue(swipeDelete.waitForExistence(timeout: 5))
        swipeDelete.tap()
        XCTAssertTrue(row.exists, "the row stays until the dialog is confirmed")
        let confirm = app.buttons["Delete"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.tap()
        XCTAssertTrue(element(app, labelStartingWith: "RowSwipeDelete").waitForNonExistence(timeout: 10))
    }

    func testSystemCardHasNoSwipe() throws {
        let database = try prepareDatabase()
        let app = launch(database: database, arguments: rowArguments())
        waitForList(app)
        let row = element(app, labelStartingWith: "Quick Add")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.swipeLeft()
        XCTAssertFalse(app.buttons["Delete"].waitForExistence(timeout: 2))
    }
}
