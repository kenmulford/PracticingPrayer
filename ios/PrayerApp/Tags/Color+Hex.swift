import SwiftUI

extension Color {
    /// Parses `#RRGGBB` (the leading `#` is optional). Any other shape returns nil.
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
