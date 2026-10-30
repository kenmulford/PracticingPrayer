//
//  PrayerAppShortcuts.swift
//
//  The app's single AppShortcutsProvider.
//

import AppIntents

nonisolated struct PrayerAppShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: ImportTextIntent(),
            phrases: [
                "Save to \(.applicationName)",
                "New prayer card in \(.applicationName)",
                "Add a prayer to \(.applicationName)"
            ],
            shortTitle: "Save to Prayer",
            systemImageName: "hands.and.sparkles.fill")
    }
}
