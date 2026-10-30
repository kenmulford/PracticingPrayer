import Foundation
import Observation
import PrayerKit

@Observable
final class CardDetailModel {
    @ObservationIgnored private let cardId: Int64
    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let preferences: AppPreferences
    @ObservationIgnored private let isSessionUnlocked: Bool

    private(set) var card: PrayerCardRecord?
    private(set) var collectionName = ""
    private(set) var isProtected = false
    private(set) var isMutating = false
    var searchText = ""
    var errorMessage: String?
    private(set) var shareItem: ShareItem?
    var shareError: String?
    var canShowShare: Bool { !isSystem && !isProtected }
    var isSystem: Bool { card?.isSystem ?? false }
    var isArchived: Bool {
        let folder = Int64(preferences.archivedFolderId)
        return folder != 0 && card?.boxId == folder
    }
    private(set) var prayerCount = 0
    private(set) var activePrayerCount = 0
    var canPray: Bool { activePrayerCount > 0 }
    private(set) var activePrayers: [PrayerRequestRecord] = []
    private(set) var answeredPrayers: [PrayerRequestRecord] = []
    private(set) var moveTargets: [CardBoxRecord] = []

    init(cardId: Int64, database: PrayerDatabase, preferences: AppPreferences, isSessionUnlocked: Bool = false) {
        self.isSessionUnlocked = isSessionUnlocked
        self.cardId = cardId
        self.database = database
        self.preferences = preferences
    }

    func load() {
        do {
            let id = cardId
            let list = try database.cardList(isSessionUnlocked: isSessionUnlocked)
            let found = list.sections.lazy.compactMap { section in
                section.cards.first { $0.card.id == id }.map { (section, $0.card) }
            }.first
            guard let (section, record) = found else { return }
            card = record
            isProtected = !ShareItemBuilder.isShareable(record, box: section.box)
            collectionName = section.box?.name ?? String(localized: "Loose Cards")

            let all = try PrayerStore(database: database).prayers(cardId: cardId).map(\.record)
            prayerCount = all.count
            do {
                shareItem = try ShareItemBuilder.card(record, prayers: all)
            } catch {
                shareItem = nil
                shareError = error.localizedDescription
            }
            activePrayerCount = all.filter { !$0.isAnswered }.count
            let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
            let matching = query.isEmpty ? all : all.filter { $0.title.range(of: query, options: .caseInsensitive) != nil }
            activePrayers = matching.filter { !$0.isAnswered }
            answeredPrayers = matching.filter(\.isAnswered)

            moveTargets = try CollectionStore(database: database).collections()
                .filter { !$0.isSystem }
                .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private var mutations: CardMutations { CardMutations(database: database, preferences: preferences) }
    private var prayers: PrayerStore { PrayerStore(database: database) }

    func toggleFavorite(now: Date = .now) {
        guard let card else { return }
        mutate { try mutations.setFavorite(id: cardId, !card.isFavorite, now: now) }
    }

    func move(toBox boxId: Int64, now: Date = .now) {
        mutate { try mutations.move(id: cardId, toBox: boxId, now: now) }
    }

    func archive(now: Date = .now) -> Bool {
        mutate { try mutations.archive(id: cardId, now: now) }
    }

    func unarchive(now: Date = .now) {
        mutate { try mutations.unarchive(id: cardId, now: now) }
    }

    func delete() -> Bool {
        mutate { try mutations.delete(id: cardId) }
    }

    func markAnswered(prayerId: Int64, now: Date = .now) {
        mutate { try prayers.markAnswered(id: prayerId, now: now) }
    }

    func markActive(prayerId: Int64, now: Date = .now) {
        mutate { try prayers.markActive(id: prayerId, now: now) }
    }

    func deletePrayer(id: Int64) {
        mutate { try prayers.delete(id: id) }
    }

    @discardableResult
    private func mutate(_ work: () throws -> Void) -> Bool {
        guard !isMutating else { return false }
        isMutating = true
        defer { isMutating = false }
        do {
            try work()
            load()
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }
}
