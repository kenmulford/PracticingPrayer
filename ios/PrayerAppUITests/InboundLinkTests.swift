import XCTest

final class InboundLinkTests: XCTestCase {
    private static let welcomeTitle = "Welcome to Practicing Prayer"
    private static let shareBase = "https://practicingprayerapp.com/share"

    private var fixtureCopy: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        super.tearDown()
    }

    private func launch(link: String) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("InboundLinkTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchArguments = [
            "-ResetTips", "-OnboardingStep", "Welcome", "-OnboardingComplete", "NO", "-InboundShareURL", link,
        ]
        app.launch()
        return app
    }

    func testRequestLinkSkipsWelcome() throws {
        let link = "\(Self.shareBase)/r?v=2&d=z.H4sIAAAAAAAAEyXLOwqAQAxF0a2E1BZiI-gG7G1tBub5YTRKJqKDuHdF28s9F9tkM7jiBm6mJVGvE8R3e54XZaQgAGcsqyG-U7vrAE0kOI0OINS0qXvRqvSRIhqcTzQ68fEvfD_PnIc3ZQAAAA"
        let app = try launch(link: link)
        XCTAssertTrue(app.navigationBars["Confirm Import"].waitForExistence(timeout: 15))
        XCTAssertFalse(app.staticTexts[Self.welcomeTitle].exists)
    }

    func testInvalidCardLinkShowsAlert() throws {
        let app = try launch(link: "\(Self.shareBase)/c?v=2&d=z.AAAA")
        let alert = app.alerts["Unable to Import"]
        XCTAssertTrue(alert.waitForExistence(timeout: 15))
        XCTAssertTrue(alert.staticTexts["The shared prayer card link appears to be invalid or incomplete."].exists)
        XCTAssertTrue(alert.buttons["OK"].exists)
    }

    func testNewerVersionShowsAlert() throws {
        let app = try launch(link: "\(Self.shareBase)/r?v=3&d=x")
        let alert = app.alerts["Unable to Import"]
        XCTAssertTrue(alert.waitForExistence(timeout: 15))
        XCTAssertTrue(alert.staticTexts[
            "This prayer was shared from a newer version of Practicing Prayer. Please update the app to import it."].exists)
    }

    func testForeignLinkShowsWelcome() throws {
        let app = try launch(link: "https://example.com/share/r?title=x")
        XCTAssertTrue(app.staticTexts[Self.welcomeTitle].firstMatch.waitForExistence(timeout: 15))
        XCTAssertFalse(app.alerts["Unable to Import"].exists)
    }
}
