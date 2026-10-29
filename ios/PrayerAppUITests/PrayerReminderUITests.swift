import SQLite3
import XCTest

final class PrayerReminderUITests: XCTestCase {
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
            .appendingPathComponent("PrayerReminderUITests-\(UUID().uuidString).db")
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

    private func insertCard(_ title: String) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, 0, NULL, PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(for card: String, _ title: String) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, 0, PrayerFrequency, "
            + "0, NULL, CreatedAt, UpdatedAt, 9, 0, -1, -1, NULL, 0 FROM PrayerRequest WHERE Id = 5"
    }

    private func row(_ app: XCUIApplication, _ title: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    /// Menu rows expose no label to XCUITest on this runtime, so the Repeat choice is addressed by position:
    /// Never, Daily, Weekly, Monthly, Yearly.
    func testEnableWeeklyShowsDetailRow() throws {
        let title = "Reminder Prayer ReminderWeekly"
        let database = try prepareDatabase([
            insertCard("Card ReminderWeekly"), insertRequest(for: "Card ReminderWeekly", title),
        ])
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES", "-AllowNotifications", "NO"]
        app.launch()
        let tab = app.tabBars.buttons["Prayers"].exists ? app.tabBars.buttons["Prayers"] : app.buttons["Prayers"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Prayers tab missing")
        tab.tap()

        let element = row(app, title)
        XCTAssertTrue(element.waitForExistence(timeout: 20), "prayer row missing")
        element.tap()
        let edit = app.buttons["Edit Prayer"].firstMatch
        XCTAssertTrue(edit.waitForExistence(timeout: 10), "Edit missing")
        edit.tap()

        let toggle = app.switches["PrayerEditor_RemindMe"].firstMatch
        XCTAssertTrue(toggle.waitForExistence(timeout: 10), "Remind Me missing")
        toggle.tap()

        let repeatMenu = app.buttons["PrayerEditor_Repeat"].firstMatch
        XCTAssertTrue(repeatMenu.waitForExistence(timeout: 10), "Repeat missing")
        repeatMenu.tap()
        let weekly = app.menuItems.element(boundBy: 2)
        XCTAssertTrue(weekly.waitForExistence(timeout: 5), "Weekly menu row missing")
        weekly.tap()

        app.buttons["PrayerEditor_Confirm"].firstMatch.tap()
        let summary = app.staticTexts["PrayerDetail_Reminder"].firstMatch
        XCTAssertTrue(summary.waitForExistence(timeout: 10), "Reminder row missing")
        XCTAssertTrue(summary.label.contains("Weekly on"), "Reminder row read \(summary.label)")
    }
}
