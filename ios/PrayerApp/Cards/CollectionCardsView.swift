import PrayerKit
import SwiftUI

struct CollectionCardsView: View {
    let boxId: Int64
    @Binding var selection: CardsRoute?
    let revision: Int
    let onChange: () -> Void

    @Environment(AppServices.self) private var services
    @State private var load: Result<CardList, any Error>?

    init(boxId: Int64, selection: Binding<CardsRoute?>, revision: Int, onChange: @escaping () -> Void) {
        self.boxId = boxId
        _selection = selection
        self.revision = revision
        self.onChange = onChange
    }

    private var isSessionUnlocked: Bool { services.confidential.isUnlocked }

    private var section: CardSection? {
        guard case .success(let list) = load else { return nil }
        return list.sections.first { $0.boxId == boxId }
    }

    var body: some View {
        content
            .navigationTitle(section?.box?.name ?? String(localized: "Loose Cards"))
            .navigationBarTitleDisplayMode(.large)
            .task(id: boxId) { reload() }
            .onChange(of: isSessionUnlocked) { reload() }
            .onChange(of: selection) { reload() }
            .onChange(of: revision) { reload() }
    }

    @ViewBuilder
    private var content: some View {
        switch load {
        case .none:
            Color.clear
        case .failure:
            ContentUnavailableView {
                Label("Can't Load Prayer Cards", systemImage: "exclamationmark.triangle")
            } actions: {
                Button("Try Again") { reload() }
                    .buttonStyle(.borderedProminent)
                    .prominentLabel()
            }
        case .success:
            if let section, !section.cards.isEmpty {
                CardRowActionsHost(onChange: onChange, onRemoved: deselect) { perform in
                    cardList(section, perform: perform)
                }
            } else {
                ContentUnavailableView("No Cards", systemImage: "rectangle.stack")
            }
        }
    }

    private func cardList(
        _ section: CardSection, perform: @escaping (CardRowAction, CardListItem) -> Void
    ) -> some View {
        List(selection: $selection) {
            Section {
                ForEach(section.cards, id: \.card.id) { item in
                    if item.isMasked {
                        Button {
                            Task { await openProtected(item) }
                        } label: {
                            CardRow(item: item)
                        }
                        .buttonStyle(.plain)
                        .selectionDisabled()
                    } else {
                        NavigationLink(value: CardsRoute.card(id: item.card.id)) {
                            CardRow(item: item, isHidden: isHidden(item, in: section))
                        }
                        .modifier(
                            CardRowActions(
                                item: item, archivedFolderId: Int64(services.preferences.archivedFolderId)
                            ) { perform($0, item) })
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    private func isHidden(_ item: CardListItem, in section: CardSection) -> Bool {
        ProtectionPolicy.effectiveMode(of: item.card, box: section.box) == .hidden
    }

    private func openProtected(_ item: CardListItem) async {
        if await services.confidential.authenticate(reason: "Open protected card") {
            selection = .card(id: item.card.id)
        }
    }

    // A removed or archived card must not stay open in the detail pane.
    private func deselect(_ cardId: Int64) {
        if selection == .card(id: cardId) { selection = nil }
    }

    private func reload() {
        load = Result { try services.database.cardList(isSessionUnlocked: isSessionUnlocked) }
    }
}
