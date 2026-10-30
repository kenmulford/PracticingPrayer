import Foundation
import PrayerKit

/// Picks up the App Group hand-off file written by the share extension and `ImportTextIntent`.
/// Token vocabulary and the log trim follow the App Group file contract (`PendingImportWriter`, `ImportBreadcrumbLog`).
nonisolated struct PendingImportReader {
    static let maxLogLines = 200

    static let defaultContainerURL: @Sendable () -> URL? = {
        #if DEBUG
        if let dir = ProcessInfo.processInfo.environment["PENDING_IMPORT_DIR"] {
            return URL(fileURLWithPath: dir)
        }
        #endif
        return FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: PendingImportWriter.appGroupID)
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
        let payloadURL = container.appendingPathComponent(PendingImportWriter.fileName)
        guard fm.fileExists(atPath: payloadURL.path) else { return nil }

        trimLog(in: container)
        let log = ImportBreadcrumbLog(containerURL: container)

        // Claim the file by renaming it: an extension write landing mid-pickup then creates a fresh
        // pending-import.json instead of being read half-way or deleted unread.
        let claimedURL = container.appendingPathComponent("pending-import.\(UUID().uuidString).claimed")
        do {
            try fm.moveItem(at: payloadURL, to: claimedURL)
        } catch {
            log.append(.ioFail, byteCount: nil)
            return nil
        }
        defer { try? fm.removeItem(at: claimedURL) }

        guard let data = try? Data(contentsOf: claimedURL) else {
            log.append(.ioFail, byteCount: nil)
            return nil
        }
        guard let wire = try? JSONDecoder().decode(Wire.self, from: data) else {
            log.append(.parseFail, byteCount: data.count)
            return nil
        }
        guard !wire.raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            log.append(.empty, byteCount: data.count)
            return nil
        }
        log.append(.ok, byteCount: data.count)
        return wire.raw
    }

    /// Keeps the newest `maxLogLines` lines so the append-only log cannot grow without bound.
    private func trimLog(in container: URL) {
        let logURL = container.appendingPathComponent(ImportBreadcrumbLog.fileName)
        guard let text = try? String(contentsOf: logURL, encoding: .utf8) else { return }
        let lines = text.split(separator: "\n", omittingEmptySubsequences: true)
        guard lines.count > Self.maxLogLines else { return }
        let kept = lines.suffix(Self.maxLogLines).joined(separator: "\n") + "\n"
        try? kept.write(to: logURL, atomically: true, encoding: .utf8)
    }
}

struct PendingImport {
    let payload: SharePayload
    let cardTitle: String

    static func pickUp(
        reader: PendingImportReader = PendingImportReader(), now: Date = .now, locale: Locale = .current
    ) -> PendingImport? {
        guard let raw = reader.takePending() else { return nil }
        let parsed = TextSelectionParser.parse(raw, now: now, locale: locale)
        guard !parsed.prayers.isEmpty else { return nil }
        return PendingImport(
            payload: .card(title: parsed.suggestedCardTitle, requests: parsed.prayers),
            cardTitle: parsed.suggestedCardTitle)
    }
}
