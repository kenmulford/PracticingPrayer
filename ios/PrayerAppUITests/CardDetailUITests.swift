import SQLite3
import XCTest

final class CardDetailUITests: XCTestCase {
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
            .appendingPathComponent("CardDetailUITests-\(UUID().uuidString).db")
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

    private func standardArguments(expanded: String, showTip: Bool = false) -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", showTip ? "NO" : "YES", "-ExpandedSectionIds", expanded,
         "-ArchivedFolderId", "2"]
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

    private func open(_ app: XCUIApplication, card title: String) {
        waitForList(app)
        let row = element(app, labelStartingWith: title)
        XCTAssertTrue(row.waitForExistence(timeout: 10), "\(title) row missing")
        row.tap()
        openedCard = title
        XCTAssertTrue(app.navigationBars[title].waitForExistence(timeout: 10), "\(title) detail missing")
    }

    // The Cards list also has a More menu, so scope the lookup to the open card's navigation bar.
    private var openedCard = ""

    private func openMore(_ app: XCUIApplication) {
        let more = app.navigationBars[openedCard].buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10))
        more.tap()
    }

    private let allSections = "0,1,2,3,4,5,6,7,8"

    private func waitForList(_ app: XCUIApplication) {
        XCTAssertTrue(header(app, "Archived").waitForExistence(timeout: 20), "card list did not load")
    }

    // MARK: - Tests

    func testMoreMenuOrder() throws {
        let database = try prepareDatabase([insertCard("DetailMenu")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailMenu")
        openMore(app)
        let labels = ["Favorite", "Edit", "Move To…", "Archive", "Delete"]
        let items = labels.map { app.buttons[$0].firstMatch }
        XCTAssertTrue(items[0].waitForExistence(timeout: 5))
        XCTAssertTrue(items.allSatisfy { $0.exists }, "missing menu item")
        let positions = items.map { $0.frame.minY }
        XCTAssertEqual(positions, positions.sorted(), "menu out of order: \(positions)")
    }

    func testMoveToUpdatesSubtitle() throws {
        let database = try prepareDatabase([insertBox("DetailMove Box"), insertCard("DetailMove")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailMove")
        openMore(app)
        app.buttons["Move To…"].firstMatch.tap()
        let loose = app.buttons["Loose Cards"].firstMatch
        let target = app.buttons["DetailMove Box"].firstMatch
        XCTAssertTrue(loose.waitForExistence(timeout: 5))
        XCTAssertTrue(target.exists)
        XCTAssertLessThan(loose.frame.minY, target.frame.minY)
        target.tap()
        XCTAssertTrue(app.staticTexts["DetailMove Box"].firstMatch.waitForExistence(timeout: 5), "subtitle not updated")
    }

    func testArchiveConfirmsAndPops() throws {
        let database = try prepareDatabase([insertCard("DetailArchive")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailArchive")
        openMore(app)
        app.buttons["Archive"].firstMatch.tap()
        XCTAssertTrue(app.staticTexts["Archive Card?"].waitForExistence(timeout: 5))
        app.buttons["Archive"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["DetailArchive"].waitForNonExistence(timeout: 10), "detail still shown")
    }

    func testUnarchiveStays() throws {
        let database = try prepareDatabase([insertCard("DetailUnarchive", box: "Archived")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailUnarchive")
        openMore(app)
        XCTAssertFalse(app.buttons["Archive"].firstMatch.exists)
        app.buttons["Unarchive"].firstMatch.tap()
        XCTAssertFalse(app.staticTexts["Archive Card?"].exists)
        XCTAssertTrue(app.navigationBars["DetailUnarchive"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["Loose Cards"].firstMatch.waitForExistence(timeout: 5), "subtitle not updated")
    }

    func testDeleteNamesCardAndCount() throws {
        let database = try prepareDatabase([
            insertCard("DetailDelete"),
            insertRequest(for: "DetailDelete", "Delete Request One"),
            insertRequest(for: "DetailDelete", "Delete Request Two"),
        ])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailDelete")
        openMore(app)
        app.buttons["Delete"].firstMatch.tap()
        XCTAssertTrue(app.staticTexts["Delete “DetailDelete”?"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["This also deletes its 2 prayer requests."].exists)
        // An iPad popover has no Cancel button; tapping outside it cancels.
        let cancel = app.buttons["Cancel"].firstMatch
        if cancel.exists {
            cancel.tap()
        } else {
            app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.98)).tap()
        }
        XCTAssertTrue(app.navigationBars["DetailDelete"].waitForExistence(timeout: 5), "cancel must keep the card")
    }

    func testEmptyState() throws {
        let database = try prepareDatabase([insertCard("DetailEmpty")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        open(app, card: "DetailEmpty")
        XCTAssertTrue(app.staticTexts["No Prayers"].waitForExistence(timeout: 5))
    }

    func testSystemCardMenu() throws {
        let app = launch(database: try prepareDatabase(), arguments: standardArguments(expanded: allSections))
        open(app, card: "Quick Add")
        openMore(app)
        XCTAssertTrue(app.buttons["Move To…"].firstMatch.waitForExistence(timeout: 5))
        for hidden in ["Delete", "Edit", "Archive", "Favorite"] {
            XCTAssertFalse(app.buttons[hidden].firstMatch.exists, "\(hidden) must be hidden")
        }
    }
}
