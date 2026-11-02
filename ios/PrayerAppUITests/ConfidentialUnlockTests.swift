import SQLite3
import XCTest

final class ConfidentialUnlockTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(authStub: String, statements: [String] = []) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("ConfidentialUnlockTests-\(UUID().uuidString).db")
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
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchEnvironment["CONFIDENTIAL_AUTH_STUB"] = authStub
        app.launchArguments = [
            "-ResetTips", "-CollectionsBannerDismissed", "YES", "-ArchivedFolderId", "2",
        ]
        app.launch()
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Cards tab missing")
        tab.tap()
        return app
    }

    private func insertBox(_ name: String, protectAll: Int, mode: Int) -> String {
        "INSERT INTO CardBox (Name, IsSystem, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt) "
            + "VALUES ('\(name)', 0, 10, \(protectAll), \(mode), 1, 1)"
    }

    private func insertCard(_ title: String, protection: Int = 0, box: String) -> String {
        "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, 0, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, \(protection), (SELECT Id FROM CardBox WHERE Name = '\(box)'), PreArchiveBoxId, SystemKey "
            + "FROM PrayerCard WHERE Id = 2"
    }

    private func collectionRow(_ app: XCUIApplication, label: String) -> XCUIElement {
        app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH 'Collections_Row_' AND label == %@", label))
            .firstMatch
    }

    private func createLockedCard(_ app: XCUIApplication, title: String) {
        app.buttons["Add Card"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForExistence(timeout: 10))
        let field = app.textFields["CardEditor_Title"]
        field.tap()
        field.typeText(title)
        app.buttons["Locked"].firstMatch.tap()
        app.buttons["CardEditor_Confirm"].firstMatch.tap()
        XCTAssertTrue(app.navigationBars["New Card"].waitForNonExistence(timeout: 10))
        app.openCollection("Loose Cards")
    }

    private func showConfidential(_ app: XCUIApplication) {
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10))
        more.tap()
        let show = app.buttons["Show Confidential"].firstMatch
        XCTAssertTrue(show.waitForExistence(timeout: 5), "Show Confidential missing")
        show.tap()
    }

    func testStubSuccessRevealsLockedCardAndFlipsMenu() throws {
        let app = try launch(authStub: "success")
        createLockedCard(app, title: "ConfidentialUnlockReveal")
        XCTAssertFalse(app.staticTexts["ConfidentialUnlockReveal"].exists, "title must be masked while locked")
        showConfidential(app)
        XCTAssertTrue(app.staticTexts["ConfidentialUnlockReveal"].waitForExistence(timeout: 10), "title not revealed")
        app.buttons["More"].firstMatch.tap()
        XCTAssertTrue(app.buttons["Hide Confidential"].firstMatch.waitForExistence(timeout: 5))
    }

    func testStubCancelKeepsCardProtected() throws {
        let app = try launch(authStub: "cancel")
        createLockedCard(app, title: "ConfidentialUnlockCancel")
        showConfidential(app)
        XCTAssertFalse(app.staticTexts["ConfidentialUnlockCancel"].waitForExistence(timeout: 3))
        XCTAssertTrue(app.staticTexts["Protected"].firstMatch.exists)
    }

    func testTapTrailingAreaOfLockedRowOpensCardDetail() throws {
        let app = try launch(authStub: "success")
        createLockedCard(app, title: "ConfidentialUnlockTapTrailing")
        let row = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Protected, locked")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertTrue(app.navigationBars["ConfidentialUnlockTapTrailing"].waitForExistence(timeout: 10))
    }

    func testCancelTapTrailingAreaKeepsRowProtected() throws {
        let app = try launch(authStub: "cancel")
        createLockedCard(app, title: "ConfidentialUnlockTapCancel")
        let row = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Protected, locked")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertFalse(app.navigationBars["ConfidentialUnlockTapCancel"].waitForExistence(timeout: 3))
        XCTAssertTrue(row.exists)
    }

    func testLockedCollectionRowAuthsThenPushes() throws {
        let name = "A testLockedCollectionRowAuthsThenPushes"
        let app = try launch(authStub: "success", statements: [
            insertBox(name, protectAll: 1, mode: 1), insertCard("LockedRowCard", box: name),
        ])
        let row = collectionRow(app, label: "\(name), locked")
        XCTAssertTrue(row.waitForExistence(timeout: 10), "locked collection row missing")
        row.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertTrue(app.navigationBars[name].waitForExistence(timeout: 10))
    }

    func testLockedEmptyCollectionShowsLockedRow() throws {
        let name = "A testLockedEmptyCollectionShowsLockedRow"
        let app = try launch(authStub: "success", statements: [insertBox(name, protectAll: 1, mode: 1)])
        XCTAssertTrue(collectionRow(app, label: "\(name), locked").waitForExistence(timeout: 10))
    }

    func testCancelOnLockedCollectionRowStaysOnRoot() throws {
        let name = "A testCancelOnLockedCollectionRowStaysOnRoot"
        let app = try launch(authStub: "cancel", statements: [
            insertBox(name, protectAll: 1, mode: 1), insertCard("CancelRowCard", box: name),
        ])
        let row = collectionRow(app, label: "\(name), locked")
        XCTAssertTrue(row.waitForExistence(timeout: 10))
        row.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertFalse(app.navigationBars[name].waitForExistence(timeout: 3))
        XCTAssertTrue(app.navigationBars["Collections"].exists)
        XCTAssertTrue(row.exists)
    }

    func testHiddenCollectionAbsentWhileLocked() throws {
        let withCard = "A testHiddenCollectionAbsentWhileLocked Card"
        let empty = "A testHiddenCollectionAbsentWhileLocked Empty"
        let app = try launch(authStub: "success", statements: [
            insertBox(withCard, protectAll: 1, mode: 2), insertCard("HiddenRowCard", box: withCard),
            insertBox(empty, protectAll: 1, mode: 2),
        ])
        XCTAssertTrue(app.staticTexts["Archived"].firstMatch.waitForExistence(timeout: 10))
        let leaked = app.descendants(matching: .any).matching(NSPredicate(
            format: "identifier BEGINSWITH 'Collections_Row_' AND (label BEGINSWITH %@ OR label BEGINSWITH %@)",
            withCard, empty))
        XCTAssertEqual(leaked.count, 0)
    }

    func testShowConfidentialRevealsProtectedCollections() throws {
        let hidden = "A testShowConfidentialRevealsProtectedCollections Hidden"
        let locked = "A testShowConfidentialRevealsProtectedCollections Locked"
        let app = try launch(authStub: "success", statements: [
            insertBox(hidden, protectAll: 1, mode: 2), insertCard("RevealHiddenCard", box: hidden),
            insertBox(locked, protectAll: 1, mode: 1), insertCard("RevealLockedCard", box: locked),
        ])
        XCTAssertTrue(collectionRow(app, label: "\(locked), locked").waitForExistence(timeout: 10))
        showConfidential(app)
        XCTAssertTrue(
            app.descendants(matching: .any)
                .matching(NSPredicate(format: "identifier BEGINSWITH 'Collections_Row_' AND label BEGINSWITH %@", hidden))
                .firstMatch.waitForExistence(timeout: 10))
        XCTAssertFalse(collectionRow(app, label: "\(locked), locked").exists)
        app.openCollection(locked)
        XCTAssertTrue(app.navigationBars[locked].waitForExistence(timeout: 10))
    }

    func testUnprotectedCollectionWithLockedCardShowsNormalRow() throws {
        let name = "A testUnprotectedCollectionWithLockedCardShowsNormalRow"
        let app = try launch(authStub: "success", statements: [
            insertBox(name, protectAll: 0, mode: 1), insertCard("UnprotectedLockedCard", protection: 1, box: name),
        ])
        XCTAssertTrue(app.staticTexts["Archived"].firstMatch.waitForExistence(timeout: 10))
        XCTAssertFalse(collectionRow(app, label: "\(name), locked").exists)
        app.openCollection(name)
        XCTAssertTrue(app.navigationBars[name].waitForExistence(timeout: 10))
    }
}
