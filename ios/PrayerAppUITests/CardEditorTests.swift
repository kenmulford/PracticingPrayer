import SQLite3
import XCTest

final class CardEditorTests: XCTestCase {
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
            .appendingPathComponent("CardEditorTests-\(UUID().uuidString).db")
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
        let tab = app.tabBars.buttons["Cards"].exists ? app.tabBars.buttons["Cards"] : app.buttons["Cards"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
    }

    private func standardArguments(expanded: String, showTip: Bool = false) -> [String] {
        ["-ResetTips", "-CollectionsBannerDismissed", showTip ? "NO" : "YES", "-ExpandedSectionIds", expanded,
         "-ArchivedFolderId", "2"]
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

    // MARK: - Editor helpers

    private func openEditor(_ app: XCUIApplication, card title: String) {
        open(app, card: title)
        openMore(app)
        let edit = app.buttons["Edit"].firstMatch
        XCTAssertTrue(edit.waitForExistence(timeout: 5), "Edit missing")
        edit.tap()
        XCTAssertTrue(app.navigationBars["Edit Card"].waitForExistence(timeout: 10), "editor missing")
    }

    private func dismissPopover(_ app: XCUIApplication) {
        let cancel = app.buttons["Cancel"].firstMatch
        if cancel.exists, cancel.identifier != "CardEditor_Cancel" {
            cancel.tap()
        } else {
            app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.98)).tap()
        }
    }

    // MARK: - Tests

    func testAddCardCreates() throws {
        let app = launch(database: try prepareDatabase(), arguments: standardArguments(expanded: allSections))
        waitForList(app)
        app.buttons["Add Card"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForExistence(timeout: 10))
        let save = app.buttons["CardEditor_Confirm"]
        XCTAssertFalse(save.isEnabled, "Save must be disabled until a title is entered")
        let field = app.textFields["CardEditor_Title"]
        field.tap()
        field.typeText("EditorCreated")
        XCTAssertTrue(save.isEnabled)
        save.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForNonExistence(timeout: 10))
        XCTAssertTrue(element(app, labelStartingWith: "EditorCreated").waitForExistence(timeout: 10), "new card row missing")
    }

    func testSaveAndAddAnother() throws {
        let app = launch(database: try prepareDatabase(), arguments: standardArguments(expanded: allSections))
        waitForList(app)
        app.buttons["Add Card"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForExistence(timeout: 10))
        let field = app.textFields["CardEditor_Title"]
        field.tap()
        field.typeText("EditorAnother")
        app.buttons["CardEditor_SaveAndAdd"].tap()
        XCTAssertTrue(app.navigationBars["New Card"].exists, "sheet must stay open")
        let cleared = NSPredicate(format: "value == %@ OR value == %@", "", "Title")
        let wait = XCTNSPredicateExpectation(predicate: cleared, object: field)
        XCTAssertEqual(XCTWaiter().wait(for: [wait], timeout: 5), .completed, "title not cleared")
        XCTAssertFalse(app.buttons["CardEditor_SaveAndAdd"].isEnabled)
    }

    func testEditShowsValues() throws {
        let database = try prepareDatabase([insertCard("EditorShow")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        openEditor(app, card: "EditorShow")
        XCTAssertEqual(app.textFields["CardEditor_Title"].value as? String, "EditorShow")
        XCTAssertTrue(app.buttons["Archive Card"].exists)
        XCTAssertTrue(app.buttons["Delete Card"].exists)
    }

    func testDeleteMessage() throws {
        let database = try prepareDatabase([
            insertCard("EditorZero"),
            insertCard("EditorOne"), insertRequest(for: "EditorOne", "Editor One Request"),
            insertCard("EditorTwo"),
            insertRequest(for: "EditorTwo", "Editor Two A"), insertRequest(for: "EditorTwo", "Editor Two B"),
        ])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        let cases = [
            ("EditorZero", "This card has no prayer requests."),
            ("EditorOne", "This also deletes its 1 prayer request."),
            ("EditorTwo", "This also deletes its 2 prayer requests."),
        ]
        for (index, (title, message)) in cases.enumerated() {
            openEditor(app, card: title)
            app.buttons["Delete Card"].firstMatch.tap()
            XCTAssertTrue(app.staticTexts["Delete “\(title)”?"].waitForExistence(timeout: 5))
            XCTAssertTrue(app.staticTexts[message].exists, "missing: \(message)")
            if index < cases.count - 1 {
                dismissPopover(app)
                app.buttons["CardEditor_Cancel"].tap()
                XCTAssertTrue(app.navigationBars["Edit Card"].waitForNonExistence(timeout: 5))
            } else {
                let confirm = app.buttons.matching(NSPredicate(format: "label == %@", "Delete Card")).allElementsBoundByIndex.last
                confirm?.tap()
                XCTAssertTrue(app.navigationBars[title].waitForNonExistence(timeout: 10), "detail still shown")
            }
        }
    }

    func testDiscardDialog() throws {
        let database = try prepareDatabase([insertCard("EditorDiscard")])
        let app = launch(database: database, arguments: standardArguments(expanded: allSections))
        openEditor(app, card: "EditorDiscard")
        let field = app.textFields["CardEditor_Title"]
        field.tap()
        field.typeText("X")
        app.buttons["CardEditor_Cancel"].tap()
        XCTAssertTrue(app.buttons["Discard Changes"].firstMatch.waitForExistence(timeout: 5))
    }
}
