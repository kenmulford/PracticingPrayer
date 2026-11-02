import XCTest

extension XCUIApplication {
    /// The Cards tab: a tab-bar button on compact width, a sidebar button on regular width.
    var cardsTab: XCUIElement {
        tabBars.buttons["Cards"].exists ? tabBars.buttons["Cards"] : buttons["Cards"].firstMatch
    }

    /// Frames of every collection view on screen. Take this before tapping a menu button, then pass it to
    /// `openedMenuCollectionView(excluding:)`.
    func collectionViewFrames() -> [CGRect] {
        collectionViews.allElementsBoundByIndex.map { $0.frame }
    }

    /// The menu a button just opened. On iOS 27.2 a menu is a collection view of cells (`menuItems` is empty), so
    /// only a collection view whose frame was not in the pre-tap snapshot counts, which keeps the iPad sidebar
    /// list and sheet forms out.
    func openedMenuCollectionView(excluding before: [CGRect], timeout: TimeInterval = 5) -> XCUIElement? {
        let deadline = Date().addingTimeInterval(timeout)
        repeat {
            let views = collectionViews.allElementsBoundByIndex
            let frames = views.map { $0.frame }
            for (view, frame) in zip(views, frames) where !before.contains(frame) && view.cells.firstMatch.exists {
                return view
            }
            Thread.sleep(forTimeInterval: 0.25)
        } while Date() < deadline
        return nil
    }
}
