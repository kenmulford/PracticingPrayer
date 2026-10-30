import SwiftUI
import UIKit

@MainActor
final class PrivacyCoverWindow {
    private var window: UIWindow?

    func show() {
        guard window == nil,
              let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first
        else { return }
        let cover = UIWindow(windowScene: scene)
        cover.windowLevel = .alert + 1
        let host = UIHostingController(rootView: PrivacyCoverView())
        host.view.accessibilityViewIsModal = true
        cover.rootViewController = host
        cover.isHidden = false
        window = cover
    }

    func hide() {
        window?.isHidden = true
        window = nil
    }
}
