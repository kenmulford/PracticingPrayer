import PrayerKit
import SwiftUI

struct PrayerDetailView<CardLink: Hashable>: View {
    let prayerId: Int64
    let cardRoute: (Int64) -> CardLink
    let onChange: () -> Void
    let onClose: () -> Void

    @Environment(AppServices.self) private var services
    @State private var model: PrayerDetailModel?
    @State private var editingPrayerID: Int64?
    @State private var confirmDelete = false

    var body: some View {
        Group {
            if let model {
                content(model)
            } else {
                ProgressView()
            }
        }
        .task(id: prayerId) {
            let loaded = PrayerDetailModel(prayerId: prayerId, database: services.database)
            loaded.load()
            model = loaded
        }
        .onAppear { model?.load() }
    }

    @ViewBuilder
    private func content(_ model: PrayerDetailModel) -> some View {
        switch model.phase {
        case .loading:
            ProgressView()
        case .notFound:
            ContentUnavailableView("Prayer Not Found", systemImage: "hands.and.sparkles")
        case .loaded:
            if let prayer = model.prayer {
                loadedList(model, prayer)
            }
        }
    }

    private func loadedList(_ model: PrayerDetailModel, _ prayer: PrayerRequestRecord) -> some View {
        List {
            Section {
                Text(verbatim: prayer.title).font(.title2.bold())
                if let details = prayer.details, !details.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text(verbatim: details).font(.body)
                }
            }
            Section {
                NavigationLink(value: cardRoute(prayer.prayerCardId)) {
                    LabeledContent("Card", value: model.cardTitle)
                }
                .accessibilityElement(children: .combine)
                LabeledContent("Started", value: date(prayer.createdAtTicks))
                    .accessibilityElement(children: .combine)
                if prayer.isAnswered, let answered = prayer.answeredAtTicks {
                    LabeledContent("Answered", value: date(answered))
                        .accessibilityElement(children: .combine)
                }
                if let summary = model.prayedSummary {
                    LabeledContent("Prayed", value: summary)
                        .monospacedDigit()
                        .accessibilityElement(children: .combine)
                }
                if prayer.isImported == true {
                    LabeledContent("Source", value: String(localized: "Imported"))
                        .accessibilityElement(children: .combine)
                }
            }
            if !model.tags.isEmpty {
                Section {
                    TagFlowLayout {
                        ForEach(model.tags, id: \.id) { tag in
                            Text(verbatim: tag.name)
                                .font(.subheadline)
                                .foregroundStyle(PrayerDetailModel.prefersDarkText(onTagHex: tag.color) ? Color.black : Color.white)
                                .padding(.horizontal, 10)
                                .padding(.vertical, 4)
                                .background(Color.tag(tag.color), in: Capsule())
                        }
                    }
                } header: {
                    Text("Tags").accessibilityAddTraits(.isHeader)
                }
            }
            Section {
                if prayer.isAnswered {
                    Button("Mark Active", systemImage: "arrow.uturn.backward.circle") {
                        perform { model.markActive() }
                    }
                } else {
                    Button("Mark Answered", systemImage: "checkmark.circle") {
                        perform { model.markAnswered() }
                    }
                }
                Button("Delete Prayer", role: .destructive) { confirmDelete = true }
            }
            .disabled(model.isMutating)
        }
        .listStyle(.insetGrouped)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button { editingPrayerID = prayer.id } label: {
                    Label("Edit", systemImage: "pencil")
                }
                .accessibilityLabel("Edit Prayer")
                .disabled(model.isMutating)
            }
        }
        .sheet(
            isPresented: Binding(
                get: { editingPrayerID != nil }, set: { if !$0 { editingPrayerID = nil } }),
            onDismiss: {
                model.load()
                onChange()
            }
        ) {
            PrayerEditorView(prayerId: prayer.id, database: services.database) { if $0 == .deleted { onClose() } }
        }
        .deletePrayerDialog(isPresented: $confirmDelete, title: prayer.title) {
            if model.delete() {
                onChange()
                onClose()
            }
        }
        .alert(
            "Couldn't Update Prayer",
            isPresented: Binding(
                get: { model.errorMessage != nil }, set: { if !$0 { model.errorMessage = nil } })
        ) {
            Button("OK") {}
        } message: {
            Text(model.errorMessage ?? "")
        }
    }

    private func perform(_ action: () -> Void) {
        action()
        if model?.errorMessage == nil { onChange() }
    }

    private func date(_ ticks: Int64) -> String {
        guard let value = try? Date(ticks: ticks) else { return "" }
        return value.formatted(date: .abbreviated, time: .omitted)
    }
}

/// Detail screens for stacks whose route enum holds no selection state: closing pops the pushed screen.
struct PushedPrayerDetail<CardLink: Hashable>: View {
    let prayerId: Int64
    let cardRoute: (Int64) -> CardLink
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        PrayerDetailView(prayerId: prayerId, cardRoute: cardRoute, onChange: {}, onClose: { dismiss() })
    }
}

struct PushedCardDetail: View {
    let cardId: Int64
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        CardDetailView(cardId: cardId, onChange: {}, onClose: { dismiss() })
    }
}

struct TagFlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        arrange(width: proposal.width ?? .infinity, subviews: subviews).size
    }

    func placeSubviews(
        in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()
    ) {
        let result = arrange(width: bounds.width, subviews: subviews)
        for (index, origin) in result.origins.enumerated() {
            subviews[index].place(
                at: CGPoint(x: bounds.minX + origin.x, y: bounds.minY + origin.y), proposal: .unspecified)
        }
    }

    private func arrange(width: CGFloat, subviews: Subviews) -> (size: CGSize, origins: [CGPoint]) {
        var origins: [CGPoint] = []
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        var maxX: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            origins.append(CGPoint(x: x, y: y))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
            maxX = max(maxX, x - spacing)
        }
        return (CGSize(width: maxX, height: y + rowHeight), origins)
    }
}
