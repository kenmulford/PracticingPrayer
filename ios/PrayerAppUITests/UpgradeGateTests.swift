import XCTest

final class UpgradeGateTests: XCTestCase {
    func testUpgradeGate() throws {
        guard let expected = ProcessInfo.processInfo.environment["UPGRADE_GATE_EXPECTED"] else {
            XCTFail("UPGRADE_GATE_EXPECTED not set")
            return
        }
        let app = XCUIApplication()
        app.launchArguments = ["-upgradeGate", expected]
        app.launch()

        let result = app.staticTexts["UpgradeGate_Result"]
        XCTAssertTrue(result.waitForExistence(timeout: 30), "UpgradeGate_Result did not appear")
        // The label starts as "running" and is replaced when the in-app checks finish.
        let done = NSPredicate(format: "label != %@", "running")
        wait(for: [XCTNSPredicateExpectation(predicate: done, object: result)], timeout: 60)
        XCTAssertEqual(result.label, "PASS", result.label)
    }
}
