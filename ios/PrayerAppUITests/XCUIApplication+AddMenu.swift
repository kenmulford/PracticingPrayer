import XCTest

/// Menu order of the Cards root Add menu.
enum AddMenuItem: Int { case newCard, newCollection }

extension XCUIApplication {
    /// Opens the Cards root Add menu and taps an item by position, since iOS 27.2 menu cells carry no labels.
    func chooseFromAddMenu(_ item: AddMenuItem) {
        let add = navigationBars.buttons["Add"].firstMatch
        XCTAssertTrue(add.waitForExistence(timeout: 10), "Add menu button missing")
        let before = collectionViewFrames()
        add.tap()
        guard let menu = openedMenuCollectionView(excluding: before) else {
            XCTFail("Add menu did not open")
            return
        }
        menu.cells.element(boundBy: item.rawValue).tap()
    }
}
