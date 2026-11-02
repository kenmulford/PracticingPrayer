import SQLite3
import XCTest

final class CardsListTests: XCTestCase {
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
            .appendingPathComponent("CardsListTests-\(UUID().uuidString).db")
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

    private func standardArguments(showTip: Bool = false) -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", showTip ? "NO" : "YES"]
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

    private func collectionRow(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH 'Collections_Row_' AND label BEGINSWITH %@", name))
            .firstMatch
    }

    private func waitForList(_ app: XCUIApplication) {
        XCTAssertTrue(collectionRow(app, "Archived").waitForExistence(timeout: 20), "collections list did not load")
    }

    // MARK: - Tests

    func testSectionsInOrderWithFavoritesFirst() throws {
        let database = try prepareDatabase([
            insertBox("A CardsListOrder"), insertBox("B CardsListOrder"),
            insertCard("OrderCard Plain", box: "A CardsListOrder"),
            insertCard("OrderCard Zed", favorite: true, box: "A CardsListOrder"),
            insertCard("OrderCard B", box: "B CardsListOrder"),
        ])
        let app = launch(database: database, arguments: standardArguments())
        waitForList(app)

        let names = ["Loose Cards", "A CardsListOrder", "B CardsListOrder", "System", "Archived"]
        XCTAssertTrue(names.allSatisfy { collectionRow(app, $0).exists })
        let positions = names.map { collectionRow(app, $0).frame.minY }
        XCTAssertEqual(positions, positions.sorted(), "rows out of order: \(positions)")

        app.openCollection("A CardsListOrder")
        let favorite = element(app, labelStartingWith: "OrderCard Zed")
        let plain = element(app, labelStartingWith: "OrderCard Plain")
        XCTAssertTrue(favorite.exists && plain.exists)
        XCTAssertLessThan(favorite.frame.minY, plain.frame.minY)
    }

    func testRowShowsStarAndCount() throws {
        let database = try prepareDatabase([
            insertCard("RowCard", favorite: true),
            insertRequest(for: "RowCard", "Row Request One"),
            insertRequest(for: "RowCard", "Row Request Two"),
        ])
        let app = launch(database: database, arguments: standardArguments())
        waitForList(app)
        app.openCollection("Loose Cards")
        XCTAssertTrue(element(app, label: "RowCard, Favorite, 2 active prayers").waitForExistence(timeout: 10))
    }

    func testLockedRowMaskedAndHiddenAbsent() throws {
        let database = try prepareDatabase([
            insertCard("LockedCardTitle", protection: 1),
            insertRequest(for: "LockedCardTitle", "Locked Request"),
            insertCard("HiddenCardTitle", protection: 2),
        ])
        let app = launch(database: database, arguments: standardArguments())
        waitForList(app)
        app.openCollection("Loose Cards")
        XCTAssertTrue(element(app, label: "Protected").waitForExistence(timeout: 10), "masked row shows Protected with no count")
        XCTAssertFalse(element(app, labelStartingWith: "LockedCardTitle").exists)
        XCTAssertFalse(element(app, labelStartingWith: "HiddenCardTitle").exists)
    }

    func testSearchNarrowsAndClearRestores() throws {
        let database = try prepareDatabase([insertCard("SearchNeedleCard")])
        let app = launch(database: database, arguments: standardArguments())
        waitForList(app)
        XCTAssertFalse(element(app, labelStartingWith: "SearchNeedleCard").exists)

        let search = app.searchFields["Search Cards"]
        search.tap()
        search.typeText("Needle")
        XCTAssertTrue(element(app, labelStartingWith: "SearchNeedleCard").waitForExistence(timeout: 5))
        XCTAssertFalse(element(app, labelStartingWith: "General").exists)

        app.buttons["Clear text"].tap()
        XCTAssertTrue(collectionRow(app, "Archived").waitForExistence(timeout: 5))
        XCTAssertFalse(element(app, labelStartingWith: "SearchNeedleCard").exists)
    }

    func testSearchNoMatchShowsUnavailable() throws {
        let app = launch(database: try prepareDatabase(), arguments: standardArguments())
        waitForList(app)
        let search = app.searchFields["Search Cards"]
        search.tap()
        search.typeText("zzqqxxnomatch")
        XCTAssertTrue(element(app, labelStartingWith: "No Results").waitForExistence(timeout: 10))
    }

    func testEmptyDatabaseShowsNoPrayerCards() throws {
        let database = try prepareDatabase(["DELETE FROM PrayerCard"])
        let app = launch(database: database, arguments: standardArguments())
        XCTAssertTrue(element(app, label: "No Prayer Cards").waitForExistence(timeout: 20))
        XCTAssertTrue(app.navigationBars.buttons["Add Card"].exists)
    }

    func testLoadFailureShowsTryAgain() throws {
        // Schema upkeep recreates a dropped table on open, so the fault is a row whose date cannot decode.
        let database = try prepareDatabase(["UPDATE PrayerCardTag SET CreatedAt = NULL"])
        let app = launch(database: database, arguments: standardArguments())
        XCTAssertTrue(app.staticTexts["Can't Load Prayer Cards"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Try Again"].exists)
        XCTAssertFalse(element(app, labelStartingWith: "General").exists)
    }

    func testTipShowsOnceAndCloses() throws {
        let database = try prepareDatabase()
        let app = launch(database: database, arguments: standardArguments(showTip: true))
        waitForList(app)
        let close = app.buttons["Close"].firstMatch
        XCTAssertTrue(close.waitForExistence(timeout: 10))
        close.tap()
        XCTAssertTrue(close.waitForNonExistence(timeout: 10))

        app.terminate()
        app.launchArguments = []
        app.launch()
        openCardsTab(app)
        waitForList(app)
        XCTAssertFalse(app.buttons["Close"].exists)
    }

    func testSelectingRowShowsDetailPlaceholder() throws {
        let app = launch(database: try prepareDatabase(), arguments: standardArguments())
        waitForList(app)
        app.openCollection("Loose Cards")
        XCTAssertTrue(app.staticTexts["Select a Prayer Card"].waitForExistence(timeout: 5))
        let row = element(app, labelStartingWith: "Fixture Card 1")
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        XCTAssertTrue(app.navigationBars["Fixture Card 1"].waitForExistence(timeout: 5))
    }
}
