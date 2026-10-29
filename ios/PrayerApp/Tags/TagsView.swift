import PrayerKit
import SwiftUI

struct TagsView: View {
    @Binding var path: [TagsRoute]
    @Environment(AppServices.self) private var services

    @State private var model: TagsModel?
    @State private var editingTagID: Int64?
    @State private var isCreatingTag = false

    var body: some View {
        content
            .accessibilityIdentifier("Shell_Root_tags")
            .navigationTitle(Text(AppTab.tags.title))
            .navigationBarTitleDisplayMode(.large)
            .toolbar { TagsToolbar(newTag: { isCreatingTag = true }) }
            .navigationDestination(for: TagsRoute.self) { route in
                switch route {
                case .tagPrayers(let id): TagPrayersView(tagId: id, path: $path)
                }
            }
            .task {
                if model == nil { model = TagsModel(database: services.database) }
                model?.reload()
            }
            .onAppear { model?.reload() }
            .sheet(isPresented: $isCreatingTag, onDismiss: { model?.reload() }) {
                NavigationStack { Color.clear.navigationTitle("New Tag") }
            }
            .sheet(isPresented: editingBinding, onDismiss: { model?.reload() }) {
                NavigationStack { Color.clear.navigationTitle("Edit Tag") }
            }
            .confirmationDialog(
                Text("Delete “\(model?.pendingDelete?.name ?? "")”?"), isPresented: deleteBinding,
                titleVisibility: .visible, presenting: model?.pendingDelete
            ) { _ in
                Button("Delete Tag", role: .destructive) { model?.confirmDelete() }
                Button("Cancel", role: .cancel) {}
            } message: { _ in
                Text("It will be removed from all prayer requests.")
            }
            .alert("Couldn't Delete Tag", isPresented: errorBinding) {
                Button("OK") {}
            } message: {
                Text(model?.errorMessage ?? "")
            }
    }

    @ViewBuilder
    private var content: some View {
        switch model?.load {
        case .none:
            ProgressView()
        case .failure:
            ContentUnavailableView {
                Label("Can't Load Tags", systemImage: "exclamationmark.triangle")
            } actions: {
                Button("Try Again") { model?.reload() }
                    .buttonStyle(.borderedProminent)
                    .prominentLabel()
            }
        case .success(let summaries) where summaries.isEmpty:
            ContentUnavailableView {
                Label("No Tags", systemImage: "tag")
            } actions: {
                Button("New Tag") { isCreatingTag = true }
                    .buttonStyle(.borderedProminent)
                    .prominentLabel()
            }
        case .success(let summaries):
            List {
                ForEach(summaries, id: \.tag.id) { summary in
                    NavigationLink(value: TagsRoute.tagPrayers(id: summary.tag.id)) {
                        TagRow(summary: summary)
                    }
                    .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                        if TagsModel.canDelete(summary.tag) {
                            deleteButton(summary.tag)
                        }
                        editButton(summary.tag)
                    }
                    .contextMenu {
                        editButton(summary.tag)
                        if TagsModel.canDelete(summary.tag) {
                            deleteButton(summary.tag)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
        }
    }

    private func deleteButton(_ tag: PrayerTagRecord) -> some View {
        Button("Delete", systemImage: "trash", role: .destructive) { model?.pendingDelete = tag }
    }

    private func editButton(_ tag: PrayerTagRecord) -> some View {
        Button("Edit", systemImage: "pencil") { editingTagID = tag.id }
    }

    private var deleteBinding: Binding<Bool> {
        Binding(get: { model?.pendingDelete != nil }, set: { if !$0 { model?.pendingDelete = nil } })
    }

    private var editingBinding: Binding<Bool> {
        Binding(get: { editingTagID != nil }, set: { if !$0 { editingTagID = nil } })
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { model?.errorMessage != nil }, set: { if !$0 { model?.errorMessage = nil } })
    }
}
