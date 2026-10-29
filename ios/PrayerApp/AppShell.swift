import SwiftUI

struct AppShell: View {
    @Environment(AppServices.self) private var services
    @State private var selection: AppTab = .home
    @State private var homePath: [HomeRoute] = []
    @State private var cardsPath: [CardsRoute] = []
    @State private var prayersPath: [PrayersRoute] = []
    @State private var tagsPath: [TagsRoute] = []
    @State private var settingsPath: [SettingsRoute] = []

    var body: some View {
        Group {
            switch services.phase {
            case .opening:
                ProgressView()
            case .failed:
                ContentUnavailableView {
                    Label("Can't Open Your Prayers", systemImage: "exclamationmark.triangle")
                } actions: {
                    Button("Try Again") {
                        Task { await services.open() }
                    }
                    .buttonStyle(.borderedProminent)
                    .prominentLabel()
                }
            case .open:
                tabs
            }
        }
        .task { await services.open() }
    }

    private var tabs: some View {
        TabView(selection: $selection) {
            ForEach(AppTab.allCases, id: \.self) { tab in
                Tab(value: tab) {
                    root(for: tab)
                } label: {
                    Label {
                        Text(tab.label)
                    } icon: {
                        Image(systemName: tab.systemImage)
                    }
                }
            }
        }
        .tabViewStyle(.sidebarAdaptable)
    }

    @ViewBuilder
    private func root(for tab: AppTab) -> some View {
        switch tab {
        case .home:
            NavigationStack(path: $homePath) { HomeView() }
        case .cards:
            CardsView(path: $cardsPath)
        case .prayers:
            PrayersView(path: $prayersPath)
        case .tags:
            NavigationStack(path: $tagsPath) { TagsView(path: $tagsPath) }
        case .settings:
            NavigationStack(path: $settingsPath) { SettingsView() }
        }
    }
}

private struct ProminentLabel: ViewModifier {
    @Environment(\.colorScheme) private var colorScheme

    func body(content: Content) -> some View {
        content.foregroundStyle(colorScheme == .dark ? Color.black : Color.white)
    }
}

extension View {
    func prominentLabel() -> some View {
        modifier(ProminentLabel())
    }
}
