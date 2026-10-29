import Observation
import PrayerKit
import SwiftUI

@Observable
final class TagsModel {
    @ObservationIgnored private let database: PrayerDatabase

    private(set) var load: Result<[TagSummary], any Error>?
    var pendingDelete: PrayerTagRecord?
    var errorMessage: String?

    init(database: PrayerDatabase) {
        self.database = database
    }

    static func canDelete(_ tag: PrayerTagRecord) -> Bool {
        tag.isSystem != true
    }

    func reload() {
        load = Result { try TagStore(database: database).tags() }
    }

    func summary(id: Int64) -> TagSummary? {
        guard case .success(let summaries)? = load else { return nil }
        return summaries.first { $0.tag.id == id }
    }

    @discardableResult
    func confirmDelete() -> Bool {
        guard let tag = pendingDelete else { return false }
        defer {
            pendingDelete = nil
            reload()
        }
        do {
            try TagStore(database: database).deleteTag(id: tag.id)
            AccessibilityNotification.Announcement(String(localized: "Tag deleted")).post()
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }
}
