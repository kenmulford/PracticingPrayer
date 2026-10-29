import PrayerKit
import SwiftUI

struct CardDetailView: View {
    let cardId: Int64
    let onChange: () -> Void
    let onClose: () -> Void

    @Environment(AppServices.self) private var services
    @State private var model: CardDetailModel?

    var body: some View {
        Group {
            if let model {
                CardDetailContent(model: model, onChange: onChange, onClose: onClose)
            } else {
                Color.clear
            }
        }
        .task(id: cardId) {
            let loaded = CardDetailModel(cardId: cardId, database: services.database, preferences: services.preferences)
            loaded.load()
            model = loaded
        }
    }
}

private struct CardDetailContent: View {
    let model: CardDetailModel
    let onChange: () -> Void
    let onClose: () -> Void

    @Environment(AppServices.self) private var services
    @State private var isMoving = false
    @State private var isEditing = false
    @State private var isAddingPrayer = false
    @State private var confirmArchive = false
    @State private var confirmDelete = false
    @State private var prayerToDelete: PrayerRequestRecord?

    private var isSearching: Bool {
        !model.searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var body: some View {
        List {
            if !model.activePrayers.isEmpty {
                Section {
                    ForEach(model.activePrayers, id: \.id) { prayerRow($0) }
                } header: {
                    Text("Prayers").accessibilityAddTraits(.isHeader)
                }
            }
            if !model.answeredPrayers.isEmpty {
                Section {
                    ForEach(model.answeredPrayers, id: \.id) { prayerRow($0) }
                } header: {
                    Text("Answered").accessibilityAddTraits(.isHeader)
                }
            }
            if model.isSystem {
                Section {} footer: {
                    Text("This is the Quick Add card, so it can't be edited, favorited, archived, or deleted.")
                }
            }
        }
        .overlay { emptyState }
        .navigationTitle(model.card?.title ?? "")
        .navigationSubtitle(model.collectionName)
        .navigationBarTitleDisplayMode(.large)
        .searchable(text: searchBinding, prompt: Text("Search Prayers"))
        .toolbar { toolbar }
        .sheet(isPresented: $isAddingPrayer, onDismiss: refresh) {
            PrayerEditorView(
                prayerId: nil, cardId: model.card?.id, cardTitle: model.card?.title, database: services.database)
        }
        .sheet(isPresented: $isEditing, onDismiss: refresh) {
            CardEditorView(card: model.card, database: services.database, preferences: services.preferences) { outcome in
                if outcome == .deleted || outcome == .archived { onClose() }
            }
        }
        .sheet(isPresented: $isMoving) {
            MoveToCollectionSheet(
                collections: model.moveTargets, currentBoxId: model.card?.boxId ?? 0
            ) { boxId in
                perform { model.move(toBox: boxId) }
            }
        }
        .confirmationDialog("Archive Card?", isPresented: $confirmArchive, titleVisibility: .visible) {
            Button("Archive") {
                if model.archive() { onClose() }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Are you sure you want to Archive this card? You can find it in your Archived Cards collection.")
        }
        .confirmationDialog(
            Text("Delete “\(model.card?.title ?? "")”?"), isPresented: $confirmDelete, titleVisibility: .visible
        ) {
            Button("Delete Card", role: .destructive) {
                if model.delete() { onClose() }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            switch model.prayerCount {
            case 0: EmptyView()
            case 1: Text("This also deletes its 1 prayer request.")
            default: Text("This also deletes its \(model.prayerCount) prayer requests.")
            }
        }
        .deletePrayerDialog(isPresented: prayerDeleteBinding, title: prayerToDelete?.title ?? "") {
            if let prayer = prayerToDelete { perform { model.deletePrayer(id: prayer.id) } }
        }
        .alert("Couldn't Update Card", isPresented: errorBinding) {
            Button("OK") {}
        } message: {
            Text(model.errorMessage ?? "")
        }
    }

    // MARK: - Toolbar

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) { toolbarButtons }
                .visibilityPriority(.high)
        } else {
            ToolbarItemGroup(placement: .topBarTrailing) { toolbarButtons }
        }
    }

    @ViewBuilder
    private var toolbarButtons: some View {
        Button { isAddingPrayer = true } label: {
            Label("New Prayer", systemImage: "plus")
        }
        Menu {
            if !model.isSystem {
                Button(
                    model.card?.isFavorite == true ? "Unfavorite" : "Favorite",
                    systemImage: model.card?.isFavorite == true ? "star.slash" : "star"
                ) { perform { model.toggleFavorite() } }
                Button("Edit", systemImage: "pencil") { isEditing = true }
            }
            Button("Move To…", systemImage: "folder") { isMoving = true }
            if !model.isSystem {
                if model.isArchived {
                    Button("Unarchive", systemImage: "tray.and.arrow.up") { perform { model.unarchive() } }
                } else {
                    Button("Archive", systemImage: "archivebox") { confirmArchive = true }
                }
                Button("Delete", systemImage: "trash", role: .destructive) { confirmDelete = true }
            }
        } label: {
            Label("More", systemImage: "ellipsis")
        }
        .disabled(model.isMutating)
    }

    // MARK: - Rows

    private func prayerRow(_ prayer: PrayerRequestRecord) -> some View {
        NavigationLink(value: CardsRoute.prayer(id: prayer.id)) {
            PrayerRow(prayer: prayer)
        }
        .swipeActions(edge: .leading) { statusButton(prayer) }
        .swipeActions(edge: .trailing) {
            Button("Delete", systemImage: "trash", role: .destructive) { prayerToDelete = prayer }
        }
        .contextMenu {
            statusButton(prayer)
            Button("Delete", systemImage: "trash", role: .destructive) { prayerToDelete = prayer }
        }
    }

    @ViewBuilder
    private func statusButton(_ prayer: PrayerRequestRecord) -> some View {
        if prayer.isAnswered {
            Button("Mark Active", systemImage: "arrow.uturn.backward") {
                perform { model.markActive(prayerId: prayer.id) }
            }
        } else {
            Button("Mark Answered", systemImage: "checkmark.circle") {
                perform { model.markAnswered(prayerId: prayer.id) }
            }
            .tint(.green)
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        if model.prayerCount == 0 {
            ContentUnavailableView {
                Label("No Prayers", systemImage: "hands.and.sparkles")
            } description: {
                Text("Add a prayer to this card.")
            } actions: {
                Button("New Prayer") { isAddingPrayer = true }
            }
        } else if isSearching && model.activePrayers.isEmpty && model.answeredPrayers.isEmpty {
            ContentUnavailableView.search(text: model.searchText)
        }
    }

    // MARK: - Helpers

    private func perform(_ action: () -> Void) {
        action()
        if model.errorMessage == nil { onChange() }
    }

    private func refresh() {
        model.load()
        onChange()
    }

    private var searchBinding: Binding<String> {
        Binding(
            get: { model.searchText },
            set: {
                model.searchText = $0
                model.load()
            })
    }

    private var errorBinding: Binding<Bool> {
        Binding(
            get: { model.errorMessage != nil },
            set: { if !$0 { model.errorMessage = nil } })
    }

    private var prayerDeleteBinding: Binding<Bool> {
        Binding(
            get: { prayerToDelete != nil },
            set: { if !$0 { prayerToDelete = nil } })
    }
}

private struct PrayerRow: View {
    let prayer: PrayerRequestRecord

    private var answeredDate: Date? {
        prayer.answeredAtTicks.flatMap { try? Date(ticks: $0) }
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading) {
                Text(verbatim: prayer.title)
                if prayer.isAnswered, let answeredDate {
                    Text(answeredDate.formatted(date: .abbreviated, time: .omitted))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            if prayer.isAnswered {
                Spacer()
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
                    .accessibilityLabel("Answered")
            }
        }
        .accessibilityElement(children: .combine)
    }
}
