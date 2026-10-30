import XCTest

extension XCUIApplication {
    /// The Cards tab: a tab-bar button on compact width, a sidebar button on regular width.
    var cardsTab: XCUIElement {
        tabBars.buttons["Cards"].exists ? tabBars.buttons["Cards"] : buttons["Cards"].firstMatch
    }
}
