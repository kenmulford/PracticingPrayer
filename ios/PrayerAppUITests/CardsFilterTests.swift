import SQLite3
import XCTest

final class CardsFilterTests: XCTestCase {
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
            if statement.hasPrefix("UPDATE") {
                XCTAssertGreaterThan(sqlite3_changes(handle), 0, "\(statement) changed no rows")
            }
            sqlite3_free(message)
        }
    }

    private func prepareDatabase(_ statements: [String] = []) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("CardsFilterTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
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

    private func standardArguments(expanded: String = "0", extra: [String] = []) -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", "YES", "-ExpandedSectionIds", expanded] + extra
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

    private func insertTag(_ name: String, _ color: String) -> String {
        "INSERT INTO PrayerTag (Name, Color, CreatedAt, UpdatedAt, IsSystem) VALUES ('\(name)', '\(color)', 1, 1, 0)"
    }

    private func tagRequest(_ tag: String, _ request: String) -> String {
        "INSERT INTO PrayerCardTag (PrayerCardId, PrayerTagId, CreatedAt, PrayerRequestId) "
            + "SELECT PrayerCardId, (SELECT Id FROM PrayerTag WHERE Name = '\(tag)'), 1, Id "
            + "FROM PrayerRequest WHERE Title = '\(request)'"
    }

    /// Two cards, each with one prayer carrying its own tag.
    private func twoTaggedCards(_ test: String) -> [String] {
        [
            insertCard("Alpha \(test)"), insertRequest(for: "Alpha \(test)", "Alpha Prayer \(test)"),
            insertCard("Beta \(test)"), insertRequest(for: "Beta \(test)", "Beta Prayer \(test)"),
            insertTag("TagA \(test)", "#FF0000"), insertTag("TagB \(test)", "#0000FF"),
            tagRequest("TagA \(test)", "Alpha Prayer \(test)"), tagRequest("TagB \(test)", "Beta Prayer \(test)"),
        ]
    }

    // MARK: - Queries

    private func element(_ app: XCUIApplication, label: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    private func element(_ app: XCUIApplication, labelStartingWith prefix: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    private func waitForList(_ app: XCUIApplication) {
        let archived = element(app, labelStartingWith: "Archived · ")
        XCTAssertTrue(archived.waitForExistence(timeout: 20), "card list did not load")
    }

    private func filterButton(_ app: XCUIApplication) -> XCUIElement {
        app.buttons["Filter"].firstMatch
    }

    /// Menu rows expose no label to XCUITest on this runtime, so the selection is seeded with
    /// `-CardsFilterTagIds` and menu rows are only counted or addressed by position.
    /// Fixture tags take ids 1 to 6, so the first tag a test inserts is 7 and the second 8.
    private func seeded(_ ids: String) -> [String] {
        standardArguments(extra: ["-CardsFilterTagIds", ids])
    }

    private func subtitle(_ app: XCUIApplication, _ tag: String) -> XCUIElement {
        element(app, label: "Filtered by \(tag)")
    }

    private func menuOrder(adding names: [String]) -> [String] {
        let others = ["Family", "Fixture Tag 1", "Fixture Tag 2", "Urgent", "Work"] + names
        return ["Recently Notified"] + others.sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    private func openMenu(_ app: XCUIApplication) -> XCUIElementQuery {
        XCTAssertTrue(filterButton(app).waitForExistence(timeout: 10), "Filter button missing")
        let framesBefore = app.collectionViewFrames()
        filterButton(app).tap()
        guard let menu = app.openedMenuCollectionView(excluding: framesBefore) else {
            XCTFail("menu did not open")
            return app.collectionViews.matching(identifier: "menu.missing").cells
        }
        return menu.cells
    }

    // MARK: - Tests

    func testMenuListsTagsAndSelectingNarrows() throws {
        let test = "MenuNarrows"
        let names = ["TagA \(test)", "TagB \(test)"]
        let app = launch(database: try prepareDatabase(twoTaggedCards(test)), arguments: standardArguments())
        waitForList(app)
        XCTAssertTrue(element(app, labelStartingWith: "Beta \(test)").exists)
        XCTAssertFalse(element(app, labelStartingWith: "Filtered by").exists)

        let rows = openMenu(app)
        XCTAssertEqual(rows.count, menuOrder(adding: names).count)
        let index = menuOrder(adding: names).firstIndex(of: names[0])!
        rows.element(boundBy: index).coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()

        XCTAssertTrue(subtitle(app, names[0]).waitForExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "Alpha \(test)").exists)
        XCTAssertFalse(element(app, labelStartingWith: "Beta \(test)").exists)
        XCTAssertEqual(
            openMenu(app).count, menuOrder(adding: names).count + 1,
            "an active filter adds Clear Filter as the first row")
    }

    func testClearFilterRestoresList() throws {
        let test = "ClearRestores"
        let names = ["TagA \(test)", "TagB \(test)"]
        let app = launch(database: try prepareDatabase(twoTaggedCards(test)), arguments: seeded("7"))
        XCTAssertTrue(subtitle(app, names[0]).waitForExistence(timeout: 10))
        XCTAssertFalse(element(app, labelStartingWith: "Beta \(test)").exists)

        let rows = openMenu(app)
        XCTAssertEqual(rows.count, menuOrder(adding: names).count + 1)
        rows.element(boundBy: 0).coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        XCTAssertTrue(element(app, labelStartingWith: "Beta \(test)").waitForExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "Alpha \(test)").exists)
        XCTAssertFalse(element(app, labelStartingWith: "Filtered by").exists)
    }

    func testNoMatchShowsNoMatchingCards() throws {
        let test = "NoMatch"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Alpha \(test)"), insertRequest(for: "Alpha \(test)", "Alpha Prayer \(test)"),
                insertTag("Unused \(test)", "#00FF00"),
            ]),
            arguments: seeded("7"))
        XCTAssertTrue(app.staticTexts["No Matching Cards"].waitForExistence(timeout: 20))
        XCTAssertTrue(filterButton(app).exists)
        app.buttons["Clear Filter"].firstMatch.tap()
        XCTAssertTrue(element(app, labelStartingWith: "Alpha \(test)").waitForExistence(timeout: 10))
    }

    func testOnlySystemTagKeepsFilterEnabled() throws {
        // Opening the database seeds the Recently Notified system tag, so the tag list is never empty.
        let app = launch(
            database: try prepareDatabase(["DELETE FROM PrayerCardTag", "DELETE FROM PrayerTag"]),
            arguments: standardArguments())
        waitForList(app)
        XCTAssertTrue(filterButton(app).waitForExistence(timeout: 10))
        XCTAssertTrue(filterButton(app).isEnabled)
        XCTAssertEqual(openMenu(app).count, 1, "the menu offers only Recently Notified")
    }

    func testTagLoadFailureDisablesFilterKeepsList() throws {
        // Schema upkeep recreates a dropped table on open, so the fault is a row whose date cannot decode.
        let app = launch(
            database: try prepareDatabase(["UPDATE PrayerTag SET CreatedAt = NULL WHERE Name = 'Work'"]),
            arguments: standardArguments())
        waitForList(app)
        XCTAssertTrue(filterButton(app).waitForExistence(timeout: 10))
        XCTAssertFalse(filterButton(app).isEnabled)
    }

    func testLockedCardAbsentWhenFiltered() throws {
        let test = "LockedFiltered"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Protected", protection: 1), insertRequest(for: "Protected", "Protected Prayer \(test)"),
                insertTag("TagP \(test)", "#FF00FF"), tagRequest("TagP \(test)", "Protected Prayer \(test)"),
            ]),
            arguments: standardArguments())
        waitForList(app)
        XCTAssertTrue(element(app, labelStartingWith: "Protected").exists)
        app.terminate()
        app.launchArguments = seeded("7")
        app.launch()
        openCardsTab(app)
        XCTAssertTrue(subtitle(app, "TagP \(test)").waitForExistence(timeout: 20))
        XCTAssertFalse(element(app, labelStartingWith: "Protected").exists)
    }

    func testDeletedTagDroppedOnReload() throws {
        let test = "DeletedTag"
        let database = try prepareDatabase(twoTaggedCards(test))
        let app = launch(database: database, arguments: seeded("7"))
        XCTAssertTrue(subtitle(app, "TagA \(test)").waitForExistence(timeout: 20))

        run(
            [
                "DELETE FROM PrayerCardTag WHERE PrayerTagId = (SELECT Id FROM PrayerTag WHERE Name = 'TagA \(test)')",
                "DELETE FROM PrayerTag WHERE Name = 'TagA \(test)'",
            ], at: database.path)

        let home = app.tabBars.buttons["Home"].exists ? app.tabBars.buttons["Home"] : app.buttons["Home"].firstMatch
        XCTAssertTrue(home.waitForExistence(timeout: 10))
        home.tap()
        openCardsTab(app)
        XCTAssertTrue(element(app, labelStartingWith: "Beta \(test)").waitForExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "Alpha \(test)").exists)
        XCTAssertFalse(element(app, labelStartingWith: "Filtered by").exists)
    }

    func testAX5LongTagNameNotTruncated() throws {
        let test = "AX5Long"
        let name = "A very long tag name that must wrap instead of truncating \(test)"
        let app = launch(
            database: try prepareDatabase([
                insertCard("Alpha \(test)"), insertRequest(for: "Alpha \(test)", "Alpha Prayer \(test)"),
                insertTag(name, "#FF8800"), tagRequest(name, "Alpha Prayer \(test)"),
            ]),
            arguments: standardArguments(extra: [
                "-UIPreferredContentSizeCategoryName", "UICTContentSizeCategoryAccessibilityXXXLarge",
            ]))
        waitForList(app)
        let rows = openMenu(app)
        let order = menuOrder(adding: [name])
        let long = rows.element(boundBy: order.firstIndex(of: name)!)
        let short = rows.element(boundBy: order.firstIndex(of: "Work")!)
        XCTAssertTrue(long.exists && short.exists)
        XCTAssertLessThanOrEqual(long.frame.maxX, app.windows.firstMatch.frame.width)
        XCTAssertGreaterThan(long.frame.height, short.frame.height, "the long name wraps instead of truncating")
    }
}
