import PrayerKit
import SwiftUI

enum ExpandedCardIds {
    static func parse(_ raw: String) -> Set<Int64> {
        Set(raw.split(separator: ",").compactMap { Int64($0) })
    }

    static func format(_ ids: Set<Int64>) -> String {
        ids.sorted().map(String.init).joined(separator: ",")
    }
}

struct CardTile: View {
    let item: CardListItem
    let isHidden: Bool
    let isExpanded: Bool
    let reloads: Int
    let toggle: () -> Void
    let perform: (CardRowAction) -> Void
    let addPrayer: () -> Void
    let openProtected: () -> Void

    @Environment(AppServices.self) private var services
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @State private var model: CardDetailModel?
    @State private var share = ShareItemBuilder.RowShare()

    private struct LoadKey: Equatable {
        let item: CardListItem
        let isExpanded: Bool
        let reloads: Int
    }

    private var archivedFolderId: Int64 { Int64(services.preferences.archivedFolderId) }

    private var actionModel: CardRowActionModel {
        CardRowActionModel(item: item, archivedFolderId: archivedFolderId)
    }

    private var showsBody: Bool { isExpanded && !item.isMasked }

    var body: some View {
        Section {
            header
            if showsBody {
                bar
                if let model {
                    if model.errorMessage != nil {
                        Label("Can't Load Prayers", systemImage: "exclamationmark.triangle")
                    } else {
                        ForEach(model.activePrayers, id: \.id) { prayer in
                            NavigationLink(value: CardsRoute.prayer(id: prayer.id)) { PrayerRow(prayer: prayer) }
                        }
                        if !model.answeredPrayers.isEmpty {
                            AnsweredPrayersDisclosure(prayers: model.answeredPrayers) { prayer in
                                NavigationLink(value: CardsRoute.prayer(id: prayer.id)) { PrayerRow(prayer: prayer) }
                            }
                        }
                    }
                }
                Button("Add Prayer", systemImage: "plus", action: addPrayer)
            }
        }
        .task(id: LoadKey(item: item, isExpanded: isExpanded, reloads: reloads)) { loadBody() }
    }

    private var header: some View {
        HStack {
            Button(action: toggle) {
                Image(systemName: isExpanded ? "chevron.down" : "chevron.right")
                    .frame(width: 44, height: 44)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .accessibilityLabel(
                isExpanded ? Text("Collapse \(item.card.title)") : Text("Expand \(item.card.title)"))
            NavigationLink(value: CardsRoute.card(id: item.card.id)) {
                CardRow(item: item, isHidden: isHidden)
            }
        }
        .modifier(CardRowActions(item: item, archivedFolderId: archivedFolderId, perform: perform))
    }

    private func loadBody() {
        guard showsBody else {
            model = nil
            share = ShareItemBuilder.RowShare()
            return
        }
        let loaded = CardDetailModel(
            cardId: item.card.id, database: services.database, preferences: services.preferences,
            isSessionUnlocked: services.confidential.isUnlocked)
        loaded.load()
        model = loaded
        share = .card(item.card, database: services.database)
    }

    private enum BarItem: Hashable {
        case action(CardRowAction)
        case share
    }

    private var barItems: [BarItem] {
        actionModel.barActions.flatMap { action -> [BarItem] in
            action == .pray && share.isShown ? [.action(action), .share] : [.action(action)]
        }
    }

    @ViewBuilder
    private var bar: some View {
        let items = barItems
        Group {
            if dynamicTypeSize.isAccessibilitySize {
                grid(items)
            } else {
                ViewThatFits(in: .horizontal) {
                    HStack { ForEach(items, id: \.self) { barButton($0) } }
                    grid(items)
                }
            }
        }
        .labelStyle(VerticalLabelStyle())
    }

    private func grid(_ items: [BarItem]) -> some View {
        Grid {
            ForEach(Array(stride(from: 0, to: items.count, by: 2)), id: \.self) { start in
                GridRow {
                    ForEach(items[start..<min(start + 2, items.count)], id: \.self) { barButton($0) }
                }
            }
        }
    }

    @ViewBuilder
    private func barButton(_ item: BarItem) -> some View {
        switch item {
        case .action(let action):
            Button(action.title, systemImage: action.systemImage) { perform(action) }
                .disabled(action == .pray && !actionModel.canPray)
                .buttonStyle(.borderless)
                .lineLimit(1)
                .fixedSize(horizontal: true, vertical: false)
                .frame(maxWidth: .infinity, minHeight: 44)
        case .share:
            ShareMenuItem(item: share.item)
                .buttonStyle(.borderless)
                .lineLimit(1)
                .fixedSize(horizontal: true, vertical: false)
                .frame(maxWidth: .infinity, minHeight: 44)
        }
    }
}

private struct VerticalLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        VStack(spacing: 4) {
            configuration.icon
            configuration.title.font(.caption)
        }
    }
}
