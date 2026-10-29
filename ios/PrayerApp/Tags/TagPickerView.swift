import PrayerKit
import SwiftUI

struct TagPickerView: View {
    @Binding private var selection: Set<Int64>
    @Environment(AppServices.self) private var services
    @State private var model = TagPickerModel()
    @State private var searchText = ""

    init(selection: Binding<Set<Int64>>) {
        _selection = selection
    }

    private var store: TagStore { TagStore(database: services.database) }

    var body: some View {
        Group {
            if model.loadState == .failed {
                ContentUnavailableView {
                    Label("Can't Load Tags", systemImage: "exclamationmark.triangle")
                } actions: {
                    Button("Try Again") { model.load(using: store) }
                        .buttonStyle(.borderedProminent)
                        .prominentLabel()
                }
            } else {
                list
            }
        }
        .navigationTitle("Tags")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if model.loadState == .idle { model.load(using: store) }
        }
    }

    private var list: some View {
        let visible = model.visibleTags(for: searchText)
        let candidate = model.createCandidate(for: searchText)
        return List {
            if let candidate {
                Button {
                    Task { await create(candidate) }
                } label: {
                    Label("Create “\(candidate)”", systemImage: "plus.circle")
                }
                .accessibilityLabel("Create \(candidate)")
                .disabled(model.isCreating)
                if model.createFailed {
                    Text("Couldn't create the tag.")
                        .foregroundStyle(.red)
                        .accessibilityIdentifier("TagPicker_CreateError")
                }
            }
            ForEach(visible, id: \.id) { tag in
                row(for: tag)
            }
        }
        .overlay {
            if visible.isEmpty {
                if searchText.isEmpty {
                    ContentUnavailableView(
                        "No Tags", systemImage: "tag", description: Text("Type a name to create a tag."))
                } else if candidate == nil {
                    ContentUnavailableView.search(text: searchText)
                }
            }
        }
        .searchable(text: $searchText, prompt: "Search or Create Tags")
        .onChange(of: searchText) { model.clearCreateError() }
    }

    private func row(for tag: PrayerTagRecord) -> some View {
        let isSelected = selection.contains(tag.id)
        let defaultHex = UserColorStore.defaultPaletteHexValues[0]
        let color = Color(hex: tag.color ?? defaultHex) ?? Color(hex: defaultHex) ?? .accentColor
        return Button {
            if isSelected { selection.remove(tag.id) } else { selection.insert(tag.id) }
        } label: {
            HStack {
                Image(systemName: "circle.fill")
                    .foregroundStyle(color)
                    .accessibilityHidden(true)
                Text(verbatim: tag.name)
                    .foregroundStyle(.primary)
                Spacer()
                Image(systemName: "checkmark")
                    .opacity(isSelected ? 1 : 0)
                    .accessibilityHidden(true)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private func create(_ name: String) async {
        guard let tag = await model.create(name: name, using: store) else { return }
        selection.insert(tag.id)
        searchText = ""
        AccessibilityNotification.Announcement(String(localized: "Created tag \(tag.name)")).post()
    }
}
