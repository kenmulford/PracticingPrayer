import Foundation

enum AppTab: Hashable, CaseIterable {
    case home, cards, prayers, tags, settings
}

extension AppTab {
    var label: LocalizedStringResource {
        switch self {
        case .home: "Home"
        case .cards: "Cards"
        case .prayers: "Prayers"
        case .tags: "Tags"
        case .settings: "Settings"
        }
    }

    var title: LocalizedStringResource {
        switch self {
        case .cards: "Prayer Cards"
        default: label
        }
    }

    var systemImage: String {
        switch self {
        case .home: "house"
        case .cards: "rectangle.stack"
        case .prayers: "hands.and.sparkles"
        case .tags: "tag"
        case .settings: "gearshape"
        }
    }
}

enum HomeRoute: Hashable { case overdue, unanswered, activeCards }
enum CardsRoute: Hashable {}
enum PrayersRoute: Hashable {}
enum TagsRoute: Hashable {}
enum SettingsRoute: Hashable { case appSettings, collections, backup, help, about }
