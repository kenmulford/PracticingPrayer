import SQLite3
import XCTest

final class PrayerTimeScopeTests: XCTestCase {
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
            .appendingPathComponent("PrayerTimeScopeTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
        return copy
    }

    private func insertCard(_ title: String, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertCollection(_ name: String) -> String {
        "INSERT INTO CardBox (Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES ('\(name)', 0, 99, 0, 0, 1, 1)"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, 0, "
            + "AnsweredAt, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, "
            + "IsImported FROM PrayerRequest WHERE Id = 5"
    }

    private func insertTag(_ name: String, _ color: String) -> String {
        "INSERT INTO PrayerTag (Name, Color, CreatedAt, UpdatedAt, IsSystem) VALUES ('\(name)', '\(color)', 1, 1, 0)"
    }

    private func tagRequest(_ tag: String, _ request: String) -> String {
        "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, CreatedAt, PrayerRequestId) "
            + "SELECT PrayerCardId, (SELECT Id FROM PrayerTag WHERE Name = '\(tag)'), 1, Id "
            + "FROM PrayerRequest WHERE Title = '\(request)'"
    }

    // MARK: - Queries

    private func launch(_ statements: [String] = []) throws -> XCUIApplication {
        let database = try prepareDatabase(statements)
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        return app
    }

    private func prayerTimeButton(_ app: XCUIApplication) -> XCUIElement {
        app.buttons["Home_PrayerTimeButton"].firstMatch
    }

    private func chooseScope(_ app: XCUIApplication, _ title: String) {
        let button = prayerTimeButton(app)
        XCTAssertTrue(button.waitForExistence(timeout: 20), "Prayer Time button missing")
        XCTAssertTrue(button.isEnabled)
        button.tap()
        let option = app.buttons[title].firstMatch
        XCTAssertTrue(option.waitForExistence(timeout: 10), "\(title) missing")
        option.tap()
    }

    private func start(_ app: XCUIApplication) {
        let startButton = app.buttons["PrayerTimeScope_Start"].firstMatch
        XCTAssertTrue(startButton.waitForExistence(timeout: 10), "Start missing")
        startButton.tap()
    }

    private func sessionTitle(_ app: XCUIApplication) -> XCUIElement {
        app.descendants(matching: .any)["PrayerSession_Title"].firstMatch
    }

    // MARK: - Tests

    func testAllRequestsStartsSession() throws {
        let app = try launch()
        chooseScope(app, "All Requests")
        XCTAssertTrue(sessionTitle(app).waitForExistence(timeout: 20))
    }

    func testChooseCardsStartDisabledUntilSelection() throws {
        let test = "testChooseCardsStartDisabledUntilSelection"
        let app = try launch([insertCard("Alpha \(test)"), insertRequest(for: "Alpha \(test)", "Prayer \(test)")])
        chooseScope(app, "Choose Cards…")
        let startButton = app.buttons["PrayerTimeScope_Start"].firstMatch
        XCTAssertTrue(startButton.waitForExistence(timeout: 10))
        XCTAssertFalse(startButton.isEnabled)
        let row = app.staticTexts["Alpha \(test)"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(startButton.isEnabled)
        XCTAssertEqual(startButton.label, "Start (1)")
    }

    func testByTagsStartsSession() throws {
        let test = "testByTagsStartsSession"
        let app = try launch([
            insertCard("Alpha \(test)"), insertRequest(for: "Alpha \(test)", "Prayer \(test)"),
            insertTag("Tag \(test)", "#FF0000"), tagRequest("Tag \(test)", "Prayer \(test)"),
        ])
        chooseScope(app, "By Tags…")
        let row = app.staticTexts["Tag \(test)"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        start(app)
        XCTAssertTrue(sessionTitle(app).waitForExistence(timeout: 20))
    }

    func testByCollectionStartsSession() throws {
        let test = "testByCollectionStartsSession"
        let app = try launch([
            insertCollection("Collection \(test)"),
            insertCard("Alpha \(test)", box: "Collection \(test)"), insertRequest(for: "Alpha \(test)", "Prayer \(test)"),
        ])
        chooseScope(app, "By Collection…")
        let row = app.staticTexts["Collection \(test)"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        start(app)
        XCTAssertTrue(sessionTitle(app).waitForExistence(timeout: 20))
    }

    func testCancelSheetStartsNoSession() throws {
        let app = try launch()
        chooseScope(app, "Choose Cards…")
        let cancel = app.buttons["PrayerTimeScope_Cancel"].firstMatch
        XCTAssertTrue(cancel.waitForExistence(timeout: 10))
        cancel.tap()
        XCTAssertTrue(prayerTimeButton(app).waitForExistence(timeout: 10))
        XCTAssertFalse(sessionTitle(app).exists)
    }

    func testNoActivePrayersDisablesButton() throws {
        let app = try launch(["UPDATE PrayerRequest SET IsAnswered = 1"])
        let button = prayerTimeButton(app)
        XCTAssertTrue(button.waitForExistence(timeout: 20))
        XCTAssertFalse(button.isEnabled)
    }
}
