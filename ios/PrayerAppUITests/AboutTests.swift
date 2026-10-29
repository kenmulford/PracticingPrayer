import XCTest

final class AboutTests: XCTestCase {
    private var fixtureCopy: URL?
    private var diagnosticsDirectory: URL?

    override func tearDown() {
        if let fixtureCopy { try? FileManager.default.removeItem(at: fixtureCopy) }
        if let diagnosticsDirectory { try? FileManager.default.removeItem(at: diagnosticsDirectory) }
        fixtureCopy = nil
        diagnosticsDirectory = nil
        super.tearDown()
    }

    private func launchWithFixture(arguments: [String] = []) throws -> XCUIApplication {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/db/long-upgraded.db")
        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("AboutTests-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: source, to: copy)
        fixtureCopy = copy
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("AboutTests-diagnostics-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        diagnosticsDirectory = directory

        let app = XCUIApplication()
        app.launchEnvironment["PRAYER_DB_PATH"] = copy.path
        app.launchEnvironment["PRAYER_DIAGNOSTICS_DIR"] = directory.path
        app.launchArguments = arguments
        app.launch()
        return app
    }

    private func openAbout(_ app: XCUIApplication) {
        let tab = app.buttons["Settings"].firstMatch
        XCTAssertTrue(tab.waitForExistence(timeout: 15))
        tab.tap()
        let row = control(app, "settings.row.about")
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        XCTAssertTrue(app.navigationBars["About"].waitForExistence(timeout: 5))
    }

    private func control(_ app: XCUIApplication, _ id: String) -> XCUIElement {
        app.descendants(matching: .any)[id]
    }

    func testHeaderShowsNameVersionDescription() throws {
        let app = try launchWithFixture()
        openAbout(app)
        let header = control(app, "about.header")
        XCTAssertTrue(header.waitForExistence(timeout: 5))
        XCTAssertTrue(header.label.contains("Practicing Prayer"), header.label)
        XCTAssertTrue(header.label.contains("Version 2.0.0 (1)"), header.label)
        XCTAssertTrue(header.label.contains("A personal worship tool"), header.label)
    }

    func testLinkRowsShown() throws {
        let app = try launchWithFixture()
        openAbout(app)
        for id in ["about.privacy", "about.website"] {
            let element = control(app, id)
            XCTAssertTrue(element.waitForExistence(timeout: 5), "\(id) missing")
            XCTAssertTrue(element.isEnabled, "\(id) disabled")
        }
    }

    func testPrivacyOpensBrowser() throws {
        let app = try launchWithFixture()
        openAbout(app)
        let link = control(app, "about.privacy")
        XCTAssertTrue(link.waitForExistence(timeout: 5))
        link.tap()
        let safari = XCUIApplication(bundleIdentifier: "com.apple.mobilesafari")
        XCTAssertTrue(safari.wait(for: .runningForeground, timeout: 15))
        safari.terminate()
    }

    func testSendRowHiddenWithoutLog() throws {
        let app = try launchWithFixture()
        openAbout(app)
        XCTAssertTrue(control(app, "about.privacy").waitForExistence(timeout: 5))
        XCTAssertFalse(control(app, "about.sendDiagnostics").exists)
    }

    func testSendRowShownWithSeededLog() throws {
        let app = try launchWithFixture(arguments: ["-seedDiagnosticLog"])
        openAbout(app)
        XCTAssertTrue(control(app, "about.sendDiagnostics").waitForExistence(timeout: 5))
    }
}
