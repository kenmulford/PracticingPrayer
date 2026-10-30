import SQLite3
import XCTest

final class CollectionsTests: XCTestCase {
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
            .appendingPathComponent("CollectionsTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        var handle: OpaquePointer?
        XCTAssertEqual(sqlite3_open(copy.path, &handle), SQLITE_OK)
        defer { sqlite3_close(handle) }
        for statement in statements {
            var message: UnsafeMutablePointer<CChar>?
            let result = sqlite3_exec(handle, statement, nil, nil, &message)
            XCTAssertEqual(result, SQLITE_OK, "\(statement): \(message.map { String(cString: $0) } ?? "")")
            if statement.hasPrefix("UPDATE") {
                XCTAssertGreaterThan(sqlite3_changes(handle), 0, "\(statement) changed no rows")
            }
            sqlite3_free(message)
        }
        return copy
    }

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        return app
    }

    private func openTab(_ app: XCUIApplication, _ name: String) {
        let tab = app.tabBars.buttons[name].exists ? app.tabBars.buttons[name] : app.buttons[name].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "\(name) tab missing")
        tab.tap()
    }

    private func openCollections(_ app: XCUIApplication) {
        openTab(app, "Settings")
        let row = app.buttons["settings.row.collections"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10), "Collections row missing")
        row.tap()
    }

    private func insertBox(_ name: String) -> String {
        "INSERT INTO CardBox (Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES ('\(name)', 0, 0, 0, 0, 1, 1)"
    }

    private func insertCard(_ title: String, box: String) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, (SELECT Id FROM CardBox WHERE Name = '\(box)'), PreArchiveBoxId, SystemKey "
            + "FROM PrayerCard WHERE Id = 2"
    }

    private func element(_ app: XCUIApplication, labelContaining text: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch
    }

    private func addCollection(_ app: XCUIApplication, name: String) {
        let add = app.buttons["Collections_Add"]
        XCTAssertTrue(add.waitForExistence(timeout: 10), "add button missing")
        add.tap()
        let field = app.textFields["CollectionEditor_Name"]
        XCTAssertTrue(field.waitForExistence(timeout: 10), "name field missing")
        field.tap()
        field.typeText(name)
        app.buttons["CollectionEditor_Confirm"].firstMatch.tap()
    }

    // MARK: - Tests

    func testListShowsCountsAndSystemRows() throws {
        let database = try prepareDatabase([
            insertBox("CountsBox"), insertCard("CountsCard One", box: "CountsBox"),
            insertCard("CountsCard Two", box: "CountsBox"),
        ])
        let app = launch(database: database)
        openCollections(app)
        XCTAssertTrue(element(app, labelContaining: "CountsBox").waitForExistence(timeout: 10))
        XCTAssertTrue(element(app, labelContaining: "2 cards").exists)
        let archived = app.descendants(matching: .any).matching(
            NSPredicate(format: "label CONTAINS 'Archived' AND label CONTAINS 'System'")).firstMatch
        XCTAssertTrue(archived.exists, "Archived row should show System")
    }

    func testAddCollectionCreatesRow() throws {
        let app = launch(database: try prepareDatabase())
        openCollections(app)
        addCollection(app, name: "AddedBox")
        XCTAssertTrue(element(app, labelContaining: "AddedBox").waitForExistence(timeout: 10))
    }

    func testEditRenamesRow() throws {
        let app = launch(database: try prepareDatabase([insertBox("RenameBox")]))
        openCollections(app)
        let row = element(app, labelContaining: "RenameBox")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        let field = app.textFields["CollectionEditor_Name"]
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.clearAndTypeText("RenamedBox")
        app.buttons["CollectionEditor_Confirm"].firstMatch.tap()
        XCTAssertTrue(element(app, labelContaining: "RenamedBox").waitForExistence(timeout: 10))
        XCTAssertFalse(element(app, labelContaining: "RenameBox").exists)
    }

    func testSwipeDeleteUnassign() throws {
        let app = launch(database: try prepareDatabase([insertBox("SwipeBox")]))
        openCollections(app)
        let row = element(app, labelContaining: "SwipeBox")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.swipeLeft()
        app.buttons["Delete"].firstMatch.tap()
        let unassign = app.buttons["Unassign Cards"]
        XCTAssertTrue(unassign.waitForExistence(timeout: 10))
        unassign.tap()
        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: row)
        XCTAssertEqual(XCTWaiter().wait(for: [gone], timeout: 10), .completed)
    }

    func testOnlySystemShowsNoCollectionsYet() throws {
        let app = launch(database: try prepareDatabase(["DELETE FROM CardBox WHERE IsSystem = 0"]))
        openCollections(app)
        XCTAssertTrue(app.staticTexts["No Collections Yet"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Collections_Add"].exists)
    }

    func testLoadFailureShowsTryAgain() throws {
        // Schema upkeep recreates a dropped table on open, so the fault is a row whose date cannot decode.
        let app = launch(database: try prepareDatabase(["UPDATE PrayerCardTag SET CreatedAt = NULL"]))
        openCollections(app)
        XCTAssertTrue(app.staticTexts["Can't Load Collections"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Try Again"].exists)
    }

    func testManageCollectionsFromMoreMenu() throws {
        let app = launch(database: try prepareDatabase())
        openTab(app, "Cards")
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 20))
        more.tap()
        let manage = app.buttons["Manage Collections"].firstMatch
        XCTAssertTrue(manage.waitForExistence(timeout: 10))
        manage.tap()
        XCTAssertTrue(app.navigationBars["Collections"].waitForExistence(timeout: 10))
    }
}

private extension XCUIElement {
    func clearAndTypeText(_ text: String) {
        let current = (value as? String) ?? ""
        typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count) + text)
    }
}
