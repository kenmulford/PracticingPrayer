import PrayerKit
import SwiftUI

struct HomeListView: View {
    let scope: HomeListScope
    @Environment(AppServices.self) private var services
    @State private var model: HomeListModel?

    init(scope: HomeListScope) {
        self.scope = scope
    }

    var body: some View {
        content
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .onAppear { model?.load() }
            .task {
                if model == nil {
                    model = HomeListModel(
                        scope: scope, database: services.database,
                        overdueDayThreshold: services.preferences.overdueDayThreshold)
                }
                model?.load()
            }
    }

    private var title: LocalizedStringResource {
        switch scope {
        case .overdue: "Overdue"
        case .unanswered: "Unanswered"
        case .activeCards: "Active Cards"
        }
    }

    @ViewBuilder
    private var content: some View {
        if let model {
            if model.loadFailed {
                ContentUnavailableView {
                    Label("Can't Load Prayers", systemImage: "exclamationmark.triangle")
                } actions: {
                    Button("Try Again") { model.load() }
                        .buttonStyle(.borderedProminent)
                        .prominentLabel()
                }
            } else if model.isEmpty {
                emptyState
            } else {
                list(model)
            }
        } else {
            ProgressView()
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        switch scope {
        case .overdue: ContentUnavailableView("All Prayed For", systemImage: "clock.fill")
        case .unanswered: ContentUnavailableView("No Unanswered Prayers", systemImage: "hands.and.sparkles.fill")
        case .activeCards: ContentUnavailableView("No Active Cards", systemImage: "rectangle.stack.fill")
        }
    }

    private func list(_ model: HomeListModel) -> some View {
        List {
            ForEach(model.sections ?? []) { section in
                Section {
                    ForEach(section.items, id: \.record.id) { item in
                        NavigationLink(value: HomeRoute.prayer(id: item.record.id)) {
                            PrayerListRow(
                                prayer: item.record, cardTitle: item.cardTitle,
                                isImported: item.record.isImported ?? false)
                        }
                        .prayerRowActions(
                            item,
                            setAnswered: {
                                model.setAnswered(item, answered: $0)
                                services.reconcileReminders()
                            },
                            requestDelete: { model.pendingDelete = item })
                    }
                } header: {
                    if let title = section.title {
                        Text(verbatim: title).accessibilityAddTraits(.isHeader)
                    }
                }
            }
        }
        .deletePrayerDialog(
            isPresented: Binding(get: { model.pendingDelete != nil }, set: { if !$0 { model.pendingDelete = nil } }),
            title: model.pendingDelete?.record.title ?? ""
        ) { model.confirmDelete() }
        .alert(
            "Couldn't Update Prayer",
            isPresented: Binding(get: { model.errorMessage != nil }, set: { if !$0 { model.errorMessage = nil } })
        ) {
            Button("OK") {}
        } message: {
            Text(model.errorMessage ?? "")
        }
    }
}
