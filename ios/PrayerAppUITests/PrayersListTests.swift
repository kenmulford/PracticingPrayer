import SQLite3
import XCTest

final class PrayersListTests: XCTestCase {
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
            .appendingPathComponent("PrayersListTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
        return copy
    }

    private func launch(database: URL, extra: [String] = []) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"] + extra
        app.launch()
        let tab = app.tabBars.buttons["Prayers"].exists ? app.tabBars.buttons["Prayers"] : app.buttons["Prayers"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Prayers tab missing")
        tab.tap()
        return app
    }

    private func insertCard(_ title: String, protection: Int = 0) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, \(protection), NULL, PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func insertRequest(
        for card: String, _ title: String, answered: Bool = false, imported: Bool = false
    ) -> String {
        "INSERT INTO PrayerRequest (PrayerCardId, Title, Details, CanNotify, PrayerFrequency, IsAnswered, AnsweredAt, "
            + "CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported) "
            + "SELECT (SELECT Id FROM PrayerCard WHERE Title = '\(card)'), '\(title)', Details, CanNotify, PrayerFrequency, "
            + "\(answered ? 1 : 0), \(answered ? "CreatedAt" : "NULL"), CreatedAt, UpdatedAt, NotifyHour, NotifyMinute, "
            + "NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, \(imported ? 1 : 0) FROM PrayerRequest WHERE Id = 5"
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

    private func row(_ app: XCUIApplication, _ title: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", title)).firstMatch
    }

    private func segment(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.segmentedControls.buttons[name].firstMatch
    }

    private func noResults(_ app: XCUIApplication) -> XCUIElement {
        app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH 'No Results'")).firstMatch
    }

    private func swipeAction(_ app: XCUIApplication, on row: XCUIElement, leading: Bool, button: String) {
        XCTAssertTrue(row.waitForExistence(timeout: 20), "row missing")
        // A full swipe runs the leading action itself, so drag only far enough to reveal the button.
        if leading {
            row.coordinate(withNormalizedOffset: CGVector(dx: 0.1, dy: 0.5))
                .press(forDuration: 0.1, thenDragTo: row.coordinate(withNormalizedOffset: CGVector(dx: 0.4, dy: 0.5)))
        } else {
            row.swipeLeft()
        }
        let action = app.buttons[button].firstMatch
        XCTAssertTrue(action.waitForExistence(timeout: 5), "\(button) swipe action missing")
        action.tap()
    }

    private func clearText(_ field: XCUIElement) {
        guard let value = field.value as? String, !value.isEmpty else { return }
        field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: value.count))
    }

    // MARK: - Tests

    func testActiveSegmentDefaultAndAnsweredMoves() throws {
        let test = "ActiveMoves"
        let title = "Moving Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title)]))
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 20), "active prayer missing under Active")

        segment(app, "Answered").tap()
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 3), "active prayer must not show under Answered")

        segment(app, "Active").tap()
        swipeAction(app, on: row(app, title), leading: true, button: "Mark Answered")
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 3), "row should leave Active")

        segment(app, "Answered").tap()
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 10), "row should appear under Answered")
    }

    func testRowShowsCardAndImportedGlyphInLabel() throws {
        let test = "ImportedLabel"
        let title = "Imported Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title, imported: true),
            ]))
        let element = row(app, title)
        XCTAssertTrue(element.waitForExistence(timeout: 20))
        XCTAssertTrue(element.label.hasPrefix(title), element.label)
        XCTAssertTrue(element.label.contains("Card \(test)"), element.label)
        XCTAssertTrue(element.label.contains("Imported"), element.label)
    }

    func testSearchMatchesCardTitle() throws {
        let test = "SearchCard"
        let title = "Plain Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Zebra \(test)"), insertRequest(for: "Zebra \(test)", title)]))
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 20))

        let field = app.searchFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10), "search field missing")
        field.tap()
        field.typeText("Zebra \(test)")
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 10))

        clearText(field)
        field.typeText("qqzzxx-nothing")
        XCTAssertTrue(noResults(app).waitForExistence(timeout: 10), "search miss view missing")
        XCTAssertFalse(row(app, title).exists)

        clearText(field)
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 10), "clearing search restores the row")
    }

    func testTagFilterNarrows() throws {
        let test = "TagNarrows"
        let tagged = "Tagged Prayer \(test)"
        let plain = "Plain Prayer \(test)"
        // Fixture tags take ids 1 to 6, so the first tag inserted here is 7.
        let app = launch(
            database: try prepareDatabase([
                insertCard("Card \(test)"), insertRequest(for: "Card \(test)", tagged),
                insertRequest(for: "Card \(test)", plain),
                insertTag("Tag \(test)", "#FF0000"), tagRequest("Tag \(test)", tagged),
            ]),
            extra: ["-PrayersFilterTagIds", "7"])
        XCTAssertTrue(row(app, tagged).waitForExistence(timeout: 20))
        XCTAssertFalse(row(app, plain).exists)
    }

    func testDeleteConfirmsAndCancelKeeps() throws {
        let test = "DeleteConfirms"
        let title = "Doomed Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([insertCard("Card \(test)"), insertRequest(for: "Card \(test)", title)]))
        swipeAction(app, on: row(app, title), leading: false, button: "Delete")
        XCTAssertTrue(app.staticTexts["Delete “\(title)”?"].waitForExistence(timeout: 5), "confirmation missing")

        // iPad confirmation dialogs are popovers with no Cancel button; tapping outside dismisses.
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.5)).tap()
        XCTAssertTrue(row(app, title).waitForExistence(timeout: 5), "cancel keeps the row")

        swipeAction(app, on: row(app, title), leading: false, button: "Delete")
        XCTAssertTrue(app.staticTexts["Delete “\(title)”?"].waitForExistence(timeout: 5))
        let confirm = app.popovers.buttons["Delete Prayer"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5), "confirm button missing")
        confirm.tap()
        XCTAssertFalse(row(app, title).waitForExistence(timeout: 3), "row removed after confirm")
    }

    func testLockedCardPrayerAbsent() throws {
        let test = "LockedAbsent"
        let title = "Protected Prayer \(test)"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Locked \(test)", protection: 1), insertRequest(for: "Locked \(test)", title),
                insertCard("Open \(test)"), insertRequest(for: "Open \(test)", "Open Prayer \(test)"),
            ]))
        segment(app, "All").tap()
        XCTAssertTrue(row(app, "Open Prayer \(test)").waitForExistence(timeout: 20))
        XCTAssertFalse(row(app, title).exists)

        let field = app.searchFields.firstMatch
        field.tap()
        field.typeText(title)
        XCTAssertTrue(noResults(app).waitForExistence(timeout: 10))
        XCTAssertFalse(row(app, title).exists)
    }
}
