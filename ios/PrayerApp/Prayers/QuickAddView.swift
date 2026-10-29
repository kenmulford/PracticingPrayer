import PrayerKit
import SwiftUI

struct QuickAddView: View {
    @State private var model: QuickAddModel
    @Environment(\.dismiss) private var dismiss
    @State private var showingDiscard = false

    private let database: PrayerDatabase

    init(database: PrayerDatabase) {
        self.database = database
        self.model = QuickAddModel(database: database)
    }

    var body: some View {
        NavigationStack {
            Form {
                targetSection
                ForEach($model.prayers) { $entry in
                    prayerSection($entry)
                }
                Section {
                    Button("Add Prayer", systemImage: "plus") { model.addPrayer() }
                        .accessibilityIdentifier("QuickAdd_AddPrayer")
                }
            }
            .navigationTitle("Quick Add")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") {
                        if model.isDirty { showingDiscard = true } else { dismiss() }
                    }
                    .accessibilityIdentifier("QuickAdd_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", role: .confirm) {
                        Task { if await model.save() { dismiss() } }
                    }
                    .disabled(!model.canSave)
                    .accessibilityIdentifier("QuickAdd_Confirm")
                }
            }
            .disabled(model.isSaving)
            .overlay {
                if model.isSaving { ProgressView() }
            }
        }
        .interactiveDismissDisabled(model.isDirty)
        .confirmationDialog("Discard Changes", isPresented: $showingDiscard, titleVisibility: .hidden) {
            Button("Discard Changes", role: .destructive) { dismiss() }
            Button("Keep Editing", role: .cancel) {}
        }
        .alert("Couldn't Save", isPresented: errorBinding) {
            Button("OK") {}
        } message: {
            Text(model.errorMessage ?? "")
        }
        .task { model.loadCollections() }
    }

    private var errorBinding: Binding<Bool> {
        Binding(
            get: { model.errorMessage != nil },
            set: { if !$0 { model.errorMessage = nil } })
    }

    private var targetSection: some View {
        Section("Add To") {
            Picker("Add To", selection: $model.addTo) {
                Text("New Card").tag(QuickAddModel.AddTo.newCard)
                Text("Existing Card").tag(QuickAddModel.AddTo.existingCard)
            }
            .pickerStyle(.segmented)
            switch model.addTo {
            case .newCard:
                TextField("Card Title", text: $model.cardTitle)
                Picker("Collection", selection: $model.collectionId) {
                    Text("Loose Cards").tag(Int64(0))
                    ForEach(model.collections, id: \.id) { box in
                        Text(box.name).tag(box.id)
                    }
                }
            case .existingCard:
                NavigationLink {
                    CardPickerView(
                        database: database, selectedCardId: model.selectedCardId,
                        onSelect: { model.selectCard(id: $0, title: $1) })
                } label: {
                    LabeledContent("Card") {
                        Text(model.selectedCardTitle ?? String(localized: "None"))
                    }
                }
                .accessibilityIdentifier("QuickAdd_CardRow")
            }
        }
    }

    private func prayerSection(_ entry: Binding<QuickAddModel.PrayerEntry>) -> some View {
        let number = (model.prayers.firstIndex { $0.id == entry.wrappedValue.id } ?? 0) + 1
        return Section {
            TextField("Title", text: entry.title)
                .swipeActions(edge: .trailing) {
                    if model.prayers.count > 1 {
                        Button("Delete", role: .destructive) {
                            model.removePrayer(id: entry.wrappedValue.id)
                        }
                    }
                }
            TextField("Details", text: entry.details, axis: .vertical)
        } header: {
            Text("Prayer \(number)")
                .accessibilityAddTraits(.isHeader)
        }
    }
}
