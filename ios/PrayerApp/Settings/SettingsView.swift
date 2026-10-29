import SwiftUI

struct SettingsView: View {
    @Environment(AppServices.self) private var services

    var body: some View {
        List {
            Section { row(.appSettings) }
            Section {
                row(.collections)
                row(.backup)
            }
            Section {
                row(.help)
                row(.about)
            }
        }
        .listStyle(.insetGrouped)
        .accessibilityIdentifier("Shell_Root_settings")
        .scrollContentBackground(.hidden)
        .frame(maxWidth: 700)
        .frame(maxWidth: .infinity)
        .background(Color(.systemGroupedBackground))
        .navigationTitle(Text(AppTab.settings.title))
        .navigationBarTitleDisplayMode(.large)
        .navigationDestination(for: SettingsRoute.self) { route in
            switch route {
            case .appSettings:
                AppSettingsView(preferences: services.preferences)
            case .help:
                HelpView()
            case .about:
                AboutView()
            default:
                ContentUnavailableView {
                    Text(route.title)
                }
                .navigationTitle(Text(route.title))
                .navigationBarTitleDisplayMode(.inline)
            }
        }
    }

    private func row(_ route: SettingsRoute) -> some View {
        NavigationLink(value: route) {
            SettingsRow(title: route.title, systemImage: route.systemImage, tint: route.tint)
        }
        .accessibilityIdentifier("settings.row.\(route.identifier)")
    }
}

private extension SettingsRoute {
    var identifier: String {
        switch self {
        case .appSettings: "appSettings"
        case .collections: "collections"
        case .backup: "backup"
        case .help: "help"
        case .about: "about"
        }
    }

    var title: LocalizedStringResource {
        switch self {
        case .appSettings: "App Settings"
        case .collections: "Collections"
        case .backup: "Backup & Restore"
        case .help: "Help"
        case .about: "About"
        }
    }

    var systemImage: String {
        switch self {
        case .appSettings: "gearshape.fill"
        case .collections: "folder.fill"
        case .backup: "arrow.triangle.2.circlepath"
        case .help: "questionmark"
        case .about: "info"
        }
    }

    var tint: Color {
        switch self {
        case .appSettings, .about: Color(.systemGray)
        case .collections: Color.accentColor
        case .backup: Color(.systemBlue)
        case .help: Color(.systemPurple)
        }
    }
}
