import Observation
import PrayerKit
import SwiftUI

@Observable
final class HomeModel {
    private(set) var metrics = HomeMetrics(
        overdue: [], activeCards: 0, unanswered: 0, lastPrayed: nil, answeredOnThisDay: [])
    private(set) var loadFailed = false

    func load(_ read: @escaping @Sendable () throws -> HomeMetrics) async {
        do {
            metrics = try await Task.detached { try read() }.value
            loadFailed = false
        } catch {
            loadFailed = true
        }
    }

    static func columnCount(
        dynamicTypeSize: DynamicTypeSize, horizontalSizeClass: UserInterfaceSizeClass?
    ) -> Int {
        if dynamicTypeSize.isAccessibilitySize { return 1 }
        return horizontalSizeClass == .regular ? 4 : 2
    }
}

struct HomeView: View {
    @Environment(AppServices.self) private var services
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @Environment(\.horizontalSizeClass) private var horizontalSizeClass
    @State private var model = HomeModel()
    @State private var showingQuickAdd = false

    var body: some View {
        content
            .navigationTitle("Home")
            .navigationBarTitleDisplayMode(.large)
            .navigationDestination(for: HomeRoute.self) { route in
                switch route {
                case .overdue: HomeListView(scope: .overdue)
                case .unanswered: HomeListView(scope: .unanswered)
                case .activeCards: HomeListView(scope: .activeCards)
                case .prayer(let id): PushedPrayerDetail(prayerId: id, cardRoute: HomeRoute.card(id:))
                case .card(let id): PushedCardDetail(cardId: id)
                }
            }
            .toolbar {
                if #available(iOS 27, *) {
                    ToolbarItem(placement: .topBarTrailing) { quickAddButton }
                        .visibilityPriority(.high)
                } else {
                    ToolbarItem(placement: .topBarTrailing) { quickAddButton }
                }
            }
            .sheet(isPresented: $showingQuickAdd, onDismiss: reload) {
                QuickAddView(database: services.database)
            }
            .onAppear(perform: reload)
    }

    private var quickAddButton: some View {
        Button("Quick Add", systemImage: "plus") { showingQuickAdd = true }
    }

    private var content: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                if model.loadFailed {
                    ContentUnavailableView {
                        Label("Unable to Load Home", systemImage: "exclamationmark.triangle")
                    } actions: {
                        Button("Retry", action: reload)
                    }
                } else {
                    LazyVGrid(columns: columns, spacing: 12) {
                        tiles
                    }
                    if !model.metrics.answeredOnThisDay.isEmpty {
                        AnsweredOnThisDaySection(prayers: model.metrics.answeredOnThisDay)
                    }
                }
            }
            .frame(maxWidth: 700)
            .padding(.horizontal)
            .frame(maxWidth: .infinity)
        }
        .accessibilityIdentifier("Shell_Root_home")
    }

    private var columns: [GridItem] {
        let count = HomeModel.columnCount(
            dynamicTypeSize: dynamicTypeSize, horizontalSizeClass: horizontalSizeClass)
        return Array(repeating: GridItem(.flexible(), spacing: 12), count: count)
    }

    @ViewBuilder
    private var tiles: some View {
        let metrics = model.metrics
        NavigationLink(value: HomeRoute.overdue) {
            HomeTileView(
                title: "Overdue", systemImage: "clock.fill", color: .orange,
                value: count(metrics.overdue.count),
                caption: metrics.overdue.isEmpty ? "All Prayed For" : nil)
        }
        .buttonStyle(.plain)
        NavigationLink(value: HomeRoute.unanswered) {
            HomeTileView(
                title: "Unanswered", systemImage: "hands.and.sparkles.fill", color: .accentColor,
                value: count(metrics.unanswered))
        }
        .buttonStyle(.plain)
        NavigationLink(value: HomeRoute.activeCards) {
            HomeTileView(
                title: "Active Cards", systemImage: "rectangle.stack.fill", color: .blue,
                value: count(metrics.activeCards))
        }
        .buttonStyle(.plain)
        HomeTileView(
            title: "Last Prayed", systemImage: "calendar", color: .gray,
            value: lastPrayedText(metrics.lastPrayed))
    }

    private func count(_ n: Int) -> Text {
        Text(n, format: .number).font(.title).bold().monospacedDigit()
    }

    private func lastPrayedText(_ date: Date?) -> Text {
        if let date {
            Text(date.formatted(date: .abbreviated, time: .omitted)).font(.headline)
        } else {
            Text("Not Yet").font(.headline)
        }
    }

    private func reload() {
        let database = services.database
        let threshold = services.preferences.overdueDayThreshold
        Task {
            await model.load {
                try database.homeMetrics(
                    now: .now, overdueDayThreshold: threshold, isSessionUnlocked: false)
            }
        }
    }
}
