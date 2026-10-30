//
//  AppGroupWriter.swift
//
//  Writes the App Group hand-off file (pending-import.json) and its breadcrumb log
//  for ImportTextIntent. The app target is the single copy.
//
//  Token vocabulary and breadcrumb format are the App Group file contract:
//  the main app truncates import-log.txt on pickup. DO NOT diverge.
//

import Foundation
import Darwin

nonisolated enum IntentError: Error, LocalizedError {
    case emptyText
    case appGroupUnavailable
    case payloadTooLarge(byteCount: Int)
    case writeFailed(underlying: Error?)

    var errorDescription: String? {
        switch self {
        case .emptyText:
            return String(localized: "There's no text to save. Try again with a longer selection.")
        case .appGroupUnavailable:
            return String(localized: "Couldn't reach Practicing Prayer's storage. Reinstall the app to fix sharing.")
        case .payloadTooLarge:
            return String(localized: "That prayer is too long to save from the share sheet. Try a shorter selection.")
        case .writeFailed:
            return String(localized: "Couldn't save the prayer right now. Try again.")
        }
    }
}

nonisolated struct AppGroupWriter {
    static let appGroupID = "group.com.multithreadedllc.prayercards"
    static let payloadFileName = "pending-import.json"
    static let logFileName = "import-log.txt"
    static let maxPayloadBytes = 256 * 1024

    private let containerURL: @Sendable () -> URL?

    init(containerURL: @escaping @Sendable () -> URL? = {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: AppGroupWriter.appGroupID)
    }) {
        self.containerURL = containerURL
    }

    func writePendingImport(rawText: String) throws {
        // Reject empty / whitespace-only input early. A blank payload would still
        // serialize and write successfully, but the host's import flow would
        // surface an empty ConfirmImport modal, confusing UX. Fail loud instead.
        guard !rawText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw IntentError.emptyText
        }

        guard let containerURL = containerURL() else {
            // No container at all, can't even breadcrumb. Nothing to do but throw.
            throw IntentError.appGroupUnavailable
        }

        // Apply NFC at the boundary. Notes can deliver NFD; without normalization
        // here, composed and decomposed forms of one letter produce two prayer records.
        let normalized = rawText.precomposedStringWithCanonicalMapping
        let byteCount = normalized.utf8.count

        if byteCount > AppGroupWriter.maxPayloadBytes {
            appendBreadcrumb(containerURL: containerURL, byteCount: -1, token: "io-fail")
            throw IntentError.payloadTooLarge(byteCount: byteCount)
        }

        let payload = WirePayload(raw: normalized, ts: Self.iso8601String())
        let json: Data
        do {
            let encoder = JSONEncoder()
            // Stable byte output for the golden fixture; host parser is order-
            // insensitive (System.Text.Json source-gen matches keys by name)
            // so determinism is for fixture stability only.
            encoder.outputFormatting = [.sortedKeys]
            json = try encoder.encode(payload)
        } catch {
            appendBreadcrumb(containerURL: containerURL, byteCount: -1, token: "io-fail")
            throw IntentError.writeFailed(underlying: error)
        }

        let payloadURL = containerURL.appendingPathComponent(AppGroupWriter.payloadFileName)
        do {
            // .atomic = sibling temp + fsync + rename.
            // A crash mid-write leaves the prior file intact.
            try json.write(to: payloadURL, options: [.atomic])
        } catch {
            appendBreadcrumb(containerURL: containerURL, byteCount: -1, token: "io-fail")
            throw IntentError.writeFailed(underlying: error)
        }

        appendBreadcrumb(containerURL: containerURL, byteCount: json.count, token: "write-ok")
    }

    // MARK: - Internals

    private struct WirePayload: Encodable {
        let raw: String
        let ts: String
    }

    private static func iso8601String() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        return formatter.string(from: Date())
    }

    /// Appends one line to import-log.txt using POSIX O_APPEND for cross-process
    /// atomicity with the main app's appender.
    /// Foundation's FileHandle does NOT expose O_APPEND, it truncates on
    /// `forWritingTo:` and `seekToEndOfFile()` is a TOCTOU race against the
    /// main app's truncation of the log. Use Darwin.open directly.
    /// Best-effort; failure to log must never break the import path.
    func appendBreadcrumb(containerURL: URL, byteCount: Int, token: String) {
        let line = "\(Self.iso8601String()) \(byteCount < 0 ? "-" : String(byteCount)) \(token)\n"
        guard let bytes = line.data(using: .ascii) else { return }
        let logPath = containerURL.appendingPathComponent(AppGroupWriter.logFileName).path

        let fd = logPath.withCString { cPath in
            Darwin.open(cPath, O_WRONLY | O_APPEND | O_CREAT, 0o644)
        }
        guard fd >= 0 else { return }
        defer { Darwin.close(fd) }

        _ = bytes.withUnsafeBytes { buffer -> Int in
            guard let baseAddress = buffer.baseAddress else { return 0 }
            return Darwin.write(fd, baseAddress, buffer.count)
        }
    }
}
