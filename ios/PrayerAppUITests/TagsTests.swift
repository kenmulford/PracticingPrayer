import SQLite3
import XCTest

final class TagsTests: XCTestCase {
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
            .appendingPathComponent("TagsTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
        return copy
    }

    private func insertTag(_ name: String, _ color: String) -> String {
        "INSERT INTO PrayerTag (Name, Color, CreatedAt, UpdatedAt, IsSystem) VALUES ('\(name)', '\(color)', 1, 1, 0)"
    }

    private func tagRequest(_ tag: String, _ request: String) -> String {
        "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, CreatedAt, PrayerRequestId) "
            + "SELECT PrayerCardId, (SELECT Id FROM PrayerTag WHERE Name = '\(tag)'), 1, Id "
            + "FROM PrayerRequest WHERE Title = '\(request)'"
    }

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        let tab = app.tabBars.buttons["Tags"].exists ? app.tabBars.buttons["Tags"] : app.buttons["Tags"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Tags tab missing")
        tab.tap()
        return app
    }

    private func row(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", name)).firstMatch
    }

    // MARK: - Tests

    func testRowOpensTagPrayers() throws {
        let database = try prepareDatabase([
            insertTag("Tag TagsTestsOpen", "#336699"),
            tagRequest("Tag TagsTestsOpen", "Sample Prayer Entry 1"),
        ])
        let app = launch(database: database)
        let open = row(app, "Tag TagsTestsOpen")
        XCTAssertTrue(open.waitForExistence(timeout: 10))
        open.tap()
        XCTAssertTrue(app.descendants(matching: .any)["TagPrayers_Root"].waitForExistence(timeout: 10))
    }

    func testDeleteConfirmsAndCancelKeeps() throws {
        let database = try prepareDatabase([insertTag("Tag TagsTestsDelete", "#336699")])
        let app = launch(database: database)
        let target = row(app, "Tag TagsTestsDelete")
        XCTAssertTrue(target.waitForExistence(timeout: 10))

        target.swipeLeft()
        app.buttons["Delete"].firstMatch.tap()
        app.otherElements["PopoverDismissRegion"].firstMatch.tap()
        XCTAssertTrue(target.waitForExistence(timeout: 5), "Row must stay after cancelling")

        target.swipeLeft()
        app.buttons["Delete"].firstMatch.tap()
        let confirm = app.buttons["Delete Tag"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.tap()
        XCTAssertTrue(target.waitForNonExistence(timeout: 10))
    }

    func testSystemTagSwipeHasNoDelete() throws {
        let database = try prepareDatabase()
        let app = launch(database: database)
        let system = row(app, "Recently Notified")
        XCTAssertTrue(system.waitForExistence(timeout: 10))
        system.swipeLeft()
        XCTAssertTrue(app.buttons["Edit"].firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Delete"].firstMatch.exists)
    }

    func testNewTagOpensSheet() throws {
        let database = try prepareDatabase()
        let app = launch(database: database)
        let add = app.buttons["New Tag"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10))
        add.tap()
        XCTAssertTrue(app.navigationBars["New Tag"].waitForExistence(timeout: 5))
    }
}
