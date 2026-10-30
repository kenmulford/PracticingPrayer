import Foundation

/// Finds an importable share link at the start of clipboard text.
public enum ClipboardShareLink {
    private static let prefix = "https://practicingprayerapp.com/share"

    public static func importableURL(in text: String?) -> URL? {
        guard let text else { return nil }
        let trimmed = text.drop(while: \.isWhitespace)
        guard trimmed.hasPrefix(prefix) else { return nil }
        let candidate = trimmed.prefix(while: { !$0.isWhitespace })
        guard let url = URL(string: String(candidate)) else { return nil }
        let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        func has(_ name: String) -> Bool {
            items.contains { $0.name == name && !($0.value ?? "").allSatisfy(\.isWhitespace) }
        }
        switch InboundShareRoute(url: url) {
        case .link(.request): return has("d") || has("title") ? url : nil
        case .link(.card): return has("d") || (has("title") && has("requests")) ? url : nil
        case .file, .ignored: return nil
        }
    }
}
