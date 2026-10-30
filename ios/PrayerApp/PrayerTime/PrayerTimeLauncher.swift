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

    func items(for scope: PrayerTimeScope, isSessionUnlocked: Bool = false) throws -> [PrayerListItem] {
        let store = PrayerStore(database: database)
        switch scope {
        case .all:
            let active = try store.prayers(segment: .active, isSessionUnlocked: isSessionUnlocked)
            let archived = try archivedCardIds(isSessionUnlocked: isSessionUnlocked)
            return active.filter { !archived.contains($0.record.prayerCardId) }
        case .tags(let ids):
            return try store.prayers(segment: .active, tagIds: ids, isSessionUnlocked: isSessionUnlocked)
        case .cards(let ids):
            let list = try database.cardList(filter: CardListFilter(searchText: ""), isSessionUnlocked: isSessionUnlocked)
            let cards = list.sections.flatMap(\.cards).filter { ids.contains($0.card.id) && !$0.isMasked }
            return try activePrayers(of: cards, store: store)
        case .prayers(let ids):
            let active = Dictionary(
                try store.prayers(segment: .active, isSessionUnlocked: isSessionUnlocked).map { ($0.record.id, $0) },
                uniquingKeysWith: { first, _ in first })
            return ids.compactMap { active[$0] }
        case .collection(let id):
            let cards = try eligibleCards(search: "", isSessionUnlocked: isSessionUnlocked).filter { $0.boxId == id }.flatMap(\.cards)
            return try activePrayers(of: cards, store: store)
        }
    }

    private func archivedCardIds(isSessionUnlocked: Bool) throws -> Set<Int64> {
        let list = try database.cardList(filter: CardListFilter(searchText: ""), isSessionUnlocked: isSessionUnlocked)
        return Set(list.sections.filter { $0.box?.systemKey == "archived" }.flatMap { $0.cards.map(\.card.id) })
    }

    func eligibleCards(search: String, isSessionUnlocked: Bool = false) throws -> [PrayerTimeSection] {
        let list = try database.cardList(filter: CardListFilter(searchText: search), isSessionUnlocked: isSessionUnlocked)
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

extension PrayerTimeLauncher {
    /// Protected prayers join a session only after the device owner verifies, every time, even when the
    /// session is already unlocked (1.7.2 #255: Prayer Time is an exposed landscape view).
    @MainActor
    func items(for scope: PrayerTimeScope, session: ConfidentialSession) async throws -> [PrayerListItem] {
        let visible = try items(for: scope, isSessionUnlocked: false)
        let unlocked = try items(for: scope, isSessionUnlocked: true)
        if Set(visible.map(\.record.id)) == Set(unlocked.map(\.record.id)) { return visible }
        let allowed = await session.verifyOwner(reason: "Start Prayer Time with protected prayers")
        return allowed ? unlocked : visible
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
    @State private var errorMessage: String?

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
            .alert("Couldn't Start Prayer Time", isPresented: errorBinding) {
                Button("OK") {}
            } message: {
                Text(errorMessage ?? "")
            }
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } })
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
        Task {
            do {
                let items = try await launcher.items(for: scope, session: services.confidential)
                services.prayerSession = PrayerSession(
                    items: items, database: services.database, reconcile: services.reconcileReminders)
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    private func refresh() {
        let launcher = launcher
        Task {
            let result = await Task.detached {
                Result { (try launcher.tagOptions().isEmpty == false, try launcher.collectionOptions().isEmpty == false) }
            }.value
            switch result {
            case .success(let flags): (hasTags, hasCollections) = flags
            case .failure(let error): errorMessage = error.localizedDescription
            }
        }
    }
}

private struct PrayerTimeStartModifier: ViewModifier {
    @Binding var scope: PrayerTimeScope?
    @Environment(AppServices.self) private var services
    @State private var errorMessage: String?

    func body(content: Content) -> some View {
        content
            .onChange(of: scope) { _, newScope in
                guard let newScope else { return }
                scope = nil
                start(newScope)
            }
            .alert("Couldn't Start Prayer Time", isPresented: errorBinding) {
                Button("OK") {}
            } message: {
                Text(errorMessage ?? "")
            }
    }

    private var errorBinding: Binding<Bool> {
        Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } })
    }

    private func start(_ scope: PrayerTimeScope) {
        Task {
            do {
                let items = try await PrayerTimeLauncher(database: services.database)
                    .items(for: scope, session: services.confidential)
                services.prayerSession = PrayerSession(
                    items: items, database: services.database, reconcile: services.reconcileReminders)
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }
}

extension View {
    func prayerTimeStart(_ scope: Binding<PrayerTimeScope?>) -> some View {
        modifier(PrayerTimeStartModifier(scope: scope))
    }

    func prayerTimeLauncher(isPresented: Binding<Bool>) -> some View {
        modifier(PrayerTimeLauncherModifier(isPresented: isPresented))
    }
}
