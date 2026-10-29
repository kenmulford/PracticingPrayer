import PrayerKit
import SwiftUI
import TipKit

struct CardsView: View {
    @Binding var path: [CardsRoute]
    @Environment(AppServices.self) private var services

    private let isSessionUnlocked = false
    private let tip = CollectionsTip()

    @State private var searchText = ""
    @State private var selection: CardsRoute?
    @State private var expandedIds: Set<Int64> = []
    @State private var isAddingCard = false
    @State private var load: Result<CardList, any Error>?
    @State private var tags: [PrayerTagRecord] = []
    @State private var selectedTagIds: Set<Int64> = []
    @State private var seededFilter = false
    // Hidden until the stored preference is read, so a dismissed tip never flashes.
    @State private var bannerDismissed = true

    private var isSearching: Bool {
        !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private var isNarrowed: Bool { isSearching || !selectedTagIds.isEmpty }

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle(Text(AppTab.cards.title))
                .navigationBarTitleDisplayMode(.large)
                .searchable(text: $searchText, prompt: Text("Search Cards"))
                .navigationSubtitle(Text(filterSubtitle))
                .toolbar {
                    CardsToolbar(
                        tags: tags,
                        selectedTagIds: $selectedTagIds,
                        addCard: { isAddingCard = true },
                        manageCollections: { selection = .manageCollections }
                    )
                }
                .sheet(isPresented: $isAddingCard, onDismiss: reload) {
                    CardEditorView(card: nil, database: services.database, preferences: services.preferences) { _ in }
                }
        } detail: {
            NavigationStack(path: $path) {
                detailRoot
                    .navigationDestination(for: CardsRoute.self) { route in
                        destination(route)
                    }
            }
        }
        .onAppear {
            expandedIds = Set(services.preferences.expandedSectionIds.split(separator: ",").compactMap { Int64($0) })
            bannerDismissed = services.preferences.collectionsBannerDismissed
            reloadTags()
        }
        .task(id: CardListFilter(searchText: searchText, tagIds: selectedTagIds)) { reload() }
        .task { await watchTip() }
        .onChange(of: selection) { path = [] }
    }

    @ViewBuilder
    private var sidebar: some View {
        Group {
            switch load {
            case .none:
                Color.clear
            case .failure:
                ContentUnavailableView {
                    Label("Can't Load Prayer Cards", systemImage: "exclamationmark.triangle")
                } actions: {
                    Button("Try Again") { reload() }
                        .buttonStyle(.borderedProminent)
                        .prominentLabel()
                }
            case .success(let list) where list.totalMatchCount == 0:
                if isSearching {
                    ContentUnavailableView.search(text: searchText)
                } else if !selectedTagIds.isEmpty {
                    ContentUnavailableView {
                        Label("No Matching Cards", systemImage: "line.3.horizontal.decrease.circle")
                    } description: {
                        Text("No cards have a prayer with the selected tags.")
                    } actions: {
                        Button("Clear Filter") { selectedTagIds = [] }
                    }
                } else {
                    ContentUnavailableView {
                        Label("No Prayer Cards", systemImage: "rectangle.stack")
                    } actions: {
                        Button("Add Card") { isAddingCard = true }
                    }
                }
            case .success(let list):
                cardList(list)
            }
        }
        .accessibilityIdentifier("Shell_Root_cards")
    }

    private func cardList(_ list: CardList) -> some View {
        List(selection: $selection) {
            if !bannerDismissed {
                TipView(tip) { action in
                    if action.id == "manage-collections" {
                        tip.invalidate(reason: .actionPerformed)
                        dismissBanner()
                        selection = .manageCollections
                    }
                }
                .selectionDisabled()
            }
            ForEach(list.sections.filter { !isNarrowed || !$0.cards.isEmpty }, id: \.boxId) { section in
                Section(isExpanded: expansion(for: section, in: list)) {
                    ForEach(section.cards, id: \.card.id) { item in
                        if item.isMasked {
                            CardRow(item: item).selectionDisabled()
                        } else {
                            NavigationLink(value: CardsRoute.card(id: item.card.id)) {
                                CardRow(item: item)
                            }
                        }
                    }
                } header: {
                    Text("\(sectionName(section)) · \(section.cards.count)")
                        .accessibilityAddTraits(.isHeader)
                }
            }
        }
        .listStyle(.sidebar)
    }

    private var filterSubtitle: LocalizedStringKey {
        let names = TagFilterMenu.selectedNames(selectedTagIds, in: tags).joined(separator: ", ")
        return names.isEmpty ? "" : "Filtered by \(names)"
    }

    private func sectionName(_ section: CardSection) -> String {
        section.box?.name ?? String(localized: "Loose Cards")
    }

    private func expansion(for section: CardSection, in list: CardList) -> Binding<Bool> {
        Binding(
            get: { isNarrowed || expandedIds.contains(section.boxId) },
            set: { isExpanded in
                guard !isNarrowed else { return }
                if isExpanded { expandedIds.insert(section.boxId) } else { expandedIds.remove(section.boxId) }
                services.preferences.expandedSectionIds = list.sections.map(\.boxId)
                    .filter(expandedIds.contains)
                    .map(String.init)
                    .joined(separator: ",")
            }
        )
    }

    @ViewBuilder
    private var detailRoot: some View {
        if let selection {
            destination(selection)
        } else {
            ContentUnavailableView("Select a Prayer Card", systemImage: "rectangle.stack")
        }
    }

    @ViewBuilder
    private func destination(_ route: CardsRoute) -> some View {
        switch route {
        case .card(let id):
            CardDetailView(cardId: id, onChange: reload, onClose: closeDetail)
        case .prayer:
            ContentUnavailableView("Prayer", systemImage: "hands.and.sparkles")
        case .manageCollections:
            CollectionsView()
        }
    }

    private func closeDetail() {
        if path.isEmpty { selection = nil } else { path.removeLast() }
        reload()
    }

    private func reload() {
        load = Result {
            try services.database.cardList(
                filter: CardListFilter(searchText: searchText, tagIds: selectedTagIds), isSessionUnlocked: isSessionUnlocked)
        }
    }

    private func reloadTags() {
        tags = (try? TagStore(database: services.database).tags().map(\.tag)) ?? []
        #if DEBUG
        // UI tests cannot read menu row labels, so `-CardsFilterTagIds 7,8` seeds the selection once.
        if !seededFilter, let ids = UserDefaults.standard.string(forKey: "CardsFilterTagIds") {
            selectedTagIds = Set(ids.split(separator: ",").compactMap { Int64($0) })
        }
        seededFilter = true
        #endif
        selectedTagIds = TagFilterMenu.pruned(selectedTagIds, to: tags)
    }

    private func dismissBanner() {
        services.preferences.collectionsBannerDismissed = true
        bannerDismissed = true
    }

    private func watchTip() async {
        for await _ in tip.shouldDisplayUpdates {
            if case .invalidated = tip.status {
                dismissBanner()
                return
            }
        }
    }
}
