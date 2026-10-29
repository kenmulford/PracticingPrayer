import SQLite3
import XCTest

final class PrayerEditorTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    // MARK: - Fixture

    private func prepareDatabase(_ statements: [String]) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("PrayerEditorTests-\(UUID().uuidString).db")
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

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        let tab = app.tabBars.buttons["Prayers"].exists ? app.tabBars.buttons["Prayers"] : app.buttons["Prayers"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Prayers tab missing")
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

    private func openPrayer(_ test: String) throws -> (XCUIApplication, String) {
        let title = "Detail Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title)]))
        let element = row(app, title)
        XCTAssertTrue(element.waitForExistence(timeout: 20), "prayer row missing")
        element.tap()
        XCTAssertTrue(app.buttons["Mark Answered"].firstMatch.waitForExistence(timeout: 10), "detail missing")
        return (app, title)
    }

    // MARK: - Tests

    func testEditShowsFilledEditor() throws {
        let (app, title) = try openPrayer("Fill")
        app.buttons["Edit Prayer"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["Edit Prayer"].waitForExistence(timeout: 10), "editor missing")
        XCTAssertEqual(app.textFields["PrayerEditor_Title"].value as? String, title)
    }

    func testEditSavePersistsTitle() throws {
        let (app, _) = try openPrayer("Save")
        app.buttons["Edit Prayer"].firstMatch.tap()
        let field = app.textFields["PrayerEditor_Title"]
        XCTAssertTrue(field.waitForExistence(timeout: 10), "title field missing")
        field.tap()
        field.typeText(" Edited")
        app.buttons["PrayerEditor_Confirm"].firstMatch.tap()
        XCTAssertTrue(
            app.staticTexts["Detail Prayer Save Edited"].firstMatch.waitForExistence(timeout: 10), "new title missing")
    }

    func testDeleteFromEditor() throws {
        let (app, title) = try openPrayer("Remove")
        app.buttons["Edit Prayer"].firstMatch.tap()
        let delete = app.buttons["Delete Prayer"].firstMatch
        XCTAssertTrue(delete.waitForExistence(timeout: 10), "delete button missing")
        delete.tap()
        let confirm = app.popovers.buttons["Delete"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5), "confirm button missing")
        confirm.tap()
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 5), "row removed after delete")
    }
}
