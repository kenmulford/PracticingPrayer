import SQLite3
import XCTest

final class BackupTests: XCTestCase {
    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func prepareDatabase(_ statements: [String] = []) throws -> URL {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("BackupTests-\(UUID().uuidString).db")
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

    private func insertCard(_ title: String, favorite: Bool = false, protection: Int = 0, box: String? = nil) -> String {
        let boxId = box.map { "(SELECT Id FROM CardBox WHERE Name = '\($0)')" } ?? "NULL"
        return "INSERT INTO PrayerCard (Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey) "
            + "SELECT '\(title)', CanNotify, PrayerFrequency, IsAnswered, \(favorite ? 1 : 0), CreatedAt, UpdatedAt, "
            + "IsSystem, IsImported, \(protection), \(boxId), PreArchiveBoxId, SystemKey FROM PrayerCard WHERE Id = 2"
    }

    private func openSettings(_ app: XCUIApplication) {
        let tab = app.buttons["Settings"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 15))
        tab.tap()
    }

    private func openBackup(statements: [String] = []) throws -> XCUIApplication {
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = try prepareDatabase(statements).path
        app.launch()
        openSettings(app)
        let row = app.descendants(matching: .any)["settings.row.backup"]
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        return app
    }

    private func startProtectedBackup(_ app: XCUIApplication) {
        app.buttons["backup.backUpNow"].firstMatch.tap()
        let choice = app.buttons["backup.protectChoice"].firstMatch
        XCTAssertTrue(choice.waitForExistence(timeout: 10))
        choice.tap()
    }

    func testNoProtectedCardsOpensShareSheet() throws {
        let app = try openBackup()
        app.buttons["backup.backUpNow"].firstMatch.tap()
        XCTAssertFalse(app.buttons["backup.protectChoice"].exists)
        XCTAssertTrue(app.otherElements["ActivityListView"].waitForExistence(timeout: 15))
    }

    func testProtectedCardsShowDialog() throws {
        let app = try openBackup(statements: [insertCard("BackupProtected", protection: 1)])
        app.buttons["backup.backUpNow"].firstMatch.tap()
        XCTAssertTrue(app.buttons["backup.protectChoice"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["backup.unencryptedChoice"].exists)
    }

    func testPassphraseLengthGate() throws {
        let app = try openBackup(statements: [insertCard("BackupGate", protection: 1)])
        startProtectedBackup(app)
        let field = app.alerts["Protect with a Passphrase"].secureTextFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        field.typeText("abcdefghijk")
        XCTAssertFalse(app.buttons["backup.protectBackup"].firstMatch.isEnabled)
        field.typeText("l")
        XCTAssertTrue(app.buttons["backup.protectBackup"].firstMatch.isEnabled)
    }

    func testCancelClearsField() throws {
        let app = try openBackup(statements: [insertCard("BackupCancel", protection: 1)])
        startProtectedBackup(app)
        let field = app.alerts["Protect with a Passphrase"].secureTextFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.tap()
        field.typeText("abcdefghijkl")
        app.alerts.buttons["Cancel"].tap()

        startProtectedBackup(app)
        let reopened = app.alerts["Protect with a Passphrase"].secureTextFields.firstMatch
        XCTAssertTrue(reopened.waitForExistence(timeout: 5))
        XCTAssertTrue((reopened.value as? String ?? "").isEmpty || reopened.value as? String == "Passphrase (12+ characters)")
    }
}
