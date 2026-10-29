import SQLite3
import XCTest

final class TagEditorTests: XCTestCase {
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
            .appendingPathComponent("TagEditorTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        run(statements, at: copy.path)
        return copy
    }

    private func insertTag(_ name: String, _ color: String) -> String {
        "INSERT INTO PrayerTag (Name, Color, CreatedAt, UpdatedAt, IsSystem) VALUES ('\(name)', '\(color)', 1, 1, 0)"
    }

    private func launch(database: URL) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = database.path
        app.launchArguments = ["-ResetTips", "-CollectionsBannerDismissed", "YES"]
        app.launch()
        let tab = app.tabBars.buttons["Tags"].exists ? app.tabBars.buttons["Tags"] : app.buttons["Tags"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 20), "Tags tab missing")
        tab.tap()
        return app
    }

    private func row(_ app: XCUIApplication, _ name: String) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: "label BEGINSWITH %@", name)).firstMatch
    }

    private func clearAndTypeText(_ text: String, in field: XCUIElement) {
        field.tap()
        if let current = field.value as? String, !current.isEmpty {
            field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count))
        }
        field.typeText(text)
    }

    private func openNewTag(_ app: XCUIApplication) -> XCUIElement {
        let add = app.buttons["New Tag"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10))
        add.tap()
        let field = app.textFields["TagEditor_Name"]
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        return field
    }

    // MARK: - Tests

    func testNewTagCreatesRow() throws {
        let app = launch(database: try prepareDatabase())
        clearAndTypeText("Tag EditorTestsNew", in: openNewTag(app))
        app.buttons["TagEditor_Confirm"].tap()
        XCTAssertTrue(row(app, "Tag EditorTestsNew").waitForExistence(timeout: 10))
    }

    func testConfirmDisabledForBlankName() throws {
        let app = launch(database: try prepareDatabase())
        clearAndTypeText("   ", in: openNewTag(app))
        XCTAssertFalse(app.buttons["TagEditor_Confirm"].isEnabled)
    }

    func testSwipeEditRenames() throws {
        let app = launch(database: try prepareDatabase([insertTag("Tag EditorTestsEdit", "#336699")]))
        let target = row(app, "Tag EditorTestsEdit")
        XCTAssertTrue(target.waitForExistence(timeout: 10))
        target.swipeLeft()
        app.buttons["Edit"].firstMatch.tap()
        let field = app.textFields["TagEditor_Name"]
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        clearAndTypeText("Tag EditorTestsRenamed", in: field)
        app.buttons["TagEditor_Confirm"].tap()
        XCTAssertTrue(row(app, "Tag EditorTestsRenamed").waitForExistence(timeout: 10))
    }

    func testDeleteTagFromEditor() throws {
        let app = launch(database: try prepareDatabase([insertTag("Tag EditorTestsDelete", "#336699")]))
        let target = row(app, "Tag EditorTestsDelete")
        XCTAssertTrue(target.waitForExistence(timeout: 10))
        target.swipeLeft()
        app.buttons["Edit"].firstMatch.tap()
        let delete = app.buttons["TagEditor_DeleteTag"]
        XCTAssertTrue(delete.waitForExistence(timeout: 5))
        delete.tap()
        let confirm = app.buttons["Delete Tag"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.tap()
        XCTAssertTrue(target.waitForNonExistence(timeout: 10))
    }
}
