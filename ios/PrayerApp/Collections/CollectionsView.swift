import PrayerKit
import SwiftUI

struct CollectionsView: View {
    @Environment(AppServices.self) private var services

    private struct Loaded {
        let collections: [CardBoxRecord]
        let counts: [Int64: Int]
    }

    private struct EditorTarget: Identifiable {
        let record: CardBoxRecord?
        var id: Int64 { record?.id ?? 0 }
    }

    @State private var load: Result<Loaded, any Error>?
    @State private var editor: EditorTarget?
    @State private var deleteTarget: CardBoxRecord?
    @State private var showingDelete = false
    @State private var deleteError: String?

    var body: some View {
        content
            .navigationTitle("Collections")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if #available(iOS 27, *) {
                    ToolbarItem(placement: .topBarTrailing) { addButton }
                } else {
                    ToolbarItem(placement: .topBarTrailing) { addButton }
                }
            }
            .sheet(item: $editor, onDismiss: reload) { target in
                CollectionEditorView(database: services.database, collection: target.record, onFinish: {})
            }
            .deleteCollectionDialog(isPresented: $showingDelete, name: deleteTarget?.name ?? "") { option in
                guard let target = deleteTarget else { return }
                Task { await delete(target, option) }
            }
            .alert("Couldn't Delete", isPresented: errorBinding) {
                Button("OK") {}
            } message: {
                Text(deleteError ?? "")
            }
            .task { reload() }
    }

    private var addButton: some View {
        Button("Add Collection", systemImage: "plus") { editor = EditorTarget(record: nil) }
            .accessibilityIdentifier("Collections_Add")
    }

    @ViewBuilder
    private var content: some View {
        switch load {
        case .none:
            Color.clear
        case .failure:
            ContentUnavailableView {
                Label("Can't Load Collections", systemImage: "exclamationmark.triangle")
            } actions: {
                Button("Try Again") { reload() }
                    .buttonStyle(.borderedProminent)
            }
        case .success(let loaded):
            list(loaded)
        }
    }

    private func list(_ loaded: Loaded) -> some View {
        let user = loaded.collections.filter { !$0.isSystem }
        return List {
            Section {
                ForEach(loaded.collections, id: \.id) { box in
                    if box.isSystem {
                        row(box, trailing: Text("System"))
                    } else {
                        Button { editor = EditorTarget(record: box) } label: {
                            row(box, trailing: Text("\(loaded.counts[box.id] ?? 0) cards"))
                        }
                        .buttonStyle(.plain)
                        .swipeActions(edge: .trailing) {
                            Button("Delete", role: .destructive) { confirmDelete(box) }
                        }
                        .contextMenu {
                            Button("Edit", systemImage: "pencil") { editor = EditorTarget(record: box) }
                            Button("Delete", systemImage: "trash", role: .destructive) { confirmDelete(box) }
                        }
                    }
                }
            }
            if user.isEmpty {
                Section {
                    Text("No Collections Yet")
                        .foregroundStyle(.secondary)
                    Button("Add Collection") { editor = EditorTarget(record: nil) }
                }
            }
        }
    }

    private func row(_ box: CardBoxRecord, trailing: Text) -> some View {
        HStack {
            Text(box.name)
            Spacer()
            trailing
                .foregroundStyle(.secondary)
                .monospacedDigit()
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    private var errorBinding: Binding<Bool> {
        Binding(
            get: { deleteError != nil },
            set: { if !$0 { deleteError = nil } })
    }

    private func delete(_ target: CardBoxRecord, _ option: CollectionDeleteOption) async {
        var verified = false
        if CollectionEditorModel.requiresOwnerVerificationToDelete(target) {
            guard await services.confidential.authenticate(reason: "Delete protected collection") else { return }
            verified = true
        }
        do {
            try CollectionStore(database: services.database).delete(
                id: target.id, option: option, ownerVerified: verified)
            services.reconcileReminders()
            reload()
        } catch {
            deleteError = error.localizedDescription
        }
    }

    private func confirmDelete(_ box: CardBoxRecord) {
        deleteTarget = box
        showingDelete = true
    }

    private func reload() {
        load = Result {
            let collections = try CollectionStore(database: services.database).collections()
            let sections = try services.database.cardList(isSessionUnlocked: services.confidential.isUnlocked).sections
            return Loaded(
                collections: collections,
                counts: Dictionary(sections.map { ($0.boxId, $0.cards.count) }, uniquingKeysWith: +))
        }
    }
}
