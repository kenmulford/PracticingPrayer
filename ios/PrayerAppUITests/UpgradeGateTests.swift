import XCTest

final class UpgradeGateTests: XCTestCase {
    private static let tips = [
        "Create Your First Prayer Card", "Add a Prayer Request", "Pray through your requests one by one.",
        "Give it a name \u{2014} a person, a topic, or a place.",
    ]

    func testUpgradeGate() throws {
        let environment = ProcessInfo.processInfo.environment
        guard let expected = environment["UPGRADE_GATE_EXPECTED"],
              let reminders = environment["UPGRADE_GATE_REMINDERS"],
              let importText = environment["UPGRADE_GATE_IMPORT_TEXT"]
        else {
            XCTFail("UPGRADE_GATE_EXPECTED, UPGRADE_GATE_REMINDERS or UPGRADE_GATE_IMPORT_TEXT not set")
            return
        }

        // Phase 1: first native launch over the 1.7.2 install.
        let app = XCUIApplication()
        app.launchEnvironment["CONFIDENTIAL_AUTH_STUB"] = "passcodeNotSet"
        app.launch()

        let cancel = app.buttons["QuickAdd_Cancel"]
        XCTAssertTrue(cancel.waitForExistence(timeout: 30), "Confirm Import not shown")
        let imported = app.descendants(matching: .any).matching(
            NSPredicate(format: "label CONTAINS %@ OR value CONTAINS %@", importText, importText)).firstMatch
        XCTAssertTrue(imported.waitForExistence(timeout: 10), "import text not shown")
        XCTAssertFalse(app.staticTexts["Welcome to Practicing Prayer"].exists, "Welcome shown after upgrade")
        cancel.tap()
        XCTAssertTrue(app.buttons["Home_PrayerTimeButton"].waitForExistence(timeout: 15), "Home missing")
        assertNoTips(app, on: "Home")
        openCards(app)
        assertNoTips(app, on: "Cards")

        // Phase 2: the 1.7.2 PIN reveals the confidential card. Sections start collapsed after an upgrade, so the
        // card's section is opened first; otherwise its title is hidden whether or not the PIN verifies.
        let looseCards = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Loose Cards")).firstMatch
        XCTAssertTrue(looseCards.waitForExistence(timeout: 10), "Loose Cards section missing")
        if looseCards.images["collapsed"].exists { looseCards.tap() }
        let more = app.buttons["More"].firstMatch
        XCTAssertTrue(more.waitForExistence(timeout: 10), "More missing")
        more.tap()
        let show = app.buttons["Show Confidential"].firstMatch
        XCTAssertTrue(show.waitForExistence(timeout: 5), "Show Confidential missing")
        show.tap()
        XCTAssertTrue(app.navigationBars["Enter PIN"].waitForExistence(timeout: 10), "Enter PIN sheet missing")
        let entry = app.secureTextFields["EnterPin_Field"]
        entry.typeText("000000")
        XCTAssertTrue(app.staticTexts["UpgradeGate Confidential"].waitForNonExistence(timeout: 3),
                      "confidential card shown after wrong PIN")
        XCTAssertTrue(app.staticTexts["Incorrect PIN. 4 attempts left."].waitForExistence(timeout: 10), "wrong PIN not rejected")
        entry.typeText("135790")
        XCTAssertTrue(app.staticTexts["UpgradeGate Confidential"].waitForExistence(timeout: 10), "PIN did not verify")

        // Phase 3: the gate view reads what phase 1 scheduled.
        app.terminate()
        app.launchEnvironment["CONFIDENTIAL_AUTH_STUB"] = nil
        app.launchArguments = ["-upgradeGate", expected, "-upgradeGateReminders", reminders]
        app.launch()

        let result = app.staticTexts["UpgradeGate_Result"]
        XCTAssertTrue(result.waitForExistence(timeout: 30), "UpgradeGate_Result did not appear")
        // The label starts as "running" and is replaced when the in-app checks finish.
        let done = NSPredicate(format: "label != %@", "running")
        wait(for: [XCTNSPredicateExpectation(predicate: done, object: result)], timeout: 60)
        XCTAssertEqual(result.label, "PASS", result.label)
    }

    private func openCards(_ app: XCUIApplication) {
        let tab = app.cardsTab
        XCTAssertTrue(tab.waitForExistence(timeout: 15), "Cards tab missing")
        tab.tap()
        XCTAssertTrue(app.navigationBars["Prayer Cards"].waitForExistence(timeout: 10), "Cards missing")
    }

    private func assertNoTips(_ app: XCUIApplication, on screen: String) {
        for tip in Self.tips {
            XCTAssertTrue(app.staticTexts[tip].waitForNonExistence(timeout: 3), "onboarding tip \"\(tip)\" shown on \(screen)")
        }
    }
}
