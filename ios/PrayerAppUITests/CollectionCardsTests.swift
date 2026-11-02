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

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
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

    private func insertCard(_ title: String, favorite: Bool = false, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, \(favorite ? 1 : 0), CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
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

    func testLoadFailureShowsTryAgain() throws {
        // Schema upkeep recreates a dropped table on open, so the fault is a row whose date cannot decode.
        let app = launch(database: try prepareDatabase(["UPDATE PrayerCardTag SET CreatedAt = NULL"]))
        XCTAssertTrue(app.staticTexts["Can't Load Prayer Cards"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Try Again"].exists)
    }
}
