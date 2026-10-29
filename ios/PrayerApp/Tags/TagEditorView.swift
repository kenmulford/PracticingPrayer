import PrayerKit
import SwiftUI

struct TagEditorView: View {
    @State private var model: TagEditorModel
    @Environment(\.dismiss) private var dismiss
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @State private var showingDiscard = false
    @State private var showingDelete = false
    @State private var pickedColor = Color.accentColor
    @State private var pickedHex: String?
    @State private var failureTitle: LocalizedStringKey = "Couldn't Save"

    private let name: String

    init(database: PrayerDatabase, tag: PrayerTagRecord?) {
        self.model = TagEditorModel(database: database, tag: tag)
        self.name = tag?.name ?? ""
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $model.name)
                        .disabled(!model.isNameEditable)
                        .accessibilityIdentifier("TagEditor_Name")
                }
                Section {
                    LazyVGrid(columns: columns, alignment: .leading, spacing: 12) {
                        ForEach(model.swatches) { swatch in
                            swatchButton(swatch)
                        }
                        ColorPicker("Custom", selection: $pickedColor, supportsOpacity: false)
                            .labelsHidden()
                            .accessibilityIdentifier("TagEditor_Custom")
                    }
                } header: {
                    Text("Color").accessibilityAddTraits(.isHeader)
                }
                if model.canDelete {
                    Section {
                        Button("Delete Tag", role: .destructive) { showingDelete = true }
                            .accessibilityIdentifier("TagEditor_DeleteTag")
                    }
                }
            }
            .navigationTitle(model.isNew ? "New Tag" : "Edit Tag")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") {
                        if model.isDirty { showingDiscard = true } else { dismiss() }
                    }
                    .accessibilityIdentifier("TagEditor_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", role: .confirm) {
                        failureTitle = "Couldn't Save"
                        if model.save() { dismiss() }
                    }
                    .disabled(!model.canSave)
                    .accessibilityIdentifier("TagEditor_Confirm")
                }
            }
        }
        .interactiveDismissDisabled(model.isDirty)
        .task(id: pickedHex) {
            guard let pickedHex else { return }
            try? await Task.sleep(for: .milliseconds(600))
            guard !Task.isCancelled else { return }
            model.addCustomColor(hex: pickedHex)
        }
        .onChange(of: pickedColor) { _, color in pickedHex = color.hexString }
        .confirmationDialog("Discard Changes", isPresented: $showingDiscard, titleVisibility: .hidden) {
            Button("Discard Changes", role: .destructive) { dismiss() }
            Button("Keep Editing", role: .cancel) {}
        }
        .confirmationDialog(
            "Delete Color", isPresented: colorDeleteBinding, titleVisibility: .visible,
            presenting: model.pendingColorDelete
        ) { swatch in
            if model.tagsUsing(swatch) > 0 {
                ForEach(model.swatches.filter { $0 != swatch }) { other in
                    Button(model.label(for: other)) { _ = model.deleteColor(swatch, replacementHex: other.hex) }
                }
            } else {
                Button("Delete Color", role: .destructive) {
                    _ = model.deleteColor(swatch, replacementHex: UserColorStore.defaultPaletteHexValues[0])
                }
            }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog(
            Text("Delete “\(name)”?"), isPresented: $showingDelete, titleVisibility: .visible
        ) {
            Button("Delete Tag", role: .destructive) {
                failureTitle = "Couldn't Delete"
                if model.delete() { dismiss() }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("It will be removed from all prayer requests.")
        }
        .alert(alertTitle, isPresented: alertBinding) {
            Button("OK") {}
        } message: {
            Text(alertMessage)
        }
    }

    private var columns: [GridItem] {
        dynamicTypeSize.isAccessibilitySize
            ? [GridItem(.flexible())] : [GridItem(.adaptive(minimum: 44), spacing: 12)]
    }

    private func swatchButton(_ swatch: TagEditorModel.Swatch) -> some View {
        let selected = swatch.hex.caseInsensitiveCompare(model.selectedHex ?? "") == .orderedSame
        return Button { model.selectedHex = swatch.hex } label: {
            Circle()
                .fill(Color.tag(swatch.hex))
                .frame(width: 44, height: 44)
                .overlay { if selected { Circle().strokeBorder(.primary, lineWidth: 3) } }
                .overlay {
                    if selected { Image(systemName: "checkmark").foregroundStyle(.white).fontWeight(.bold) }
                }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(model.label(for: swatch))
        .accessibilityAddTraits(selected ? .isSelected : [])
        .accessibilityIdentifier("TagEditor_Swatch_\(swatch.hex.replacingOccurrences(of: "#", with: ""))")
        .contextMenu {
            if !swatch.isDefault {
                Button("Delete Color", role: .destructive) { model.pendingColorDelete = swatch }
            }
        }
    }

    private var colorDeleteBinding: Binding<Bool> {
        Binding(
            get: { model.pendingColorDelete != nil },
            set: { if !$0 { model.pendingColorDelete = nil } })
    }

    private var alertTitle: LocalizedStringKey {
        if case .duplicateName = model.alert { return "Duplicate Tag Name" }
        return failureTitle
    }

    private var alertMessage: String {
        switch model.alert {
        case .duplicateName(let name):
            String(localized: "A tag named '\(name)' already exists. Please choose a different name.")
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
