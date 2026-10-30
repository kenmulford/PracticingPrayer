import XCTest

/// A session restored from the 1.7.2 MAUI build names `SceneDelegate`; `-forceLegacyScene` routes a fresh launch
/// through that delegate so SwiftUI's scene lifecycle (scenePhase) is exercised on the same path.
final class LegacySceneTests: XCTestCase {
    private var fixtureCopy: URL?
    private var pendingDir: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        if let pendingDir { try? FileManager.default.removeItem(at: pendingDir) }
        super.tearDown()
    }

    func testLegacySceneConsumesPendingImportAndLiftsPrivacyCover() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let dbCopy = FileManager.default.temporaryDirectory
            .appendingPathComponent("LegacySceneTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(
            at: root.appendingPathComponent("contracts/db/long-upgraded.db"), to: dbCopy)
        fixtureCopy = dbCopy
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("LegacySceneTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        pendingDir = dir
        let payload = dir.appendingPathComponent("pending-import.json")
        try FileManager.default.copyItem(
            at: root.appendingPathComponent("contracts/app-group/pending-import.golden.json"), to: payload)

        let app = XCUIApplication()
        app.launchArguments += ["-forceLegacyScene"]
        app.launchEnvironment["PRAYER_DB_PATH"] = dbCopy.path
        app.launchEnvironment["PENDING_IMPORT_DIR"] = dir.path
        app.launch()

        let cancel = app.buttons["QuickAdd_Cancel"].firstMatch
        XCTAssertTrue(cancel.waitForExistence(timeout: 15))
        XCTAssertFalse(FileManager.default.fileExists(atPath: payload.path))
        // No fixture card is protected, so the cover shows only while the scene is not active.
        XCTAssertFalse(app.otherElements["PrivacyCover_Root"].exists)
        cancel.tap()
        XCTAssertTrue(cancel.waitForNonExistence(timeout: 3))
    }
}
