import PrayerKit
import SwiftUI

struct PrayersView: View {
    @Binding var path: [PrayersRoute]
    @Environment(AppServices.self) private var services

    private let isSessionUnlocked = false

    @State private var selection: PrayersRoute?
    @State private var segment: PrayerSegment = .active
    @State private var searchText = ""
    @State private var selectedTagIds: Set<Int64> = []
    @State private var tags: [PrayerTagRecord] = []
    @State private var seededFilter = false
    @State private var load: Result<[PrayerListItem], any Error>?
    @State private var pendingDelete: PrayerListItem?
    @State private var errorMessage: String?
    @State private var isAddingPrayer = false
    @State private var editingPrayerID: Int64?
    @State private var prayingPrayerID: Int64?

    private struct Query: Hashable {
        var segment: PrayerSegment
        var searchText: String
        var tagIds: Set<Int64>
    }

    private var isNarrowed: Bool {
        !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !selectedTagIds.isEmpty
    }

    var body: some View {
        NavigationSplitView {
            content
                .accessibilityIdentifier("Shell_Root_prayers")
                .navigationTitle(Text(AppTab.prayers.title))
                .navigationBarTitleDisplayMode(.large)
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
                    PrayersToolbar(tags: tags, selectedTagIds: $selectedTagIds, newPrayer: { isAddingPrayer = true })
                }
                .sheet(isPresented: $isAddingPrayer) {
                    NavigationStack { Color.clear.navigationTitle("New Prayer") }
                }
                .sheet(isPresented: editingBinding) {
                    NavigationStack { Color.clear.navigationTitle("Edit Prayer") }
                }
                .confirmationDialog(
                    "Delete Prayer?", isPresented: deleteBinding, titleVisibility: .visible, presenting: pendingDelete
                ) { item in
                    Button("Delete", role: .destructive) { perform { try store.delete(id: item.record.id) } }
                    Button("Cancel", role: .cancel) {}
                }
                .alert("Couldn't Update Prayer", isPresented: errorBinding) {
                    Button("OK") {}
                } message: {
                    Text(errorMessage ?? "")
                }
        } detail: {
            NavigationStack(path: $path) {
                ContentUnavailableView("Select a Prayer", systemImage: "hands.and.sparkles")
                    .navigationDestination(for: PrayersRoute.self) { route in
                        switch route {
                        case .prayer:
                            ContentUnavailableView("Prayer", systemImage: "hands.and.sparkles")
                        }
                    }
            }
        }
        .onAppear { reloadTags() }
        .task(id: Query(segment: segment, searchText: searchText, tagIds: selectedTagIds)) { reload() }
        .onChange(of: selection) { path = [] }
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
            if isNarrowed {
                ContentUnavailableView.search(text: searchText)
            } else {
                switch segment {
                case .active: ContentUnavailableView("No Active Prayers", systemImage: "hands.and.sparkles")
                case .answered: ContentUnavailableView("No Answered Prayers", systemImage: "hands.and.sparkles")
                case .all: ContentUnavailableView("No Prayers", systemImage: "hands.and.sparkles")
                }
            }
        case .success(let items):
            List(selection: $selection) {
                ForEach(items, id: \.record.id) { item in
                    NavigationLink(value: PrayersRoute.prayer(id: item.record.id)) {
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
                        pray: { prayingPrayerID = item.record.id },
                        edit: { editingPrayerID = item.record.id },
                        requestDelete: { pendingDelete = item })
                }
            }
        }
    }

    private var deleteBinding: Binding<Bool> {
        Binding(get: { pendingDelete != nil }, set: { if !$0 { pendingDelete = nil } })
    }

    private var editingBinding: Binding<Bool> {
        Binding(get: { editingPrayerID != nil }, set: { if !$0 { editingPrayerID = nil } })
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } })
    }

    private func perform(_ change: () throws -> Void) {
        do {
            try change()
        } catch {
            errorMessage = error.localizedDescription
        }
        reload()
    }

    private func reload() {
        load = Result {
            try store.prayers(
                segment: segment, search: searchText, tagIds: selectedTagIds, isSessionUnlocked: isSessionUnlocked)
        }
    }

    private func reloadTags() {
        tags = (try? TagStore(database: services.database).tags().map(\.tag)) ?? []
        #if DEBUG
        // UI tests cannot read menu row labels, so `-PrayersFilterTagIds 7,8` seeds the selection once.
        if !seededFilter, let ids = UserDefaults.standard.string(forKey: "PrayersFilterTagIds") {
            selectedTagIds = Set(ids.split(separator: ",").compactMap { Int64($0) })
        }
        seededFilter = true
        #endif
        selectedTagIds = TagFilterMenu.pruned(selectedTagIds, to: tags)
    }
}
