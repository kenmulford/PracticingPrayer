import PrayerKit
import SwiftUI

struct CardEditorView: View {
    @State private var model: CardEditorModel
    @Environment(\.dismiss) private var dismiss
    @Environment(AppServices.self) private var services
    @FocusState private var titleFocused: Bool
    @State private var showingDiscard = false
    @State private var showingDelete = false

    private let cardTitle: String
    private let onFinish: (CardEditorModel.Outcome) -> Void

    init(
        card: PrayerCardRecord?, database: PrayerDatabase, preferences: AppPreferences,
        onFinish: @escaping (CardEditorModel.Outcome) -> Void
    ) {
        let model = CardEditorModel(card: card, database: database, preferences: preferences)
        model.load()
        self.model = model
        self.cardTitle = card?.title ?? ""
        self.onFinish = onFinish
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Title", text: $model.title)
                        .focused($titleFocused)
                        .accessibilityIdentifier("CardEditor_Title")
                    Picker("Collection", selection: $model.boxId) {
                        if let stored = model.storedSystemBox {
                            Text(verbatim: stored.name).tag(stored.id).disabled(true)
                        }
                        Text("Loose Cards").tag(Int64(0))
                        ForEach(model.collections, id: \.id) { collection in
                            Text(verbatim: collection.name).tag(collection.id)
                        }
                    }
                    .pickerStyle(.menu)
                }
                Section {
                    Picker("Protection", selection: $model.protectionMode) {
                        Text("Off").tag(CardProtectionMode.unprotected)
                        Text("Locked").tag(CardProtectionMode.lockedVisible)
                        Text("Hidden").tag(CardProtectionMode.hidden)
                    }
                    .pickerStyle(.segmented)
                    .accessibilityHint(protectionFooter)
                } footer: {
                    Text(protectionFooter)
                }
                if model.isNew {
                    Section {
                        Button("Save and Add Another") {
                            if model.saveAndAddAnother() { titleFocused = true }
                        }
                        .disabled(!model.canSave)
                        .accessibilityIdentifier("CardEditor_SaveAndAdd")
                    }
                } else {
                    Section {
                        if model.isArchived {
                            Button("Unarchive Card") { run(.unarchived) { model.unarchive() } }
                        } else {
                            Button("Archive Card") { run(.archived) { model.archive() } }
                        }
                        Button("Delete Card", role: .destructive) { showingDelete = true }
                    }
                }
            }
            .navigationTitle(model.isNew ? "New Card" : "Edit Card")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark", role: .cancel) {
                        if model.isDirty { showingDiscard = true } else { finish(.saved) }
                    }
                    .accessibilityIdentifier("CardEditor_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", systemImage: "checkmark", role: .confirm) {
                        Task { await save() }
                    }
                    .disabled(!model.canSave)
                    .accessibilityIdentifier("CardEditor_Confirm")
                }
            }
        }
        .interactiveDismissDisabled(model.isDirty)
        .confirmationDialog("Discard Changes", isPresented: $showingDiscard, titleVisibility: .hidden) {
            Button("Discard Changes", role: .destructive) { finish(.saved) }
            Button("Keep Editing", role: .cancel) {}
        }
        .confirmationDialog(
            Text("Delete “\(cardTitle)”?"), isPresented: $showingDelete, titleVisibility: .visible
        ) {
            Button("Delete Card", role: .destructive) {
                if model.delete() {
                    services.reconcileReminders()
                    finish(.deleted)
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            switch model.prayerCount {
            case 0: Text("This card has no prayer requests.")
            case 1: Text("This also deletes its 1 prayer request.")
            default: Text("This also deletes its \(model.prayerCount) prayer requests.")
            }
        }
        .alert("Couldn't Save", isPresented: alertBinding) {
            Button("OK") {}
        } message: {
            Text(model.alert ?? "")
        }
        .onAppear { if model.isNew { titleFocused = true } }
    }

    private var protectionFooter: LocalizedStringKey {
        switch model.protectionMode {
        case .unprotected: "Anyone using this iPhone can see this card."
        case .lockedVisible: "The card shows as “Protected” until you unlock with Face ID or your passcode."
        case .hidden: "The card is hidden from every list until you choose Show Confidential."
        }
    }

    private func save() async {
        var verified = false
        if model.requiresOwnerVerification {
            guard await services.confidential.verifyOwner(reason: "Change card protection") else { return }
            verified = true
        }
        run(.saved) { model.save(ownerVerified: verified) }
    }

    private func run(_ outcome: CardEditorModel.Outcome, _ work: () -> Bool) {
        if work() { finish(outcome) }
    }

    private func finish(_ outcome: CardEditorModel.Outcome) {
        dismiss()
        onFinish(outcome)
    }

    private var alertBinding: Binding<Bool> {
        Binding(
            get: { model.alert != nil },
            set: { if !$0 { model.alert = nil } })
    }
}
