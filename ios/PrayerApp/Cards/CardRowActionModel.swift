import Foundation
import Observation
import PrayerKit

enum CardRowAction: Hashable {
    case pray, favorite, unfavorite, edit, moveTo, archive, unarchive, delete
}

extension CardRowAction {
    var title: LocalizedStringResource {
        switch self {
        case .pray: "Pray"
        case .favorite: "Favorite"
        case .unfavorite: "Unfavorite"
        case .edit: "Edit"
        case .moveTo: "Move To…"
        case .archive: "Archive"
        case .unarchive: "Unarchive"
        case .delete: "Delete"
        }
    }

    var systemImage: String {
        switch self {
        case .pray: "hands.and.sparkles"
        case .favorite: "star.fill"
        case .unfavorite: "star.slash"
        case .edit: "pencil"
        case .moveTo: "folder"
        case .archive: "archivebox"
        case .unarchive: "tray.and.arrow.up"
        case .delete: "trash"
        }
    }
}

@Observable
final class CardRowActionModel {
    private let title: String
    private let isFavorite: Bool
    private let isArchived: Bool
    private let isLocked: Bool
    private let activePrayerCount: Int

    init(title: String, isFavorite: Bool, isArchived: Bool, isSystem: Bool, isMasked: Bool, activePrayerCount: Int) {
        self.title = title
        self.isFavorite = isFavorite
        self.isArchived = isArchived
        self.isLocked = isSystem || isMasked
        self.activePrayerCount = activePrayerCount
    }

    convenience init(item: CardListItem, archivedFolderId: Int64) {
        self.init(
            title: item.card.title, isFavorite: item.card.isFavorite,
            isArchived: archivedFolderId != 0 && item.card.boxId == archivedFolderId,
            isSystem: item.card.isSystem == true, isMasked: item.isMasked,
            activePrayerCount: item.activePrayerCount ?? 0)
    }

    private var favoriteAction: CardRowAction { isFavorite ? .unfavorite : .favorite }
    private var archiveAction: CardRowAction { isArchived ? .unarchive : .archive }

    var menuActions: [CardRowAction] {
        isLocked ? [] : [.pray, favoriteAction, .edit, .moveTo, archiveAction, .delete]
    }

    var canPray: Bool { activePrayerCount > 0 }

    var leadingActions: [CardRowAction] { isLocked ? [] : [favoriteAction] }

    var trailingActions: [CardRowAction] { isLocked ? [] : [.delete, archiveAction] }

    var deleteMessage: String {
        switch activePrayerCount {
        case 0: String(localized: "Delete “\(title)”?")
        case 1: String(localized: "Delete “\(title)” and its 1 prayer request?")
        default: String(localized: "Delete “\(title)” and its \(activePrayerCount) prayer requests?")
        }
    }

    static func perform(
        _ action: CardRowAction, cardId: Int64, database: PrayerDatabase, preferences: AppPreferences,
        isSessionUnlocked: Bool
    ) -> String? {
        let model = CardDetailModel(
            cardId: cardId, database: database, preferences: preferences, isSessionUnlocked: isSessionUnlocked)
        model.load()
        switch action {
        case .favorite, .unfavorite: model.toggleFavorite()
        case .archive: _ = model.archive()
        case .unarchive: model.unarchive()
        case .delete: _ = model.delete()
        case .pray, .edit, .moveTo: return nil
        }
        return model.errorMessage
    }

    static func move(
        cardId: Int64, toBox boxId: Int64, database: PrayerDatabase, preferences: AppPreferences, isSessionUnlocked: Bool
    ) -> String? {
        let model = CardDetailModel(
            cardId: cardId, database: database, preferences: preferences, isSessionUnlocked: isSessionUnlocked)
        model.load()
        model.move(toBox: boxId)
        return model.errorMessage
    }

    static func previewTitles(
        cardId: Int64, database: PrayerDatabase, preferences: AppPreferences, isSessionUnlocked: Bool
    ) -> [String] {
        let model = CardDetailModel(
            cardId: cardId, database: database, preferences: preferences, isSessionUnlocked: isSessionUnlocked)
        model.load()
        return model.activePrayers.prefix(3).map(\.title)
    }
}
