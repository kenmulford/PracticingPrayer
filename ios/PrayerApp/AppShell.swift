import PrayerKit
import SwiftUI

struct AppShell: View {
    @Environment(AppServices.self) private var services
    @Environment(\.scenePhase) private var scenePhase
    @State private var selection: AppTab = .home
    @State private var homePath: [HomeRoute] = []
    @State private var cardsPath: [CardsRoute] = []
    @State private var prayersPath: [PrayersRoute] = []
    @State private var tagsPath: [TagsRoute] = []
    @State private var settingsPath: [SettingsRoute] = []
    @State private var showsPrayerTimeAlert = false
    @State private var notifiedPrayers: [PrayerListItem] = []
    @State private var coverWindow = PrivacyCoverWindow()
    @State private var isCovered = false
    @State private var relockGeneration = 0

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
            case .open, .restoring:
                tabs.fullScreenCover(item: Bindable(services).prayerSession) { PrayerSessionView(session: $0) }
                    .environment(services.onboarding)
                    .sheet(isPresented: welcomeBinding) {
                        WelcomeSheet(
                            getStarted: {
                                selection = .cards
                                services.onboarding.getStarted()
                            },
                            skip: services.onboarding.skip)
                    }
                    .sheet(item: importSheetBinding) { item in
                        if case .payload(let payload) = item.content {
                            ConfirmImportView(database: services.database, payload: payload, cardTitle: item.cardTitle)
                        }
                    }
                    .alert("Unable to Import", isPresented: failureBinding, presenting: failure) { _ in
                        Button("OK") {}
                    } message: { failure in
                        Text(failure.message)
                    }
                    .alert("Did someone share a prayer with you?", isPresented: promptBinding) {
                        Button("Cancel", role: .cancel) { services.clipboardHandoff.cancel() }
                        Button("Import") { services.clipboardHandoff.importClipboard() }
                    } message: {
                        Text("Import it from your clipboard.")
                    }
                    .alert("No Shared Prayer Found", isPresented: nothingFoundBinding) {
                        Button("OK") { services.clipboardHandoff.acknowledgeNothingFound() }
                    } message: {
                        Text("Tap the link you received to open it.")
                    }
                    .onChange(of: services.onboarding.stage, initial: true) { services.onboarding.syncTips() }
                    .task(id: services.onboarding.run) { await services.onboarding.watchTips() }
            }
        }
        .onOpenURL { services.inboundShare.handle($0) }
        .onContinueUserActivity(NSUserActivityTypeBrowsingWeb) { services.inboundShare.handle(userActivity: $0) }
        .onChange(of: services.inboundShare.isDeepLinkSession, initial: true) { _, isDeepLink in
            if isDeepLink { services.onboarding.markDeepLinkSession() }
        }
        .accessibilityHidden(isCovered)
        .onChange(of: scenePhase, initial: true) { _, phase in
            if phase == .background { services.confidential.relock() }
            if phase == .active {
                isCovered = false
                coverWindow.hide()
                return
            }
            let hasProtected = services.phase == .open
                ? PrivacyCoverPolicy.hasProtectedCards(in: services.database) : true
            isCovered = PrivacyCoverPolicy.isCovered(isSceneActive: false, hasProtectedCards: hasProtected)
            if isCovered { coverWindow.show() } else { coverWindow.hide() }
        }
        .task {
            #if DEBUG
            services.inboundShare.handleLaunchArgument()
            #endif
            await services.open()
            await services.clipboardHandoff.run(
                isEligible: services.phase == .open && services.onboarding.showsWelcome
                    && !services.inboundShare.isDeepLinkSession)
            #if DEBUG
            if ProcessInfo.processInfo.arguments.contains(PrayerSession.debugArgument), services.phase == .open {
                let items = (try? PrayerStore(database: services.database)
                    .prayers(segment: .active, isSessionUnlocked: false)) ?? []
                services.prayerSession = PrayerSession(items: Array(items.prefix(3)), database: services.database)
            }
            #endif
        }
        .onChange(of: services.confidential.isUnlocked) { _, unlocked in
            if !unlocked { closeProtectedContent() }
        }
        .onChange(of: services.phase == .open && NotificationTapHandler.shared.hasPendingTap, initial: true) { _, ready in
            guard ready else { return }
            NotificationTapHandler.shared.hasPendingTap = false
            notifiedPrayers = NotificationTapHandler.recentlyNotifiedPrayers(database: services.database)
            showsPrayerTimeAlert = !notifiedPrayers.isEmpty
        }
        .alert("Prayer Time", isPresented: $showsPrayerTimeAlert) {
            Button("Cancel", role: .cancel) {}
            Button("Pray Now") {
                services.prayerSession = PrayerSession(
                    items: notifiedPrayers, database: services.database, reconcile: { services.reconcileReminders() })
            }
        } message: {
            Text("Would you like to pray for your recently notified prayer requests now?")
        }
        .task(id: isReadyForNotificationPrompt) {
            guard isReadyForNotificationPrompt else { return }
            await requestNotificationPermissionIfNeeded()
        }
        .onChange(of: isReadyForPendingImport, initial: true) { _, ready in
            if ready, let pending = PendingImport.pickUp() {
                services.inboundShare.enqueue(pending.payload, cardTitle: pending.cardTitle)
            }
        }
    }

    // Rebuilding the tab tree closes every editor, sheet and detail screen that may show protected content;
    // the paths reset with it so no pushed screen outlives the lock.
    private func closeProtectedContent() {
        if let session = services.prayerSession, session.holdsProtectedPrayers(database: services.database) {
            services.prayerSession = nil
        }
        homePath = []
        cardsPath = []
        prayersPath = []
        tagsPath = []
        settingsPath = []
        relockGeneration += 1
    }

    // The App Group pickup joins the same FIFO as links and files, so one sheet presents every import.
    private var isReadyForPendingImport: Bool {
        scenePhase == .active && services.phase == .open
    }

    // Covers upgraders whose notification permission was never decided; the ask waits for a clear screen.
    private var isReadyForNotificationPrompt: Bool {
        scenePhase == .active && importReady && services.clipboardHandoff.isFinished
            && services.inboundShare.queue.isEmpty && !AppServices.suppressesNotificationPrompt
    }

    private func requestNotificationPermissionIfNeeded() async {
        guard NotificationPromptPolicy.shouldRequest(
            status: await NotificationAuthorization.status(),
            allowNotifications: services.preferences.allowNotifications,
            hasReminder: NotificationPromptPolicy.hasReminder(in: services.database),
            isPresentationClear: isReadyForNotificationPrompt)
        else { return }
        if await NotificationAuthorization.request() {
            services.reconcileReminders()
        } else {
            services.preferences.allowNotifications = false
        }
    }

    // The queue head waits until nothing else covers the screen (one sheet at a time).
    private var importReady: Bool {
        services.phase == .open && services.prayerSession == nil && !showsPrayerTimeAlert
            && !services.onboarding.showsWelcome
    }

    private var importSheetBinding: Binding<InboundShareHandler.Item?> {
        Binding(
            get: {
                guard importReady, let head = services.inboundShare.queue.first, case .payload = head.content else { return nil }
                return head
            },
            set: { if $0 == nil { services.inboundShare.dismissHead() } })
    }

    private var failure: InboundShareHandler.Failure? {
        guard importReady, let head = services.inboundShare.queue.first, case .failure(let failure) = head.content else { return nil }
        return failure
    }

    private var failureBinding: Binding<Bool> {
        Binding(get: { failure != nil }, set: { if !$0 { services.inboundShare.dismissHead() } })
    }

    private var welcomeBinding: Binding<Bool> {
        Binding(get: { services.onboarding.showsWelcome && services.clipboardHandoff.isFinished }, set: { _ in })
    }

    private var promptBinding: Binding<Bool> {
        Binding(get: { services.clipboardHandoff.prompt == .importPrompt }, set: { _ in })
    }

    private var nothingFoundBinding: Binding<Bool> {
        Binding(get: { services.clipboardHandoff.prompt == .nothingFound }, set: { _ in })
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
        .id(TabsIdentity(database: services.databaseGeneration, relock: relockGeneration))
        .onChange(of: services.databaseGeneration) {
            homePath = []
            cardsPath = []
            prayersPath = []
            tagsPath = []
            settingsPath = []
            selection = .home
        }
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

private struct TabsIdentity: Hashable {
    let database: Int
    let relock: Int
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
