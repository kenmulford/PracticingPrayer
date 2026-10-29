import Foundation
import Testing
import UIKit
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@Test func tabsAreInShellOrder() {
    #expect(AppTab.allCases.map(\.systemImage) == ["house", "rectangle.stack", "hands.and.sparkles", "tag", "gearshape"])
}

@Test func missingDatabaseFails() async {
    let services = AppServices(databasePath: "/nonexistent/prayer_app.db")
    await services.open()
    #expect(services.phase == .failed)
}

@Test func fixtureDatabaseOpens() async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("AppShellModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer { try? FileManager.default.removeItem(at: copy) }

    let services = AppServices(databasePath: copy.path)
    await services.open()
    #expect(services.phase == .open)
}

@Test(arguments: [
    (UIUserInterfaceStyle.light, UIAccessibilityContrast.normal, "5A6E4B"),
    (UIUserInterfaceStyle.dark, UIAccessibilityContrast.normal, "9DB58A"),
    (UIUserInterfaceStyle.light, UIAccessibilityContrast.high, "415432"),
    (UIUserInterfaceStyle.dark, UIAccessibilityContrast.high, "B5CEA2"),
])
func accentColorAppearances(style: UIUserInterfaceStyle, contrast: UIAccessibilityContrast, hex: String) throws {
    let color = try #require(UIColor(named: "AccentColor"))
    let traits = UITraitCollection(traitsFrom: [
        UITraitCollection(userInterfaceStyle: style),
        UITraitCollection(accessibilityContrast: contrast),
    ])
    var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
    color.resolvedColor(with: traits).getRed(&r, green: &g, blue: &b, alpha: &a)
    let resolved = String(format: "%02X%02X%02X", Int((r * 255).rounded()), Int((g * 255).rounded()), Int((b * 255).rounded()))
    #expect(resolved == hex)
}
