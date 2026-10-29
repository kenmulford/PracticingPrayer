import Foundation

public enum TextNormalization {
    public static func normalizeQuotes(_ text: String) -> String {
        text
            .replacingOccurrences(of: "\u{2018}", with: "'")
            .replacingOccurrences(of: "\u{2019}", with: "'")
            .replacingOccurrences(of: "\u{201C}", with: "\"")
            .replacingOccurrences(of: "\u{201D}", with: "\"")
    }

    public static func collapseWhitespace(_ text: String) -> String {
        text.replacing(/\s+/, with: " ").trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
