import SwiftUI
import UIKit

@MainActor
protocol PinSheetPresenting: AnyObject {
    func present(_ flow: PinFallbackFlow)
    func dismiss()
}

/// Presents the PIN sheets from their own window because the Card and Collection editors authenticate from inside a sheet.
@MainActor
final class PinSheetWindow: PinSheetPresenting {
    private var window: UIWindow?

    func present(_ flow: PinFallbackFlow) {
        guard window == nil,
              let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first
        else { return }
        let root = UIViewController()
        root.view.backgroundColor = .clear
        let pinWindow = UIWindow(windowScene: scene)
        pinWindow.windowLevel = .normal + 1
        pinWindow.rootViewController = root
        pinWindow.makeKeyAndVisible()
        let host = UIHostingController(rootView: PinSheetHost(flow: flow))
        host.isModalInPresentation = true
        root.present(host, animated: true)
        window = pinWindow
    }

    func dismiss() {
        guard let pinWindow = window else { return }
        window = nil
        pinWindow.rootViewController?.dismiss(animated: true) { pinWindow.isHidden = true }
    }
}

struct PinSheetHost: View {
    let flow: PinFallbackFlow

    var body: some View {
        switch flow.sheet {
        case .setPin: SetPinSheet(flow: flow)
        case .enterPin: EnterPinSheet(flow: flow)
        case nil: Color.clear
        }
    }
}
