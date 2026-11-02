import SQLite3
import XCTest

final class PrayerTimeEntryPointsTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func prepareDatabase(_ statements: [String]) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("PrayerTimeEntryPointsTests-\(UUID().uuidString).db")
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

    private func insertCollection(_ name: String) -> String {
        "INSERT INTO CardBox (Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES ('\(name)', 0, 10, 0, 0, 1, 1)"
    }

    private func insertCard(_ title: String, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, 0, "
            + "AnsweredAt, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, "
            + "IsImported FROM PrayerRequest WHERE Id = 5"
    }

    private func element(_ app: XCUIApplication, labelStartingWith prefix: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    func testCardDetailPrayOpensSession() throws {
        let card = "Pray Card testCardDetailPrayOpensSession"
        let database = try prepareDatabase([insertCard(card), insertRequest(for: card, "Pray request")])
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launch()
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
        app.openCollection("Loose Cards")
        let row = element(app, labelStartingWith: card)
        XCTAssertTrue(row.waitForExistence(timeout: 20), "card row missing")
        row.tap()
        let pray = app.buttons["Pray"].firstMatch
        XCTAssertTrue(pray.waitForExistence(timeout: 10), "Pray button missing")
        pray.tap()
        XCTAssertTrue(
            app.descendants(matching: .any)["PrayerSession_Title"].waitForExistence(timeout: 10),
            "session did not open")
    }

    func testCollectionRowMenuStartsPrayerTime() throws {
        let collection = "Menu Collection testCollectionRowMenuStartsPrayerTime"
        let card = "Menu Card testCollectionRowMenuStartsPrayerTime"
        let database = try prepareDatabase([
            insertCollection(collection), insertCard(card, box: collection),
            insertRequest(for: card, "Menu request testCollectionRowMenuStartsPrayerTime"),
        ])
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launch()
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
        let row = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH 'Collections_Row_' AND label BEGINSWITH %@", collection))
            .firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 20), "collection row missing")
        let framesBefore = app.collectionViewFrames()
        row.press(forDuration: 1.0)
        let menu = try XCTUnwrap(app.openedMenuCollectionView(excluding: framesBefore), "Row menu did not open")
        let prayerTime = menu.cells.element(boundBy: 0)
        XCTAssertTrue(prayerTime.waitForExistence(timeout: 5), "Prayer Time menu row missing")
        prayerTime.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        XCTAssertTrue(
            app.descendants(matching: .any)["PrayerSession_Title"].waitForExistence(timeout: 10),
            "session did not open")
    }
}
