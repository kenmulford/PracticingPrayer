import Foundation
import PrayerKit

/// Picks up the App Group hand-off file written by the share extension and `ImportTextIntent`.
/// Token vocabulary and the log trim follow the App Group file contract (`AppGroupWriter`).
nonisolated struct PendingImportReader {
    static let maxLogLines = 200

    static let defaultContainerURL: @Sendable () -> URL? = {
        #if DEBUG
        if let dir = ProcessInfo.processInfo.environment["PENDING_IMPORT_DIR"] {
            return URL(fileURLWithPath: dir)
        }
        #endif
        return FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: AppGroupWriter.appGroupID)
    }

    private let containerURL: @Sendable () -> URL?

    init(containerURL: @escaping @Sendable () -> URL? = PendingImportReader.defaultContainerURL) {
        self.containerURL = containerURL
    }

    private struct Wire: Decodable {
        let raw: String
        let ts: String?
    }

    /// Returns the shared text and deletes the file; a failed or empty pickup is logged and returns nil.
    func takePending() -> String? {
        guard let container = containerURL() else { return nil }
        let fm = FileManager.default
        let payloadURL = container.appendingPathComponent(AppGroupWriter.payloadFileName)
        guard fm.fileExists(atPath: payloadURL.path) else { return nil }

        trimLog(in: container)

        var byteCount = -1
        var token = "io-fail"
        var raw: String?
        if let data = try? Data(contentsOf: payloadURL) {
            byteCount = data.count
            if let wire = try? JSONDecoder().decode(Wire.self, from: data) {
                if wire.raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    token = "empty"
                } else {
                    token = "ok"
                    raw = wire.raw
                }
            } else {
                token = "parse-fail"
            }
        }
        try? fm.removeItem(at: payloadURL)
        AppGroupWriter(containerURL: { container }).appendBreadcrumb(
            containerURL: container, byteCount: byteCount, token: token)
        return raw
    }

    /// Keeps the newest `maxLogLines` lines so the append-only log cannot grow without bound.
    private func trimLog(in container: URL) {
        let logURL = container.appendingPathComponent(AppGroupWriter.logFileName)
        guard let text = try? String(contentsOf: logURL, encoding: .utf8) else { return }
        let lines = text.split(separator: "\n", omittingEmptySubsequences: true)
        guard lines.count > Self.maxLogLines else { return }
        let kept = lines.suffix(Self.maxLogLines).joined(separator: "\n") + "\n"
        try? kept.write(to: logURL, atomically: true, encoding: .utf8)
    }
}

struct PendingImport: Identifiable {
    let id: UUID
    let payload: SharePayload
    let cardTitle: String

    static func pickUp(
        reader: PendingImportReader = PendingImportReader(), now: Date = .now, locale: Locale = .current
    ) -> PendingImport? {
        guard let raw = reader.takePending() else { return nil }
        let parsed = TextSelectionParser.parse(raw, now: now, locale: locale)
        guard !parsed.prayers.isEmpty else { return nil }
        return PendingImport(
            id: UUID(), payload: .card(title: parsed.suggestedCardTitle, requests: parsed.prayers),
            cardTitle: parsed.suggestedCardTitle)
    }
}
