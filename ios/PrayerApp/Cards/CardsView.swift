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
    @State private var collectionSelection: Int64?
    @State private var listRevision = 0
    @State private var rootSections = CollectionRootSections()
    @State private var compactColumn = NavigationSplitViewColumn.sidebar
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
        NavigationSplitView(preferredCompactColumn: $compactColumn) {
            sidebar
                .navigationTitle(isSelecting ? selectionTitle : Text(AppTab.cards.title))
                .toolbarVisibility(isSelecting ? .hidden : .automatic, for: .tabBar)
                .navigationBarTitleDisplayMode(.large)
                .searchable(text: $searchText, placement: .navigationBarDrawer(displayMode: .always), prompt: Text("Search Cards"))
                .navigationSubtitle(Text(filterSubtitle))
                .toolbar {
                    CardsToolbar(
                        tags: tags,
                        selectedTagIds: $selectedTagIds,
                        addCard: addCard,
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
        } content: {
            contentColumn
        } detail: {
            NavigationStack(path: $path) {
                detailRoot
                    .navigationDestination(for: CardsRoute.self) { route in
                        destination(route)
                    }
            }
        }
        .onAppear {
            bannerDismissed = services.preferences.collectionsBannerDismissed
            reloadTags()
        }
        .task(id: CardListFilter(searchText: searchText, tagIds: selectedTagIds)) { reload() }
        .task { await watchTip() }
        .onChange(of: selection) {
            path = []
            if selection != nil { compactColumn = .detail }
        }
        .onChange(of: collectionSelection) {
            selection = nil
            path = []
        }
        .onChange(of: isSessionUnlocked) {
            reload()
            if !isSessionUnlocked, let id = collectionSelection,
                case .success(let list) = load,
                let section = list.sections.first(where: { $0.boxId == id }),
                CollectionProtection.rootState(of: section.box, isSessionUnlocked: false) != .normal
            {
                collectionSelection = nil
            }
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
                    VStack {
                        addCardTipView.padding(.horizontal)
                        ContentUnavailableView {
                            Label("No Prayer Cards", systemImage: "rectangle.stack")
                        } actions: {
                            Button("Add Card", action: addCard)
                        }
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
        } else if isNarrowed {
            browsingList(list)
        } else {
            collectionsList()
        }
    }

    private func selectingList(_ list: CardList) -> some View {
        let visibleIds = Set(list.sections.flatMap { $0.cards.map(\.card.id) })
        let binding = Binding<Set<Int64>>(
            get: { selectedIds },
            set: { selectedIds = $0.union(selectedIds.subtracting(visibleIds)) })
        return List(selection: binding) {
            ForEach(list.sections.filter { !isNarrowed || !$0.cards.isEmpty }, id: \.boxId) { section in
                Section(isExpanded: .constant(true)) {
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

    private func addCard() {
        isAddingCard = true
        services.onboarding.advance(from: .addCard)
    }

    @ViewBuilder
    private var addCardTipView: some View {
        if let tip = services.onboarding.tips.currentTip as? AddCardTip {
            TipView(tip, arrowEdge: .top)
        }
    }

    private var listTips: some View {
        Group {
            addCardTipView.selectionDisabled()
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
        }
    }

    private func collectionsList() -> some View {
        List(selection: $collectionSelection) {
            listTips
            Section {
                ForEach(rootSections.user, id: \.boxId) { collectionLink($0) }
            }
            Section {
                ForEach(rootSections.system, id: \.boxId) { collectionLink($0) }
            }
        }
        .listStyle(.insetGrouped)
    }

    @ViewBuilder
    private func collectionLink(_ section: CardSection) -> some View {
        if CollectionProtection.rootState(of: section.box, isSessionUnlocked: isSessionUnlocked) == .locked {
            Button {
                Task { await openProtectedCollection(section) }
            } label: {
                LockedCollectionRow(section: section)
            }
            .buttonStyle(.plain)
            .selectionDisabled()
        } else {
            NavigationLink(value: section.boxId) {
                CollectionRow(section: section)
            }
            .tag(section.boxId)
        }
    }

    @ViewBuilder
    private var contentColumn: some View {
        if let id = collectionSelection {
            CollectionCardsView(boxId: id, selection: $selection, revision: listRevision, onChange: reload)
        } else {
            ContentUnavailableView("Select a Collection", systemImage: "folder")
        }
    }

    private func browsingList(_ list: CardList) -> some View {
        CardRowActionsHost(onChange: reload, onRemoved: deselect) { perform in
            List(selection: $selection) {
                listTips
                ForEach(list.sections.filter { !$0.cards.isEmpty }, id: \.boxId) { section in
                    Section(isExpanded: .constant(true)) {
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
                                    ) { perform($0, item) })
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
    }

    private func isHidden(_ item: CardListItem, in section: CardSection) -> Bool {
        ProtectionPolicy.effectiveMode(of: item.card, box: section.box) == .hidden
    }

    private func openProtected(_ item: CardListItem) async {
        if await services.confidential.authenticate(reason: "Open protected card") {
            selection = .card(id: item.card.id)
        }
    }

    private func openProtectedCollection(_ section: CardSection) async {
        if await services.confidential.authenticate(reason: "Open protected collection") {
            collectionSelection = section.boxId
            compactColumn = .content
        }
    }

    private var filterSubtitle: LocalizedStringKey {
        let names = TagFilterMenu.selectedNames(selectedTagIds, in: tags).joined(separator: ", ")
        return names.isEmpty ? "" : "Filtered by \(names)"
    }

    private func sectionName(_ section: CardSection) -> String {
        section.box?.name ?? String(localized: "Loose Cards")
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
            CollectionsView(onChange: reload)
        }
    }

    private func closeDetail() {
        if path.isEmpty { selection = nil } else { path.removeLast() }
        reload()
    }

    // A removed or archived card must not stay open in the detail pane.
    private func deselect(_ cardId: Int64) {
        guard selection == .card(id: cardId) else { return }
        selection = nil
        path = []
    }

    private func reload() {
        load = Result {
            try services.database.cardList(
                filter: CardListFilter(searchText: searchText, tagIds: selectedTagIds), isSessionUnlocked: isSessionUnlocked)
        }
        if case .success(let list) = load {
            rootSections = CollectionProtection.rootSections(list.sections, isSessionUnlocked: isSessionUnlocked)
        } else {
            rootSections = CollectionRootSections()
        }
        if let id = collectionSelection, case .success(let list) = load, !list.sections.contains(where: { $0.boxId == id }) {
            collectionSelection = nil
        }
        listRevision &+= 1
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

    private var moveTargets: [CardBoxRecord] { CardRowActionsHost<EmptyView>.moveTargets(services.database) }

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

private struct CollectionRow: View {
    let section: CardSection

    var body: some View {
        HStack {
            Label(
                section.box?.name ?? String(localized: "Loose Cards"),
                systemImage: section.boxId == 0 ? "tray" : "folder")
            Spacer()
            Text(section.cards.count, format: .number)
                .monospacedDigit()
                .foregroundStyle(.secondary)
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("Collections_Row_\(section.boxId)")
    }
}

private struct LockedCollectionRow: View {
    let section: CardSection

    var body: some View {
        let name = section.box?.name ?? ""
        HStack {
            Label(name, systemImage: "lock.fill")
            Spacer()
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(String(localized: "\(name), locked"))
        .accessibilityIdentifier("Collections_Row_\(section.boxId)")
    }
}
