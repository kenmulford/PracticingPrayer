import PrayerKit
import SwiftUI

struct TagPrayersView: View {
    let tagId: Int64
    @Binding var path: [TagsRoute]
    @Environment(AppServices.self) private var services

    private let isSessionUnlocked = false

    @State private var model: TagsModel?
    @State private var segment: PrayerSegment = .active
    @State private var searchText = ""
    @State private var load: Result<[PrayerListItem], any Error>?
    @State private var pendingDelete: PrayerListItem?
    @State private var errorMessage: String?
    @State private var isEditingTag = false
    @State private var editingPrayer: PrayerEditorTarget?
    @State private var prayScope: PrayerTimeScope?

    private struct Query: Hashable {
        var segment: PrayerSegment
        var searchText: String
    }

    private var tag: PrayerTagRecord? { model?.summary(id: tagId)?.tag }

    var body: some View {
        content
            .accessibilityIdentifier("TagPrayers_Root")
            .navigationTitle(tag?.name ?? "")
            .navigationBarTitleDisplayMode(.inline)
            .searchable(text: $searchText, prompt: Text("Search Prayers"))
            .safeAreaBar(edge: .top) {
                Picker("Prayers", selection: $segment) {
                    Text("Active").tag(PrayerSegment.active)
                    Text("Answered").tag(PrayerSegment.answered)
                    Text("All").tag(PrayerSegment.all)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .padding(.horizontal)
                .padding(.bottom, 8)
            }
            .toolbar {
                TagPrayersToolbar(
                    canDelete: tag.map(TagsModel.canDelete) ?? false,
                    edit: { isEditingTag = true },
                    requestDelete: { model?.pendingDelete = tag })
            }
            .sheet(isPresented: $isEditingTag, onDismiss: { reloadTag() }) {
                TagEditorView(database: services.database, tag: tag)
            }
            .prayerTimeStart($prayScope)
            .sheet(item: $editingPrayer, onDismiss: reload) { target in
                PrayerEditorView(prayerId: target.prayerId, database: services.database)
            }
            .deletePrayerDialog(isPresented: deleteBinding, title: pendingDelete?.record.title ?? "") {
                if let item = pendingDelete { perform { try store.delete(id: item.record.id) } }
            }
            .confirmationDialog(
                Text("Delete “\(model?.pendingDelete?.name ?? "")”?"), isPresented: tagDeleteBinding,
                titleVisibility: .visible, presenting: model?.pendingDelete
            ) { _ in
                Button("Delete Tag", role: .destructive) {
                    if model?.confirmDelete() == true { popSelf() }
                }
                Button("Cancel", role: .cancel) {}
            } message: { _ in
                Text("It will be removed from all prayer requests.")
            }
            .alert("Couldn't Update Prayer", isPresented: errorBinding) {
                Button("OK") {}
            } message: {
                Text(errorMessage ?? "")
            }
            .alert("Couldn't Delete Tag", isPresented: tagErrorBinding) {
                Button("OK") {}
            } message: {
                Text(model?.errorMessage ?? "")
            }
            .task {
                if model == nil { model = TagsModel(database: services.database) }
                reloadTag()
            }
            .onAppear { reload() }
            .task(id: Query(segment: segment, searchText: searchText)) { reload() }
    }

    private var store: PrayerStore { PrayerStore(database: services.database) }

    @ViewBuilder
    private var content: some View {
        switch load {
        case .none:
            ProgressView()
        case .failure:
            ContentUnavailableView {
                Label("Can't Load Prayers", systemImage: "exclamationmark.triangle")
            } actions: {
                Button("Try Again") { reload() }
                    .buttonStyle(.borderedProminent)
                    .prominentLabel()
            }
        case .success(let items) where items.isEmpty:
            if !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                ContentUnavailableView.search(text: searchText)
            } else {
                switch segment {
                case .active: ContentUnavailableView("No Active Prayers", systemImage: "hands.and.sparkles")
                case .answered: ContentUnavailableView("No Answered Prayers", systemImage: "hands.and.sparkles")
                case .all: ContentUnavailableView("No Prayers", systemImage: "hands.and.sparkles")
                }
            }
        case .success(let items):
            List {
                ForEach(items, id: \.record.id) { item in
                    NavigationLink(value: TagsRoute.prayer(id: item.record.id)) {
                        PrayerListRow(
                            prayer: item.record, cardTitle: item.cardTitle, isImported: item.record.isImported ?? false)
                    }
                    .prayerRowActions(
                            item,
                            setAnswered: { answered in
                                perform {
                                    if answered {
                                        try store.markAnswered(id: item.record.id, now: .now)
                                    } else {
                                        try store.markActive(id: item.record.id, now: .now)
                                    }
                                }
                            },
                            pray: { prayScope = .prayers([item.record.id]) },
                            edit: { editingPrayer = .edit(item.record.id) },
                            requestDelete: { pendingDelete = item })
                }
            }
        }
    }

    private var deleteBinding: Binding<Bool> {
        Binding(get: { pendingDelete != nil }, set: { if !$0 { pendingDelete = nil } })
    }

    private var tagDeleteBinding: Binding<Bool> {
        Binding(get: { model?.pendingDelete != nil }, set: { if !$0 { model?.pendingDelete = nil } })
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } })
    }

    private var tagErrorBinding: Binding<Bool> {
        Binding(get: { model?.errorMessage != nil }, set: { if !$0 { model?.errorMessage = nil } })
    }

    private func perform(_ change: () throws -> Void) {
        do {
            try change()
            services.reconcileReminders()
        } catch {
            errorMessage = error.localizedDescription
        }
        reload()
    }

    private func reload() {
        load = Result {
            try store.prayers(
                segment: segment, search: searchText, tagIds: [tagId], isSessionUnlocked: isSessionUnlocked)
        }
    }

    private func reloadTag() {
        model?.reload()
        if model?.summary(id: tagId) == nil, case .success? = model?.load { popSelf() }
    }

    private func popSelf() {
        path.removeAll { $0 == .tagPrayers(id: tagId) }
    }
}
