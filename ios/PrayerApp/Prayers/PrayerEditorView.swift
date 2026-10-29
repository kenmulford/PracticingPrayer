import PrayerKit
import SwiftUI

enum PrayerEditorTarget: Identifiable {
    case new
    case edit(Int64)

    var id: Int64? {
        if case .edit(let id) = self { id } else { nil }
    }

    var prayerId: Int64? { id }
}

struct PrayerEditorView: View {
    @State private var model: PrayerEditorModel
    @Environment(\.dismiss) private var dismiss
    @FocusState private var titleFocused: Bool
    @State private var showingDiscard = false
    @State private var showingDelete = false

    private let database: PrayerDatabase
    private let onFinish: (PrayerEditorModel.Outcome) -> Void

    init(
        prayerId: Int64?, cardId: Int64? = nil, cardTitle: String? = nil, database: PrayerDatabase,
        preferences: AppPreferences = AppServices.defaultPreferences,
        reminders: (any ReminderReconciling)? = nil,
        onFinish: @escaping (PrayerEditorModel.Outcome) -> Void = { _ in }
    ) {
        let model = PrayerEditorModel(
            prayerId: prayerId, cardId: cardId, cardTitle: cardTitle, database: database,
            preferences: preferences,
            reminders: reminders
                ?? ReminderScheduler(database: database, preferences: preferences, center: SystemReminderCenter()))
        model.load()
        self.model = model
        self.database = database
        self.onFinish = onFinish
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    NavigationLink {
                        CardPickerView(database: database, selectedCardId: model.cardId) { id, title in
                            model.selectCard(id: id, title: title)
                        }
                    } label: {
                        LabeledContent("Card") {
                            if model.cardTitle.isEmpty {
                                Text("Select Card").foregroundStyle(.secondary)
                            } else {
                                Text(verbatim: model.cardTitle)
                            }
                        }
                    }
                    .accessibilityIdentifier("PrayerEditor_Card")
                    TextField("Title", text: $model.title)
                        .focused($titleFocused)
                        .accessibilityIdentifier("PrayerEditor_Title")
                    TextField("Details", text: $model.details, axis: .vertical)
                        .lineLimit(3...8)
                        .accessibilityIdentifier("PrayerEditor_Details")
                }
                ReminderSection(model: model)
                Section {
                    Toggle("Answered", isOn: $model.isAnswered)
                        .accessibilityIdentifier("PrayerEditor_Answered")
                    if model.isAnswered {
                        DatePicker(
                            "Answered On", selection: $model.answeredOn, in: ...Date.now,
                            displayedComponents: .date
                        )
                        .accessibilityIdentifier("PrayerEditor_AnsweredOn")
                    }
                } header: {
                    Text("Status").accessibilityAddTraits(.isHeader)
                }
                Section {
                    NavigationLink {
                        TagPickerView(selection: $model.tagIds)
                    } label: {
                        tagsLabel
                    }
                    .accessibilityIdentifier("PrayerEditor_Tags")
                } header: {
                    Text("Tags").accessibilityAddTraits(.isHeader)
                }
                if model.isNew {
                    Section {
                        Button("Save and Add Another") {
                            Task { if await model.saveAndAddAnother() { titleFocused = true } }
                        }
                        .disabled(!model.canSave)
                        .accessibilityIdentifier("PrayerEditor_SaveAndAdd")
                    }
                } else {
                    Section {
                        Button("Delete Prayer", role: .destructive) { showingDelete = true }
                    }
                }
            }
            .navigationTitle(model.isNew ? "New Prayer" : "Edit Prayer")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark", role: .cancel) {
                        if model.isDirty { showingDiscard = true } else { finish(.saved) }
                    }
                    .accessibilityIdentifier("PrayerEditor_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", systemImage: "checkmark", role: .confirm) {
                        Task { if await model.save() { finish(.saved) } }
                    }
                    .disabled(!model.canSave)
                    .accessibilityIdentifier("PrayerEditor_Confirm")
                }
            }
        }
        .interactiveDismissDisabled(model.isDirty)
        .confirmationDialog("Discard Changes", isPresented: $showingDiscard, titleVisibility: .hidden) {
            Button("Discard Changes", role: .destructive) { finish(.saved) }
            Button("Keep Editing", role: .cancel) {}
        }
        .deletePrayerDialog(isPresented: $showingDelete, title: model.title) { run(.deleted) { model.delete() } }
        .alert("Couldn't Save", isPresented: alertBinding) {
            Button("OK") {}
        } message: {
            Text(model.alert ?? "")
        }
        .onAppear { if model.isNew { titleFocused = true } }
        .task { await model.refreshAuthorization() }
        .onChange(of: model.tagIds) { model.loadTags() }
    }

    @ViewBuilder
    private var tagsLabel: some View {
        if model.selectedTags.isEmpty {
            Text("Tags").foregroundStyle(.secondary)
        } else {
            TagFlowLayout {
                ForEach(model.selectedTags, id: \.id) { tag in
                    Text(verbatim: tag.name)
                        .font(.subheadline)
                        .foregroundStyle(
                            PrayerDetailModel.prefersDarkText(onTagHex: tag.color) ? Color.black : Color.white
                        )
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(Color.tag(tag.color), in: Capsule())
                }
            }
            .accessibilityElement(children: .combine)
        }
    }

    private func run(_ outcome: PrayerEditorModel.Outcome, _ work: () -> Bool) {
        if work() { finish(outcome) }
    }

    private func finish(_ outcome: PrayerEditorModel.Outcome) {
        dismiss()
        onFinish(outcome)
    }

    private var alertBinding: Binding<Bool> {
        Binding(
            get: { model.alert != nil },
            set: { if !$0 { model.alert = nil } })
    }
}
