import SQLite3
import XCTest

final class ShareUITests: XCTestCase {
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
            .appendingPathComponent("ShareUITests-\(UUID().uuidString).db")
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

    private func launch(database: URL, tab name: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        let tab = app.tabBars.buttons[name].exists ? app.tabBars.buttons[name] : app.buttons[name].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "\(name) tab missing")
        tab.tap()
        return app
    }

    private func insertCard(_ title: String) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, NULL, PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, "
            + "0, NULL, CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, "
            + "NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, 0 FROM PrayerRequest WHERE Id = 5"
    }

    private func row(_ app: XCUIApplication, _ title: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    func testPrayerDetailHasShare() throws {
        let title = "Share Prayer Detail"
        let app = launch(
            database: try prepareDatabase([insertCard("Card ShareDetail"), insertRequest(for: "Card ShareDetail", title)]),
            tab: "Prayers")
        let element = row(app, title)
        XCTAssertTrue(element.waitForExistence(timeout: 20), "prayer row missing")
        element.tap()
        XCTAssertTrue(app.buttons["Share"].firstMatch.waitForExistence(timeout: 10), "Share missing")
    }

    func testCardDetailMoreOffersShare() throws {
        let app = launch(
            database: try prepareDatabase([
                insertCard("Card ShareMore"), insertRequest(for: "Card ShareMore", "Share More Prayer"),
            ]),
            tab: "Cards")
        let element = row(app, "Card ShareMore")
        XCTAssertTrue(element.waitForExistence(timeout: 20), "card row missing")
        element.tap()
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10), "More missing")
        more.tap()
        XCTAssertTrue(app.buttons["Share"].firstMatch.waitForExistence(timeout: 5), "Share missing in More")
    }

    func testEmptyCardShareIsDisabled() throws {
        let app = launch(database: try prepareDatabase([insertCard("Card ShareEmpty")]), tab: "Cards")
        let element = row(app, "Card ShareEmpty")
        XCTAssertTrue(element.waitForExistence(timeout: 20), "card row missing")
        element.tap()
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10), "More missing")
        more.tap()
        let share = app.buttons["Share"].firstMatch
        XCTAssertTrue(share.waitForExistence(timeout: 5), "Share missing in More")
        XCTAssertFalse(share.isEnabled, "Share must be disabled for an empty card")
    }
}
