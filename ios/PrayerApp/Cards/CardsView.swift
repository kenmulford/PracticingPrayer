import PrayerKit
import SwiftUI
import TipKit

struct CardsView: View {
    @Binding var path: [CardsRoute]
    @Environment(AppServices.self) private var services

    private var isSessionUnlocked: Bool { services.confidential.isUnlocked }
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
    @State private var isSelecting = false
    @State private var selectedIds: Set<Int64> = []
    @State private var selectedItems: [CardListItem] = []
    @State private var isWorking = false
    @State private var bulkError: String?
    @State private var isMoving = false
    @State private var confirmArchive = false
    @State private var confirmDelete = false
    @State private var cardToDelete: CardListItem?
    @State private var rowCard: CardListItem?
    @State private var isEditingRow = false
    @State private var isMovingRow = false
    @State private var rowError: String?
    @State private var prayScope: PrayerTimeScope?

    private var isSearching: Bool {
        !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private var isNarrowed: Bool { isSearching || !selectedTagIds.isEmpty }

    private var canSelect: Bool {
        guard case .success(let list) = load else { return false }
        return list.sections.contains { $0.cards.contains { !$0.isMasked } }
    }

    private var mutableItems: [CardListItem] { selectedItems.filter { $0.card.isSystem != true } }

    private var selectState: CardsSelectState {
        CardsSelectState(
            isSelecting: isSelecting, canSelect: canSelect, hasSelection: !selectedIds.isEmpty,
            hasMutable: !mutableItems.isEmpty, isWorking: isWorking,
            start: { isSelecting = true }, done: endSelecting,
            moveTo: startMove, archive: { confirmArchive = true }, delete: { confirmDelete = true })
    }

    private var selectionTitle: Text {
        selectedIds.isEmpty ? Text("None Selected") : Text("\(selectedIds.count) Selected")
    }

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle(isSelecting ? selectionTitle : Text(AppTab.cards.title))
                .toolbarVisibility(isSelecting ? .hidden : .automatic, for: .tabBar)
                .navigationBarTitleDisplayMode(.large)
                .searchable(text: $searchText, prompt: Text("Search Cards"))
                .navigationSubtitle(Text(filterSubtitle))
                .toolbar {
                    CardsToolbar(
                        tags: tags,
                        selectedTagIds: $selectedTagIds,
                        addCard: {
                            isAddingCard = true
                            services.onboarding.advance(from: .addCard)
                        },
                        manageCollections: { selection = .manageCollections },
                        isUnlocked: isSessionUnlocked,
                        toggleConfidential: services.confidential.toggle,
                        select: selectState
                    )
                }
                .sheet(isPresented: $isMoving) {
                    MoveToCollectionSheet(collections: moveTargets, currentBoxId: nil, select: moveSelected)
                }
                .confirmationDialog("Archive Cards?", isPresented: $confirmArchive, titleVisibility: .visible) {
                    Button("Archive Cards") { archiveSelected() }
                    Button("Cancel", role: .cancel) {}
                } message: {
                    Text("Archive \(mutableItems.count) cards? You can find them in your Archived Cards collection.")
                }
                .confirmationDialog(
                    Text("Delete \(mutableItems.count) Cards?"), isPresented: $confirmDelete, titleVisibility: .visible
                ) {
                    Button("Delete Cards", role: .destructive) { deleteSelected() }
                    Button("Cancel", role: .cancel) {}
                } message: {
                    let prayers = mutableItems.reduce(0) { $0 + ($1.activePrayerCount ?? 0) }
                    if prayers > 0 {
                        Text("This also deletes their \(prayers) active prayer requests.")
                    }
                }
                .alert("Couldn't Update Cards", isPresented: errorBinding) {
                    Button("OK") {}
                } message: {
                    Text(bulkError ?? "")
                }
                .sheet(isPresented: $isAddingCard, onDismiss: reload) {
                    CardEditorView(card: nil, database: services.database, preferences: services.preferences) { _ in }
                }
                .modifier(
                    CardDeleteConfirmation(
                        item: $cardToDelete, archivedFolderId: Int64(services.preferences.archivedFolderId)
                    ) { card in
                        rowError = CardRowActionModel.perform(
                            .delete, cardId: card.card.id, database: services.database,
                            preferences: services.preferences)
                        if rowError == nil { services.reconcileReminders() }
                        reload()
                    }
                )
                .sheet(isPresented: $isEditingRow, onDismiss: reload) {
                    CardEditorView(
                        card: rowCard?.card, database: services.database, preferences: services.preferences
                    ) { _ in }
                }
                .sheet(isPresented: $isMovingRow) {
                    MoveToCollectionSheet(
                        collections: moveTargets, currentBoxId: rowCard?.card.boxId ?? 0, select: moveRowCard)
                }
                .prayerTimeStart($prayScope)
                .alert("Couldn't Update Card", isPresented: rowErrorBinding) {
                    Button("OK") {}
                } message: {
                    Text(rowError ?? "")
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
        .onChange(of: isSessionUnlocked) {
            reload()
            reloadTags()
        }
        .onChange(of: selectedIds) { refreshSelection() }
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

    @ViewBuilder
    private func cardList(_ list: CardList) -> some View {
        if isSelecting {
            selectingList(list)
        } else {
            browsingList(list)
        }
    }

    private func selectingList(_ list: CardList) -> some View {
        let visibleIds = Set(list.sections.flatMap { $0.cards.map(\.card.id) })
        let binding = Binding<Set<Int64>>(
            get: { selectedIds },
            set: { selectedIds = $0.union(selectedIds.subtracting(visibleIds)) })
        return List(selection: binding) {
            ForEach(list.sections.filter { !isNarrowed || !$0.cards.isEmpty }, id: \.boxId) { section in
                Section(isExpanded: expansion(for: section, in: list)) {
                    ForEach(section.cards, id: \.card.id) { item in
                        if item.isMasked {
                            CardRow(item: item).selectionDisabled()
                        } else {
                            CardRow(item: item, isSelected: selectedIds.contains(item.card.id), isHidden: isHidden(item, in: section))
                                .tag(item.card.id)
                        }
                    }
                } header: {
                    Text("\(sectionName(section)) · \(section.cards.count)")
                        .accessibilityAddTraits(.isHeader)
                }
            }
        }
        .listStyle(.sidebar)
        .environment(\.editMode, .constant(.active))
    }

    private func browsingList(_ list: CardList) -> some View {
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
                            Button {
                                Task { await openProtected(item) }
                            } label: {
                                CardRow(item: item)
                            }
                            .buttonStyle(.plain)
                            .selectionDisabled()
                        } else {
                            NavigationLink(value: CardsRoute.card(id: item.card.id)) {
                                CardRow(item: item, isHidden: isHidden(item, in: section))
                            }
                            .modifier(
                                CardRowActions(
                                    item: item, archivedFolderId: Int64(services.preferences.archivedFolderId)
                                ) { rowAction($0, item) })
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

    private func isHidden(_ item: CardListItem, in section: CardSection) -> Bool {
        ProtectionPolicy.effectiveMode(of: item.card, box: section.box) == .hidden
    }

    private func openProtected(_ item: CardListItem) async {
        if await services.confidential.authenticate(reason: "Open protected card") {
            selection = .card(id: item.card.id)
        }
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
        case .prayer(let id):
            PrayerDetailView(prayerId: id, cardRoute: CardsRoute.card(id:), onChange: reload, onClose: closeDetail)
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
        refreshSelection()
    }

    // MARK: - Select mode

    private var bulk: CardBulkActions {
        CardBulkActions(database: services.database, preferences: services.preferences)
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { bulkError != nil }, set: { if !$0 { bulkError = nil } })
    }

    private func refreshSelection() {
        guard !selectedIds.isEmpty || !selectedItems.isEmpty else { return }
        let all = (try? services.database.cardList(isSessionUnlocked: isSessionUnlocked))?.sections
            .flatMap(\.cards).filter { !$0.isMasked && selectedIds.contains($0.card.id) } ?? []
        selectedItems = all
        let kept = Set(all.map(\.card.id))
        if kept != selectedIds { selectedIds = kept }
    }

    private func endSelecting() {
        isSelecting = false
        selectedIds = []
        selectedItems = []
        AccessibilityNotification.Announcement(String(localized: "Selection cancelled")).post()
    }

    private func startMove() {
        isMoving = true
    }

    // Read at presentation time: a @State copy set in the same transaction as the sheet flag reached the sheet empty.
    private var moveTargets: [CardBoxRecord] {
        (try? CollectionStore(database: services.database).collections()
            .filter { !$0.isSystem }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }) ?? []
    }

    // MARK: - Row actions

    private var rowErrorBinding: Binding<Bool> {
        Binding(get: { rowError != nil }, set: { if !$0 { rowError = nil } })
    }

    private func rowAction(_ action: CardRowAction, _ item: CardListItem) {
        switch action {
        case .pray:
            prayScope = .cards([item.card.id])
        case .edit:
            rowCard = item
            isEditingRow = true
        case .moveTo:
            rowCard = item
            isMovingRow = true
        case .delete:
            cardToDelete = item
        case .share:
            break
        case .favorite, .unfavorite, .archive, .unarchive:
            rowError = CardRowActionModel.perform(
                action, cardId: item.card.id, database: services.database, preferences: services.preferences)
            reload()
        }
    }

    private func moveRowCard(toBox boxId: Int64) {
        guard let card = rowCard else { return }
        rowError = CardRowActionModel.move(
            cardId: card.card.id, toBox: boxId, database: services.database, preferences: services.preferences)
        reload()
    }

    private func moveSelected(toBox boxId: Int64) {
        let name = boxId == 0
            ? String(localized: "Loose Cards") : moveTargets.first { $0.id == boxId }?.name ?? ""
        finish(bulk.move(selectedIds, toBox: boxId)) { String(localized: "Moved \($0) cards to \(name)") }
    }

    private func archiveSelected() {
        finish(bulk.archive(Set(mutableItems.map(\.card.id)))) { String(localized: "Archived \($0) cards") }
    }

    private func deleteSelected() {
        finish(bulk.delete(Set(mutableItems.map(\.card.id)))) { String(localized: "Deleted \($0) cards") }
        services.reconcileReminders()
    }

    private func finish(_ result: CardBulkResult, announcement: (Int) -> String) {
        selectedIds.subtract(result.succeeded)
        if let message = result.errorMessage {
            bulkError = message
        } else {
            isSelecting = false
            selectedIds = []
            selectedItems = []
            AccessibilityNotification.Announcement(announcement(result.succeeded.count)).post()
        }
        reload()
    }

    private func reloadTags() {
        tags = (try? TagStore(database: services.database).tags(isSessionUnlocked: isSessionUnlocked).map(\.tag)) ?? []
        #if DEBUG
        // UI tests cannot read menu row labels, so `-CardsFilterTagIds 7,8` seeds the selection once.
        if !seededFilter, let ids = UserDefaults.standard.string(forKey: "CardsFilterTagIds") {
            selectedTagIds = Set(ids.split(separator: ",").compactMap { Int64($0) })
        }
        if !seededFilter, UserDefaults.standard.bool(forKey: "CardsSelectMode") { isSelecting = true }
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
