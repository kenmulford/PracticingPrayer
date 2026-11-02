import SQLite3
import XCTest

final class CollectionCardsTests: XCTestCase {
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
            .appendingPathComponent("CollectionCardsTests-\(UUID().uuidString).db")
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

    private func launch(database: URL, expandedCardIds: String? = "none") -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        if let expandedCardIds { app.launchArguments += ["-ExpandedCardIds", expandedCardIds] }
        app.launch()
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
        return app
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

    private func insertRequest(for card: String, _ title: String, answered: Bool = false) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, \(answered ? 1 : 0), "
            + "AnsweredAt, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, "
            + "IsImported FROM PrayerRequest WHERE Id = 5"
    }

    private func element(_ app: XCUIApplication, labelStartingWith prefix: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    // MARK: - Tests

    func testRootTitleIsCollections() throws {
        let app = launch(database: try prepareDatabase())
        XCTAssertTrue(app.navigationBars["Collections"].waitForExistence(timeout: 20))
    }

    func testSearchFieldHittableAtLaunch() throws {
        let app = launch(database: try prepareDatabase())
        let search = app.searchFields["Search Cards"]
        XCTAssertTrue(search.waitForExistence(timeout: 20))
        XCTAssertTrue(search.isHittable, "the search field must be reachable with no swipe")
    }

    func testRowPushesCollectionCards() throws {
        let app = launch(database: try prepareDatabase([
            insertBox("A CollectionCardsPush"), insertCard("PushCard", box: "A CollectionCardsPush"),
        ]))
        app.openCollection("A CollectionCardsPush")
        XCTAssertTrue(app.navigationBars["A CollectionCardsPush"].waitForExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "PushCard").waitForExistence(timeout: 10))
    }

    func testCardsFavoritesFirstThenAlphabetical() throws {
        let app = launch(database: try prepareDatabase([
            insertBox("A CollectionCardsOrder"),
            insertCard("OrderCard Plain", box: "A CollectionCardsOrder"),
            insertCard("OrderCard Zed", favorite: true, box: "A CollectionCardsOrder"),
        ]))
        app.openCollection("A CollectionCardsOrder")
        let favorite = element(app, labelStartingWith: "OrderCard Zed")
        let plain = element(app, labelStartingWith: "OrderCard Plain")
        XCTAssertTrue(favorite.waitForExistence(timeout: 10))
        XCTAssertTrue(plain.exists)
        XCTAssertLessThan(favorite.frame.minY, plain.frame.minY)
    }

    func testEmptyCollectionShowsNoCards() throws {
        let app = launch(database: try prepareDatabase([insertBox("A CollectionCardsEmpty")]))
        app.openCollection("A CollectionCardsEmpty")
        XCTAssertTrue(app.staticTexts["No Cards"].waitForExistence(timeout: 10))
    }

    func testToolbarOrder() throws {
        let app = launch(database: try prepareDatabase([insertBox("A CollectionCardsToolbar")]))
        app.openCollection("A CollectionCardsToolbar")
        let pray = app.buttons["Pray"].firstMatch
        let more = app.buttons["More"].firstMatch
        let add = app.buttons["Add Card"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10))
        XCTAssertTrue(pray.exists)
        XCTAssertTrue(more.exists)
        XCTAssertLessThan(pray.frame.minX, more.frame.minX)
        XCTAssertLessThan(more.frame.minX, add.frame.minX)
    }

    func testPrayStartsCollectionSession() throws {
        let app = launch(database: try prepareDatabase([
            insertBox("A CollectionCardsPray"), insertCard("PrayCard", box: "A CollectionCardsPray"),
            insertRequest(for: "PrayCard", "Pray Request"),
        ]))
        app.openCollection("A CollectionCardsPray")
        let pray = app.buttons["Pray"].firstMatch
        XCTAssertTrue(pray.waitForExistence(timeout: 10))
        XCTAssertTrue(pray.isEnabled)
        pray.tap()
        XCTAssertTrue(app.descendants(matching: .any)["PrayerSession_Title"].waitForExistence(timeout: 10))
    }

    func testPrayStartsLooseCardsSession() throws {
        let app = launch(database: try prepareDatabase([
            insertCard("LooseCard"), insertRequest(for: "LooseCard", "Loose Request"),
        ]))
        app.openCollection("Loose Cards")
        let pray = app.buttons["Pray"].firstMatch
        XCTAssertTrue(pray.waitForExistence(timeout: 10))
        XCTAssertTrue(pray.isEnabled)
        pray.tap()
        XCTAssertTrue(app.descendants(matching: .any)["PrayerSession_Title"].waitForExistence(timeout: 10))
    }

    func testPrayDisabledOnEmptyCollection() throws {
        let app = launch(database: try prepareDatabase([insertBox("A CollectionCardsNoPray")]))
        app.openCollection("A CollectionCardsNoPray")
        let pray = app.buttons["Pray"].firstMatch
        XCTAssertTrue(pray.waitForExistence(timeout: 10))
        XCTAssertFalse(pray.isEnabled)
        XCTAssertTrue(app.buttons["Add Card"].firstMatch.isEnabled)
    }

    func testAddCardPreselectsCollection() throws {
        let app = launch(database: try prepareDatabase([insertBox("A CollectionCardsAdd")]))
        app.openCollection("A CollectionCardsAdd")
        let add = app.buttons["Add Card"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10))
        add.tap()
        let picker = app.descendants(matching: .any)["CardEditor_Collection"].firstMatch
        XCTAssertTrue(picker.waitForExistence(timeout: 10))
        let text = "\(picker.label) \(picker.value as? String ?? "")"
        XCTAssertTrue(text.contains("A CollectionCardsAdd"), "collection not preselected: \(text)")
    }

    // MARK: - Card tiles

    private func tileFixture(_ suffix: String, answered: Bool = false) -> [String] {
        let box = "A Tile\(suffix)", card = "Tile Card \(suffix)"
        var statements = [insertBox(box), insertCard(card, box: box), insertRequest(for: card, "Tile Prayer \(suffix)")]
        if answered { statements.append(insertRequest(for: card, "Tile Answered \(suffix)", answered: true)) }
        return statements
    }

    func testTileExpandsAndCollapses() throws {
        let app = launch(database: try prepareDatabase(tileFixture("Toggle")))
        app.openCollection("A TileToggle")
        let expand = app.buttons["Expand Tile Card Toggle"]
        XCTAssertTrue(expand.waitForExistence(timeout: 10))
        expand.tap()
        XCTAssertTrue(app.buttons["Collapse Tile Card Toggle"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.staticTexts["Tile Prayer Toggle"].waitForExistence(timeout: 10))
        app.buttons["Collapse Tile Card Toggle"].tap()
        XCTAssertTrue(app.buttons["Expand Tile Card Toggle"].waitForExistence(timeout: 10))
        XCTAssertFalse(app.staticTexts["Tile Prayer Toggle"].exists)
    }

    func testTileHeaderOpensCardDetail() throws {
        let app = launch(database: try prepareDatabase(tileFixture("Header")))
        app.openCollection("A TileHeader")
        let header = element(app, labelStartingWith: "Tile Card Header")
        XCTAssertTrue(header.waitForExistence(timeout: 10))
        header.tap()
        XCTAssertTrue(app.navigationBars["Tile Card Header"].waitForExistence(timeout: 10))
    }

    func testTileAddPrayerOpensEditor() throws {
        let app = launch(database: try prepareDatabase(tileFixture("AddPrayer")))
        app.openCollection("A TileAddPrayer")
        app.buttons["Expand Tile Card AddPrayer"].tap()
        let add = app.buttons["Add Prayer"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10))
        add.tap()
        XCTAssertTrue(app.navigationBars["New Prayer"].waitForExistence(timeout: 10))
    }

    func testTileExpansionSurvivesRelaunch() throws {
        let database = try prepareDatabase(tileFixture("Relaunch"))
        let first = launch(database: database)
        first.openCollection("A TileRelaunch")
        first.buttons["Expand Tile Card Relaunch"].tap()
        XCTAssertTrue(first.buttons["Collapse Tile Card Relaunch"].waitForExistence(timeout: 10))
        first.terminate()
        let second = launch(database: database, expandedCardIds: nil)
        second.openCollection("A TileRelaunch")
        XCTAssertTrue(second.buttons["Collapse Tile Card Relaunch"].waitForExistence(timeout: 10))
    }

    func testQuickAddTileBarIsPrayAndShare() throws {
        let app = launch(database: try prepareDatabase())
        app.openCollection("System")
        let expand = element(app, labelStartingWith: "Expand Quick Add")
        XCTAssertTrue(expand.waitForExistence(timeout: 10))
        expand.tap()
        XCTAssertTrue(app.buttons["Share"].firstMatch.waitForExistence(timeout: 10))
        XCTAssertFalse(app.buttons["Edit"].exists)
        XCTAssertFalse(app.buttons["Favorite"].exists)
    }

    func testLockedCardHasNoDisclosure() throws {
        let box = "A TileLocked", card = "Tile Card Locked"
        let app = launch(database: try prepareDatabase([insertBox(box), insertCard(card, protection: 1, box: box)]))
        app.openCollection(box)
        XCTAssertTrue(element(app, labelStartingWith: "Protected").waitForExistence(timeout: 10))
        XCTAssertFalse(element(app, labelStartingWith: "Expand").exists)
    }

    func testTileAnsweredRowCollapsed() throws {
        let app = launch(database: try prepareDatabase(tileFixture("Answered", answered: true)))
        app.openCollection("A TileAnswered")
        app.buttons["Expand Tile Card Answered"].tap()
        XCTAssertTrue(app.staticTexts["Tile Prayer Answered"].waitForExistence(timeout: 10))
        XCTAssertFalse(app.staticTexts["Tile Answered Answered"].exists)
    }

    func testLoadFailureShowsTryAgain() throws {
        // Schema upkeep recreates a dropped table on open, so the fault is a row whose date cannot decode.
        let app = launch(database: try prepareDatabase(["UPDATE PrayerCardTag SET CreatedAt = NULL"]))
        XCTAssertTrue(app.staticTexts["Can't Load Prayer Cards"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Try Again"].exists)
    }
}
