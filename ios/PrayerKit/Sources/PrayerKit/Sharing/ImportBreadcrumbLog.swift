import Darwin
import Foundation

public enum ImportBreadcrumbToken: String, Sendable {
    case writeOk = "write-ok"
    case ioFail = "io-fail"
    case loadItemError = "load-item-error"
    case noAttachment = "no-attachment"
    case unsupportedType = "unsupported-type"
    case emptyText = "empty-text"
    case oversized
    case pipelineError = "pipeline-error"
    // Host-app pickup outcomes.
    case ok
    case empty
    case parseFail = "parse-fail"
}

/// Appends one line per share attempt to `import-log.txt` in the App Group container.
public struct ImportBreadcrumbLog: Sendable {
    public static let fileName = "import-log.txt"

    private let containerURL: URL

    public init(containerURL: URL) {
        self.containerURL = containerURL
    }

    /// Logging is best effort: a failure here must never break the import path.
    public func append(_ token: ImportBreadcrumbToken, byteCount: Int?, now: Date = Date()) {
        let count = byteCount.map(String.init) ?? "-"
        let line = "\(Self.timestamp(now)) \(count) \(token.rawValue)\n"
        guard let bytes = line.data(using: .ascii) else { return }
        let path = containerURL.appendingPathComponent(Self.fileName).path

        // FileHandle cannot open with O_APPEND; the O_APPEND write keeps appends atomic across processes.
        let fd = path.withCString { Darwin.open($0, O_WRONLY | O_APPEND | O_CREAT, 0o644) }
        guard fd >= 0 else { return }
        defer { Darwin.close(fd) }
        _ = bytes.withUnsafeBytes { buffer -> Int in
            guard let base = buffer.baseAddress else { return 0 }
            return Darwin.write(fd, base, buffer.count)
        }
    }

    static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        return formatter.string(from: date)
    }
}
