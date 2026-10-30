import XCTest

final class ClipboardHandoffTests: XCTestCase {
    private static let welcomeTitle = "Welcome to Practicing Prayer"
    private static let shareLink = "https://practicingprayerapp.com/share/r?v=2&d=z.H4sIAAAAAAAAEyXLOwqAQAxF0a2E1BZiI-gG7G1tBub5YTRKJqKDuHdF28s9F9tkM7jiBm6mJVGvE8R3e54XZaQgAGcsqyG-U7vrAE0kOI0OINS0qXvRqvSRIhqcTzQ68fEvfD_PnIc3ZQAAAA"

    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(clipboard: String) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("ClipboardHandoffTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchArguments = [
            "-ResetTips", "-OnboardingStep", "Welcome", "-OnboardingComplete", "NO",
            "-ShareHandoffPrompted", "NO", "-StubClipboard", clipboard,
        ]
        app.launch()
        return app
    }

    func testShareLinkOpensConfirmImport() throws {
        let app = try launch(clipboard: Self.shareLink)
        let prompt = app.alerts["Did someone share a prayer with you?"]
        XCTAssertTrue(prompt.waitForExistence(timeout: 15))
        prompt.buttons["Import"].tap()
        XCTAssertTrue(app.navigationBars["Confirm Import"].waitForExistence(timeout: 15))
        XCTAssertFalse(app.staticTexts[Self.welcomeTitle].exists)
    }

    func testCancelShowsWelcome() throws {
        let app = try launch(clipboard: Self.shareLink)
        let prompt = app.alerts["Did someone share a prayer with you?"]
        XCTAssertTrue(prompt.waitForExistence(timeout: 15))
        prompt.buttons["Cancel"].tap()
        XCTAssertTrue(app.staticTexts[Self.welcomeTitle].waitForExistence(timeout: 15))
    }

    func testForeignURLShowsNothingFound() throws {
        let app = try launch(clipboard: "https://example.com")
        app.alerts["Did someone share a prayer with you?"].buttons["Import"].tap()
        let nothing = app.alerts["No Shared Prayer Found"]
        XCTAssertTrue(nothing.waitForExistence(timeout: 15))
        nothing.buttons["OK"].tap()
        XCTAssertTrue(app.staticTexts[Self.welcomeTitle].waitForExistence(timeout: 15))
    }

    func testEmptyClipboardShowsWelcomeWithoutAlert() throws {
        let app = try launch(clipboard: "")
        XCTAssertTrue(app.staticTexts[Self.welcomeTitle].waitForExistence(timeout: 15))
        XCTAssertFalse(app.alerts.firstMatch.exists)
    }
}
