//
//  ImportTextIntent.swift
//
//  Shortcuts action that hands text to the app through the App Group file.
//  The type name and phrases stay as in 1.7.2 so saved shortcuts keep working.
//

import AppIntents
import Foundation

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
