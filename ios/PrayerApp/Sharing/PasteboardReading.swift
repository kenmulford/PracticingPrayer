import UIKit

protocol PasteboardReading {
    /// Detects a web URL without reading the contents, so no paste banner appears.
    func hasProbableWebURL() async throws -> Bool
    func string() -> String?
}

struct SystemPasteboard: PasteboardReading {
    func hasProbableWebURL() async throws -> Bool {
        try await UIPasteboard.general.detectedPatterns(for: [\.probableWebURL]).contains(\.probableWebURL)
    }

    func string() -> String? {
        UIPasteboard.general.string
    }
}

#if DEBUG
struct StubPasteboard: PasteboardReading {
    let text: String

    func hasProbableWebURL() async throws -> Bool {
        text.contains("://")
    }

    func string() -> String? {
        text
    }
}
#endif
