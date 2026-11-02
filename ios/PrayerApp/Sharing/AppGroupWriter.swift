import Foundation
import PrayerKit

/// Adapts `PendingImportWriter`, the one owner of the App Group file contract, to the intent's thrown errors.
nonisolated struct AppGroupWriter {
    private let containerURL: @Sendable () -> URL?

    init(containerURL: @escaping @Sendable () -> URL? = {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: PendingImportWriter.appGroupID)
    }) {
        self.containerURL = containerURL
    }

    func writePendingImport(rawText: String) throws {
        // A blank payload would write fine but open an empty confirm sheet, so fail before writing.
        guard !rawText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw IntentError.emptyText
        }
        guard let containerURL = containerURL() else { throw IntentError.appGroupUnavailable }

        guard PendingImportWriter(containerURL: containerURL).write(rawText) else {
            let byteCount = rawText.precomposedStringWithCanonicalMapping.utf8.count
            if byteCount > SharePayloadClassifier.maxPayloadBytes {
                throw IntentError.payloadTooLarge(byteCount: byteCount)
            }
            throw IntentError.writeFailed(underlying: nil)
        }
    }
}
