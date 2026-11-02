import Foundation
import Testing
import UIKit

struct LaunchScreenTests {
    @Test(arguments: [
        (UIUserInterfaceStyle.light, UIAccessibilityContrast.normal),
        (.dark, .normal),
        (.light, .high),
        (.dark, .high),
    ])
    func coverBackgroundIsFixed(style: UIUserInterfaceStyle, contrast: UIAccessibilityContrast) throws {
        let color = try #require(UIColor(named: "CoverBackground"))
        let traits = UITraitCollection(traitsFrom: [
            UITraitCollection(userInterfaceStyle: style),
            UITraitCollection(accessibilityContrast: contrast),
        ])
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        color.resolvedColor(with: traits).getRed(&r, green: &g, blue: &b, alpha: &a)
        let resolved = String(format: "%02X%02X%02X", Int((r * 255).rounded()), Int((g * 255).rounded()), Int((b * 255).rounded()))
        #expect(resolved == "566847")
    }

    @Test func launchScreenUsesCoverBackground() throws {
        let dict = try #require(Bundle.main.object(forInfoDictionaryKey: "UILaunchScreen") as? [String: Any])
        #expect(dict["UIColorName"] as? String == "CoverBackground")
        #expect(dict["UIImageName"] as? String == "LaunchIcon")
    }

    @Test func launchIconIs96Points() throws {
        let image = try #require(UIImage(named: "LaunchIcon"))
        #expect(image.size == CGSize(width: 96, height: 96))
    }
}
