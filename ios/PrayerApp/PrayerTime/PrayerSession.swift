import Foundation
import Observation
import PrayerKit
import SwiftUI

@Observable
final class PrayerSession: Identifiable {
    #if DEBUG
    static let debugArgument = "-prayerSession"
    static let debugAutoArgument = "-prayerSessionAuto"
    #endif

    static let autoIntervals = [30, 60, 120]

    static func autoInterval(stored: Int) -> Int {
        autoIntervals.contains(stored) ? stored : 30
    }

    let items: [PrayerListItem]
    private(set) var currentIndex = 0
    private(set) var answeredIds: Set<Int64> = []
    var errorMessage: String?

    @ObservationIgnored private let recordPrayed: (Int64) throws -> Void
    @ObservationIgnored private let markAnsweredWrite: (Int64) throws -> Void
    @ObservationIgnored private let clearRecentlyNotified: (Int64) throws -> Void
    @ObservationIgnored private let reconcile: () -> Void

    var isComplete: Bool { currentIndex >= items.count }
    var isFirst: Bool { currentIndex == 0 }

    init?(
        items: [PrayerListItem],
        recordPrayed: @escaping (Int64) throws -> Void,
        markAnswered: @escaping (Int64) throws -> Void,
        clearRecentlyNotified: @escaping (Int64) throws -> Void = { _ in },
        reconcile: @escaping () -> Void = {}
    ) {
        guard !items.isEmpty else { return nil }
        self.items = items
        self.recordPrayed = recordPrayed
        self.markAnsweredWrite = markAnswered
        self.clearRecentlyNotified = clearRecentlyNotified
        self.reconcile = reconcile
    }

    convenience init?(
        items: [PrayerListItem], database: PrayerDatabase, now: @escaping () -> Date = { .now },
        reconcile: @escaping () -> Void = {}
    ) {
        let recorder = PrayerInteractionRecorder(database: database)
        let store = PrayerStore(database: database)
        let tags = TagStore(database: database)
        self.init(
            items: items,
            recordPrayed: { _ = try recorder.recordPrayed(prayerId: $0, now: now()) },
            markAnswered: { try store.markAnswered(id: $0, now: now()) },
            clearRecentlyNotified: { try tags.removeRecentlyNotified(prayerId: $0) },
            reconcile: reconcile)
    }

    func isAnswered(_ item: PrayerListItem) -> Bool {
        answeredIds.contains(item.record.id) || item.record.isAnswered
    }

    func next() {
        guard !isComplete else { return }
        let id = items[currentIndex].record.id
        do {
            try recordPrayed(id)
            try clearRecentlyNotified(id)
        } catch {}
        currentIndex += 1
        announce()
    }

    func previous() {
        guard currentIndex > 0 else { return }
        currentIndex -= 1
        announce()
    }

    func markAnswered() {
        guard !isComplete else { return }
        let item = items[currentIndex]
        do {
            try markAnsweredWrite(item.record.id)
        } catch {
            errorMessage = error.localizedDescription
            return
        }
        reconcile()
        answeredIds.insert(item.record.id)
        AccessibilityNotification.Announcement(String(localized: "\(item.record.title) marked as answered")).post()
        next()
    }

    func move(to index: Int) {
        if index == currentIndex + 1 {
            next()
        } else if index == currentIndex - 1 {
            previous()
        }
    }

    private func announce() {
        let text = isComplete
            ? String(localized: "Prayer session complete")
            : String(localized: "Prayer \(currentIndex + 1) of \(items.count). \(items[currentIndex].record.title)")
        AccessibilityNotification.Announcement(text).post()
    }
}
