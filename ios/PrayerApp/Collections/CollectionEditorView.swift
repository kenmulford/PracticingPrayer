import PrayerKit
import SwiftUI

struct CollectionEditorView: View {
    @State private var model: CollectionEditorModel
    @Environment(\.dismiss) private var dismiss
    @Environment(AppServices.self) private var services
    @State private var showingDiscard = false
    @State private var showingDelete = false
    @State private var failureTitle: LocalizedStringKey = "Couldn't Save"

    private let name: String
    private let onFinish: () -> Void

    init(database: PrayerDatabase, collection: CardBoxRecord?, onFinish: @escaping () -> Void) {
        self.model = CollectionEditorModel(database: database, collection: collection)
        self.name = collection?.name ?? ""
        self.onFinish = onFinish
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $model.name)
                        .accessibilityIdentifier("CollectionEditor_Name")
                }
                Section {
                    Toggle("Protect All Cards", isOn: $model.protectAllCards)
                    if model.protectAllCards {
                        Picker("Protection", selection: $model.protectionMode) {
                            Text("Locked").tag(CardProtectionMode.lockedVisible)
                            Text("Hidden").tag(CardProtectionMode.hidden)
                        }
                        .pickerStyle(.segmented)
                    }
                } footer: {
                    Text("Cards with no protection of their own take the collection's setting.")
                }
                if !model.isNew {
                    Section {
                        Button("Delete Collection", role: .destructive) { showingDelete = true }
                    }
                }
            }
            .navigationTitle(model.isNew ? "New Collection" : "Edit Collection")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") {
                        if model.isDirty { showingDiscard = true } else { finish() }
                    }
                    .accessibilityIdentifier("CollectionEditor_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", role: .confirm) {
                        failureTitle = "Couldn't Save"
                        Task { await save() }
                    }
                    .disabled(!model.canSave)
                    .accessibilityIdentifier("CollectionEditor_Confirm")
                }
            }
        }
        .interactiveDismissDisabled(model.isDirty)
        .confirmationDialog("Discard Changes", isPresented: $showingDiscard, titleVisibility: .hidden) {
            Button("Discard Changes", role: .destructive) { finish() }
            Button("Keep Editing", role: .cancel) {}
        }
        .deleteCollectionDialog(isPresented: $showingDelete, name: name) { option in
            Task { await delete(option) }
        }
        .alert(alertTitle, isPresented: alertBinding) {
            Button("OK") {}
        } message: {
            Text(alertMessage)
        }
    }

    private func save() async {
        var verified = false
        if model.requiresOwnerVerification {
            guard await services.confidential.verifyOwner(reason: "Change collection protection") else { return }
            verified = true
        }
        if model.save(ownerVerified: verified) { finish() }
    }

    private func delete(_ option: CollectionDeleteOption) async {
        failureTitle = "Couldn't Delete"
        var verified = false
        if model.requiresOwnerVerificationToDelete {
            guard await services.confidential.verifyOwner(reason: "Delete protected collection") else { return }
            verified = true
        }
        if model.delete(option: option, ownerVerified: verified) {
            services.reconcileReminders()
            finish()
        }
    }

    private func finish() {
        dismiss()
        onFinish()
    }

    private var alertTitle: LocalizedStringKey {
        if case .duplicateName = model.alert { return "Duplicate Collection Name" }
        return failureTitle
    }

    private var alertMessage: String {
        switch model.alert {
        case .duplicateName(let name):
            String(localized: "A collection named '\(name)' already exists. Please choose a different name.")
        case .failure(let message): message
        case nil: ""
        }
    }

    private var alertBinding: Binding<Bool> {
        Binding(
            get: { model.alert != nil },
            set: { if !$0 { model.alert = nil } })
    }
}

extension View {
    func deleteCollectionDialog(
        isPresented: Binding<Bool>, name: String, perform: @escaping (CollectionDeleteOption) -> Void
    ) -> some View {
        confirmationDialog(
            "Delete “\(name)”?", isPresented: isPresented, titleVisibility: .visible
        ) {
            Button("Unassign Cards") { perform(.unassignCards) }
            Button("Delete All Cards & Requests", role: .destructive) { perform(.deleteAll) }
            Button("Cancel", role: .cancel) {}
        }
    }
}
