import UIKit

enum OrientationPolicy {
    static func supportedOrientations(
        idiom: UIUserInterfaceIdiom, allowLandscape: Bool, inSession: Bool
    ) -> UIInterfaceOrientationMask {
        if idiom == .pad { return .allButUpsideDown }
        return allowLandscape && inSession ? .allButUpsideDown : .portrait
    }
}
