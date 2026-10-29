import PrayerKit
import SwiftUI

struct AppShell: View {
    @Environment(AppServices.self) private var services
    @State private var selection: AppTab = .home
    @State private var homePath: [HomeRoute] = []
    @State private var cardsPath: [CardsRoute] = []
    @State private var prayersPath: [PrayersRoute] = []
    @State private var tagsPath: [TagsRoute] = []
    @State private var settingsPath: [SettingsRoute] = []
    @State private var showsPrayerTimeAlert = false

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
                tabs.fullScreenCover(item: Bindable(services).prayerSession) { PrayerSessionView(session: $0) }
            }
        }
        .task {
            await services.open()
            #if DEBUG
            if ProcessInfo.processInfo.arguments.contains(PrayerSession.debugArgument), services.phase == .open {
                let items = (try? PrayerStore(database: services.database)
                    .prayers(segment: .active, isSessionUnlocked: false)) ?? []
                services.prayerSession = PrayerSession(items: Array(items.prefix(3)), database: services.database)
            }
            #endif
        }
        .onChange(of: services.phase == .open && NotificationTapHandler.shared.hasPendingTap, initial: true) { _, ready in
            guard ready else { return }
            NotificationTapHandler.shared.hasPendingTap = false
            showsPrayerTimeAlert = !NotificationTapHandler.recentlyNotifiedPrayers(database: services.database).isEmpty
        }
        .alert("Prayer Time", isPresented: $showsPrayerTimeAlert) {
            Button("Cancel", role: .cancel) {}
            Button("Pray Now") {
                services.prayerSession = PrayerSession(
                    items: NotificationTapHandler.recentlyNotifiedPrayers(database: services.database),
                    database: services.database)
            }
        } message: {
            Text("Would you like to pray for your recently notified prayer requests now?")
        }
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
