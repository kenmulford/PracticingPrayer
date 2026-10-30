import Foundation

/// Writes the shared text to the App Group container for the host app to pick up.
public struct PendingImportWriter: Sendable {
    public static let appGroupID = "group.com.multithreadedllc.prayercards"
    public static let fileName = "pending-import.json"
    public static let hostURL = URL(string: "practicingprayer://import")!

    private struct Payload: Encodable {
        let raw: String
        let ts: String
    }

    private let containerURL: URL

    public init(containerURL: URL) {
        self.containerURL = containerURL
    }

    /// Returns true only when the file was written.
    public func write(_ text: String, now: Date = Date()) -> Bool {
        let log = ImportBreadcrumbLog(containerURL: containerURL)
        // Notes can deliver NFD; normalizing keeps composed and decomposed text one prayer.
        let normalized = text.precomposedStringWithCanonicalMapping
        guard normalized.utf8.count <= SharePayloadClassifier.maxPayloadBytes else {
            log.append(.oversized, byteCount: nil, now: now)
            return false
        }

        let json: Data
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
            json = try encoder.encode(Payload(raw: normalized, ts: ImportBreadcrumbLog.timestamp(now)))
        } catch {
            log.append(.pipelineError, byteCount: nil, now: now)
            return false
        }

        do {
            // .atomic leaves the prior file intact if the process dies mid-write.
            try json.write(to: containerURL.appendingPathComponent(Self.fileName), options: [.atomic])
        } catch {
            log.append(.ioFail, byteCount: nil, now: now)
            return false
        }
        log.append(.writeOk, byteCount: json.count, now: now)
        return true
    }
}
