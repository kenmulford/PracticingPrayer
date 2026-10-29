import Testing
import UIKit
@testable import PrayerApp

struct OrientationPolicyTests {
    @Test func padIgnoresSetting() {
        for allow in [true, false] {
            for inSession in [true, false] {
                #expect(OrientationPolicy.supportedOrientations(
                    idiom: .pad, allowLandscape: allow, inSession: inSession) == .allButUpsideDown)
            }
        }
    }

    @Test func phoneLandscapeOnlyInSessionWithSetting() {
        for allow in [true, false] {
            for inSession in [true, false] {
                let expected: UIInterfaceOrientationMask = allow && inSession ? .allButUpsideDown : .portrait
                #expect(OrientationPolicy.supportedOrientations(
                    idiom: .phone, allowLandscape: allow, inSession: inSession) == expected)
            }
        }
    }
}
