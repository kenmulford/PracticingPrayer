import PrayerKit
import SwiftUI

struct CollectionCardsView: View {
    let boxId: Int64
    @Binding var selection: CardsRoute?
    let revision: Int
    let onChange: () -> Void

    @Environment(AppServices.self) private var services
    @State private var load: Result<CardList, any Error>?
    @State private var isSelecting = false
    @State private var selectedIds: Set<Int64> = []
    @State private var selectedItems: [CardListItem] = []
    @State private var isWorking = false
    @State private var bulkError: String?
    @State private var isMoving = false
    @State private var confirmArchive = false
    @State private var confirmDelete = false
    @State private var prayScope: PrayerTimeScope?
    @State private var isAddingCard = false
    @State private var canPrayCollection = false
    @State private var seededSelectMode = false

    init(boxId: Int64, selection: Binding<CardsRoute?>, revision: Int, onChange: @escaping () -> Void) {
        self.boxId = boxId
        _selection = selection
        self.revision = revision
        self.onChange = onChange
    }

    private var isSessionUnlocked: Bool { services.confidential.isUnlocked }

    private var section: CardSection? {
        guard case .success(let list) = load else { return nil }
        return list.sections.first { $0.boxId == boxId }
    }

    private var name: String { section?.box?.name ?? String(localized: "Loose Cards") }

    private var isUserCollection: Bool {
        boxId != 0 && section?.box?.isSystem != true && section?.box?.systemKey != "archived"
    }

    // A masked card's prayers decide nothing here, so they never enable Pray (#449).
    private var canPray: Bool {
        guard let section else { return false }
        if isUserCollection { return canPrayCollection }
        return section.cards.contains { !$0.isMasked && ($0.activePrayerCount ?? 0) > 0 }
    }

    private var canSelect: Bool { section?.cards.contains { !$0.isMasked } ?? false }

    private var mutableItems: [CardListItem] { selectedItems.filter { $0.card.isSystem != true } }

    private var selectState: CardsSelectState {
        CardsSelectState(
            isSelecting: isSelecting, canSelect: canSelect, hasSelection: !selectedIds.isEmpty,
            hasMutable: !mutableItems.isEmpty, isWorking: isWorking,
            start: { isSelecting = true }, done: endSelecting,
            moveTo: { isMoving = true }, archive: { confirmArchive = true }, delete: { confirmDelete = true })
    }

    private var selectionTitle: Text {
        selectedIds.isEmpty ? Text("None Selected") : Text("\(selectedIds.count) Selected")
    }

    var body: some View {
        content
            .navigationTitle(isSelecting ? selectionTitle : Text(verbatim: name))
            .navigationSubtitle(isSelecting ? Text(verbatim: name) : Text(verbatim: ""))
            .toolbarVisibility(isSelecting ? .hidden : .automatic, for: .tabBar)
            .navigationBarTitleDisplayMode(.large)
            .toolbar {
                CollectionCardsToolbar(canPray: canPray, pray: pray, addCard: addCard, select: selectState)
            }
            .prayerTimeStart($prayScope)
            .sheet(isPresented: $isAddingCard, onDismiss: onChange) {
                CardEditorView(
                    card: nil, initialBoxId: boxId, database: services.database, preferences: services.preferences
                ) { _ in }
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
            .onChange(of: selectedIds) { refreshSelection() }
            .task(id: boxId) { reload() }
            .onChange(of: isSessionUnlocked) { reload() }
            .onChange(of: selection) { reload() }
            .onChange(of: revision) { reload() }
    }

    @ViewBuilder
    private var content: some View {
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
        case .success:
            if let section, !section.cards.isEmpty {
                if isSelecting {
                    selectingList(section)
                } else {
                    CardRowActionsHost(onChange: onChange, onRemoved: deselect) { perform in
                        cardList(section, perform: perform)
                    }
                }
            } else {
                ContentUnavailableView("No Cards", systemImage: "rectangle.stack")
            }
        }
    }

    private func cardList(
        _ section: CardSection, perform: @escaping (CardRowAction, CardListItem) -> Void
    ) -> some View {
        List(selection: $selection) {
            Section {
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
            }
        }
        .listStyle(.insetGrouped)
    }

    private func selectingList(_ section: CardSection) -> some View {
        List(selection: $selectedIds) {
            Section {
                ForEach(section.cards, id: \.card.id) { item in
                    if item.isMasked {
                        CardRow(item: item).selectionDisabled()
                    } else {
                        CardRow(
                            item: item, isSelected: selectedIds.contains(item.card.id),
                            isHidden: isHidden(item, in: section)
                        )
                        .tag(item.card.id)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .environment(\.editMode, .constant(.active))
    }

    private func isHidden(_ item: CardListItem, in section: CardSection) -> Bool {
        ProtectionPolicy.effectiveMode(of: item.card, box: section.box) == .hidden
    }

    private func openProtected(_ item: CardListItem) async {
        if await services.confidential.authenticate(reason: "Open protected card") {
            selection = .card(id: item.card.id)
        }
    }

    // A removed or archived card must not stay open in the detail pane.
    private func deselect(_ cardId: Int64) {
        if selection == .card(id: cardId) { selection = nil }
    }

    private func reload() {
        load = Result { try services.database.cardList(isSessionUnlocked: isSessionUnlocked) }
        canPrayCollection = ((try? PrayerTimeLauncher(database: services.database)
            .collectionOptions(isSessionUnlocked: isSessionUnlocked)) ?? []).contains { $0.id == boxId }
        #if DEBUG
        if !seededSelectMode, UserDefaults.standard.bool(forKey: "CardsSelectMode") { isSelecting = true }
        seededSelectMode = true
        #endif
        refreshSelection()
    }

    private func addCard() {
        isAddingCard = true
        services.onboarding.advance(from: .addCard)
    }

    // Masked cards join the session after Face ID, so every listed id is passed.
    private func pray() {
        guard let section else { return }
        prayScope = isUserCollection ? .collection(boxId) : .cards(Set(section.cards.map(\.card.id)))
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
        let all = section?.cards.filter { !$0.isMasked && selectedIds.contains($0.card.id) } ?? []
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
        onChange()
    }
}
