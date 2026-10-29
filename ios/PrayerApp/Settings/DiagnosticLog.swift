import Foundation

nonisolated final class DiagnosticLog: Sendable {
    static let fileName = "diagnostics.log"
    static let maxEntries = 100
    private static let delimiter = "---"

    let logURL: URL
    private let lock = NSLock()

    init(directory: URL) {
        logURL = directory.appendingPathComponent(Self.fileName)
    }

    func log(category: String, error: any Error) {
        let entry = "[\(Self.timestamp("yyyy-MM-dd HH:mm:ss"))] [\(category)] "
            + "\(String(describing: type(of: error))): \(error.localizedDescription)\n"
            + "\(Thread.callStackSymbols.joined(separator: "\n"))\n\(Self.delimiter)\n"
        append(entry)
    }

    func log(category: String, message: String) {
        append("[\(Self.timestamp("yyyy-MM-dd HH:mm:ss.SSS"))] [\(category)] \(message)\n")
    }

    func trim() {
        lock.lock()
        defer { lock.unlock() }
        guard let content = try? String(contentsOf: logURL, encoding: .utf8) else { return }
        let entries = content.components(separatedBy: Self.delimiter + "\n").filter { !$0.isEmpty }
        guard entries.count > Self.maxEntries else { return }
        let kept = entries.suffix(Self.maxEntries).joined(separator: Self.delimiter + "\n") + Self.delimiter + "\n"
        try? kept.write(to: logURL, atomically: true, encoding: .utf8)
    }

    var hasEntries: Bool {
        lock.lock()
        defer { lock.unlock() }
        guard let values = try? logURL.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey]),
              values.isRegularFile == true, let size = values.fileSize else { return false }
        return size > 0
    }

    private func append(_ entry: String) {
        lock.lock()
        defer { lock.unlock() }
        let data = Data(entry.utf8)
        let manager = FileManager.default
        try? manager.createDirectory(at: logURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let handle = try? FileHandle(forWritingTo: logURL) {
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
        } else {
            try? data.write(to: logURL)
        }
    }

    private static func timestamp(_ pattern: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(identifier: "UTC")
        formatter.dateFormat = pattern
        return formatter.string(from: Date())
    }
}
