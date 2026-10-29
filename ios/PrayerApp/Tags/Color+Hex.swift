import PrayerKit
import SwiftUI
import UIKit

extension Color {
    /// The tag dot color; nil or an unparsable hex falls back to the first default palette color.
    static func tag(_ hex: String?) -> Color {
        let defaultHex = UserColorStore.defaultPaletteHexValues[0]
        return Color(hex: hex ?? defaultHex) ?? Color(hex: defaultHex) ?? .accentColor
    }

    /// Parses `#RRGGBB` (the leading `#` is optional). Any other shape returns nil.
    /// Uppercase `#RRGGBB` of this color in the device RGB space.
    var hexString: String {
        var red: CGFloat = 0, green: CGFloat = 0, blue: CGFloat = 0, alpha: CGFloat = 0
        UIColor(self).getRed(&red, green: &green, blue: &blue, alpha: &alpha)
        func byte(_ value: CGFloat) -> Int { Int((min(max(value, 0), 1) * 255).rounded()) }
        return String(format: "#%02X%02X%02X", byte(red), byte(green), byte(blue))
    }

    init?(hex: String) {
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard digits.count == 6, digits.allSatisfy(\.isASCII), let value = UInt32(digits, radix: 16) else {
            return nil
        }
        self.init(
            red: Double((value >> 16) & 0xFF) / 255,
            green: Double((value >> 8) & 0xFF) / 255,
            blue: Double(value & 0xFF) / 255)
    }
}
