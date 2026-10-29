import UIKit

extension AppDelegate {
    // Static: restored 1.7.2 scenes host AppRoot outside the App environment.
    nonisolated(unsafe) static var isPrayerSessionActive = false

    static func setPrayerSessionActive(_ active: Bool) {
        isPrayerSessionActive = active
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first
        scene?.keyWindow?.rootViewController?.setNeedsUpdateOfSupportedInterfaceOrientations()
        guard !active, let scene else { return }
        let mask = OrientationPolicy.supportedOrientations(
            idiom: UIDevice.current.userInterfaceIdiom,
            allowLandscape: AppServices.defaultPreferences.prayerTimeLandscape,
            inSession: false)
        scene.requestGeometryUpdate(.iOS(interfaceOrientations: mask))
    }

    func application(
        _ application: UIApplication, supportedInterfaceOrientationsFor window: UIWindow?
    ) -> UIInterfaceOrientationMask {
        OrientationPolicy.supportedOrientations(
            idiom: window?.traitCollection.userInterfaceIdiom ?? UIDevice.current.userInterfaceIdiom,
            allowLandscape: AppServices.defaultPreferences.prayerTimeLandscape,
            inSession: Self.isPrayerSessionActive)
    }
}
