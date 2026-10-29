import PrayerKit
import SwiftUI

nonisolated struct PrayerTimeOption: Identifiable, Equatable, Sendable {
    let id: Int64
    let name: String
    let color: String?
    let activeCount: Int
}

/// CardSection has no public initializer, so filtered sections are rebuilt in this type.
nonisolated struct PrayerTimeSection: Equatable, Sendable {
    let boxId: Int64
    let box: CardBoxRecord?
    let cards: [CardListItem]
}

nonisolated struct PrayerTimeLauncher: Sendable {
    private let database: PrayerDatabase

    init(database: PrayerDatabase) {
        self.database = database
    }

    func items(for scope: PrayerTimeScope) throws -> [PrayerListItem] {
        let store = PrayerStore(database: database)
        switch scope {
        case .all:
            return try store.prayers(segment: .active, isSessionUnlocked: false)
        case .tags(let ids):
            return try store.prayers(segment: .active, tagIds: ids, isSessionUnlocked: false)
        case .cards(let ids):
            let cards = try eligibleCards(search: "").flatMap(\.cards).filter { ids.contains($0.card.id) }
            return try activePrayers(of: cards, store: store)
        case .collection(let id):
            let cards = try eligibleCards(search: "").filter { $0.boxId == id }.flatMap(\.cards)
            return try activePrayers(of: cards, store: store)
        }
    }

    func eligibleCards(search: String) throws -> [PrayerTimeSection] {
        let list = try database.cardList(filter: CardListFilter(searchText: search), isSessionUnlocked: false)
        return list.sections.compactMap { section in
            guard section.box?.systemKey != "archived" else { return nil }
            let cards = section.cards.filter {
                !$0.isMasked && $0.card.isSystem != true && ($0.activePrayerCount ?? 0) > 0
            }
            return cards.isEmpty ? nil : PrayerTimeSection(boxId: section.boxId, box: section.box, cards: cards)
        }
    }

    func tagOptions() throws -> [PrayerTimeOption] {
        let active = try items(for: .all)
        return try TagStore(database: database).tags().compactMap { summary in
            let count = active.filter { $0.tagIds.contains(summary.tag.id) }.count
            guard count > 0 else { return nil }
            return PrayerTimeOption(id: summary.tag.id, name: summary.tag.name, color: summary.tag.color, activeCount: count)
        }
    }

    func collectionOptions() throws -> [PrayerTimeOption] {
        try eligibleCards(search: "").compactMap { section in
            guard let box = section.box, !box.isSystem else { return nil }
            let count = section.cards.reduce(0) { $0 + ($1.activePrayerCount ?? 0) }
            return PrayerTimeOption(id: box.id, name: box.name, color: nil, activeCount: count)
        }
    }

    private func activePrayers(of cards: [CardListItem], store: PrayerStore) throws -> [PrayerListItem] {
        try cards.flatMap { try store.prayers(cardId: $0.card.id).filter { !$0.record.isAnswered } }
    }
}

private struct PrayerTimeLauncherModifier: ViewModifier {
    private enum Sheet: Identifiable {
        case cards, tags, collection
        var id: Self { self }
    }

    @Binding var isPresented: Bool
    @Environment(AppServices.self) private var services
    @State private var hasTags = false
    @State private var hasCollections = false
    @State private var sheet: Sheet?
    @State private var pendingScope: PrayerTimeScope?

    func body(content: Content) -> some View {
        content
            .confirmationDialog("Prayer Time", isPresented: $isPresented, titleVisibility: .visible) {
                Button("All Requests") { start(.all) }
                Button("Choose Cards…") { sheet = .cards }
                if hasTags {
                    Button("By Tags…") { sheet = .tags }
                }
                if hasCollections {
                    Button("By Collection…") { sheet = .collection }
                }
            }
            .sheet(item: $sheet, onDismiss: startPending) { sheet in
                switch sheet {
                case .cards: ChooseCardsSheet(launcher: launcher, onStart: choose)
                case .tags: ChooseTagsSheet(launcher: launcher, onStart: choose)
                case .collection: ChooseCollectionSheet(launcher: launcher, onStart: choose)
                }
            }
            .onAppear(perform: refresh)
    }

    private var launcher: PrayerTimeLauncher {
        PrayerTimeLauncher(database: services.database)
    }

    private func choose(_ scope: PrayerTimeScope) {
        pendingScope = scope
        sheet = nil
    }

    private func startPending() {
        refresh()
        guard let scope = pendingScope else { return }
        pendingScope = nil
        start(scope)
    }

    private func start(_ scope: PrayerTimeScope) {
        guard let items = try? launcher.items(for: scope) else { return }
        services.prayerSession = PrayerSession(items: items, database: services.database)
    }

    private func refresh() {
        let launcher = launcher
        Task {
            let result = await Task.detached {
                (try? launcher.tagOptions().isEmpty == false, try? launcher.collectionOptions().isEmpty == false)
            }.value
            hasTags = result.0 ?? false
            hasCollections = result.1 ?? false
        }
    }
}

extension View {
    func prayerTimeLauncher(isPresented: Binding<Bool>) -> some View {
        modifier(PrayerTimeLauncherModifier(isPresented: isPresented))
    }
}
