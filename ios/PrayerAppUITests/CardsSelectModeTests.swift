import SQLite3
import XCTest

final class CardsSelectModeTests: XCTestCase {
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
            .appendingPathComponent("CardsSelectModeTests-\(UUID().uuidString).db")
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
        let tab = app.tabBars.buttons["Cards"].exists ? app.tabBars.buttons["Cards"] : app.buttons["Cards"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
    }

    private func standardArguments(expanded: String) -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", "YES", "-ExpandedSectionIds", expanded,
         "-CardsSelectMode", "YES", "-ArchivedFolderId", "2"]
    }

    private func insertBox(_ name: String) -> String {
        "INSERT INTO CardBox (Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES ('\(name)', 0, 10, 0, 0, 1, 1)"
    }

    private func insertCard(_ title: String, favorite: Bool = false, protection: Int = 0, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, \(favorite ? 1 : 0), CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, \(protection), \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, 0, "
            + "AnsweredAt, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, "
            + "IsImported FROM PrayerRequest WHERE Id = 5"
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

    func testTitleAndDisabledActions() throws {
        let database = try prepareDatabase([insertCard("SelectTitle")])
        let app = launch(database: database, arguments: standardArguments(expanded: "0"))
        waitForList(app)
        XCTAssertTrue(app.navigationBars["None Selected"].waitForExistence(timeout: 5))
        for label in ["Move To…", "Archive", "Delete"] {
            XCTAssertTrue(button(app, label).waitForExistence(timeout: 5), "\(label) missing")
            XCTAssertFalse(button(app, label).isEnabled, "\(label) should start disabled")
        }
        tap(app, row: "SelectTitle")
        XCTAssertTrue(app.navigationBars["1 Selected"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.navigationBars["SelectTitle"].exists, "a selecting tap must not push the detail")
        XCTAssertTrue(button(app, "Delete").isEnabled)
    }

    func testMoveToMovesAllAndEnds() throws {
        let database = try prepareDatabase([
            insertBox("SelectMove Box"), insertCard("SelectMove One"), insertCard("SelectMove Two"),
        ])
        let app = launch(database: database, arguments: standardArguments(expanded: "0,1,2,3,4"))
        waitForList(app)
        tap(app, row: "SelectMove One")
        tap(app, row: "SelectMove Two")
        XCTAssertTrue(app.navigationBars["2 Selected"].waitForExistence(timeout: 5))
        button(app, "Move To…").tap()
        let target = app.buttons["SelectMove Box"].firstMatch
        XCTAssertTrue(target.waitForExistence(timeout: 5))
        target.tap()
        XCTAssertTrue(element(app, label: "SelectMove Box · 2").waitForExistence(timeout: 10))
        XCTAssertTrue(button(app, "Done").waitForNonExistence(timeout: 5))
    }

    func testArchiveSkipsSystemCards() throws {
        let database = try prepareDatabase([insertCard("SelectArchive")])
        let app = launch(database: database, arguments: standardArguments(expanded: "0,1"))
        waitForList(app)
        tap(app, row: "SelectArchive")
        tap(app, row: "Quick Add")
        XCTAssertTrue(app.navigationBars["2 Selected"].waitForExistence(timeout: 5))
        button(app, "Archive").tap()
        let confirm = button(app, "Archive Cards")
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.tap()
        XCTAssertTrue(element(app, labelStartingWith: "SelectArchive").waitForNonExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "Quick Add").exists)
    }

    func testDeleteNamesCounts() throws {
        let database = try prepareDatabase([
            insertCard("SelectDelete One"), insertCard("SelectDelete Two"),
            insertRequest(for: "SelectDelete One", "Delete Request One"),
            insertRequest(for: "SelectDelete Two", "Delete Request Two"),
        ])
        let app = launch(database: database, arguments: standardArguments(expanded: "0"))
        waitForList(app)
        tap(app, row: "SelectDelete One")
        tap(app, row: "SelectDelete Two")
        button(app, "Delete").tap()
        XCTAssertTrue(app.staticTexts["Delete 2 Cards?"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["This also deletes their 2 active prayer requests."].exists)
        button(app, "Delete Cards").tap()
        XCTAssertTrue(element(app, labelStartingWith: "SelectDelete One").waitForNonExistence(timeout: 10))
        XCTAssertFalse(element(app, labelStartingWith: "SelectDelete Two").exists)
    }

    func testDoneRestoresTabBar() throws {
        let database = try prepareDatabase([insertCard("SelectDone")])
        let app = launch(database: database, arguments: standardArguments(expanded: "0"))
        waitForList(app)
        XCTAssertFalse(app.tabBars.firstMatch.exists && app.tabBars.buttons["Cards"].isHittable, "tab bar hides while selecting")
        button(app, "Done").tap()
        XCTAssertTrue(app.buttons["Cards"].firstMatch.waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Cards"].firstMatch.isHittable)
        XCTAssertFalse(app.navigationBars["None Selected"].exists)
    }

    func testSearchKeepsSelection() throws {
        let database = try prepareDatabase([insertCard("SelectKeepAlpha"), insertCard("SelectKeepBeta")])
        let app = launch(database: database, arguments: standardArguments(expanded: "0"))
        waitForList(app)
        tap(app, row: "SelectKeepAlpha")
        let search = app.searchFields["Search Cards"]
        search.tap()
        search.typeText("KeepBeta")
        XCTAssertTrue(element(app, labelStartingWith: "SelectKeepAlpha").waitForNonExistence(timeout: 10))
        tap(app, row: "SelectKeepBeta")
        XCTAssertTrue(app.navigationBars["2 Selected"].waitForExistence(timeout: 5))
    }

    func testLockedRowNotSelectable() throws {
        let database = try prepareDatabase([insertCard("SelectLocked", protection: 1)])
        let app = launch(database: database, arguments: standardArguments(expanded: "0"))
        waitForList(app)
        let row = element(app, label: "Protected")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.tap()
        XCTAssertTrue(app.navigationBars["None Selected"].exists)
    }
}
