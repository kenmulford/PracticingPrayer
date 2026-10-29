import Foundation
import Testing
@testable import PrayerApp

@Test func hostBundleIdentifierIsTheShippingID() {
    #expect(Bundle.main.bundleIdentifier == "com.multithreadedllc.prayercards")
}
