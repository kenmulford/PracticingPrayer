import AppIntents
import Foundation

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

/// The type name and phrases stay as in 1.7.2 so saved shortcuts keep working.
struct ImportTextIntent: AppIntent {
    static let title: LocalizedStringResource = "Save to Prayer"
    static let description = IntentDescription(
        "Save shared text as a new prayer card in Practicing Prayer.")

    static let openAppWhenRun: Bool = true

    @Parameter(
        title: "Shared Text",
        description: "Text to save as a prayer card.",
        requestValueDialog: IntentDialog("What text should I save?")
    )
    var sharedText: String

    static var parameterSummary: some ParameterSummary {
        Summary("Save \(\.$sharedText) to Practicing Prayer")
    }

    @MainActor
    func perform() async throws -> some IntentResult {
        try AppGroupWriter().writePendingImport(rawText: sharedText)
        return .result()
    }
}
