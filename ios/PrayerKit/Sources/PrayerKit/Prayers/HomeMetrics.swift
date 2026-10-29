import Foundation
import GRDB

public struct HomeMetrics: Sendable, Equatable {
    public var overdue: [PrayerRequestRecord]
    public var activeCards: Int
    public var unanswered: Int
    public var lastPrayed: Date?
    public var answeredOnThisDay: [PrayerRequestRecord]

    public init(
        overdue: [PrayerRequestRecord], activeCards: Int, unanswered: Int, lastPrayed: Date?,
        answeredOnThisDay: [PrayerRequestRecord]
    ) {
        self.overdue = overdue
        self.activeCards = activeCards
        self.unanswered = unanswered
        self.lastPrayed = lastPrayed
        self.answeredOnThisDay = answeredOnThisDay
    }
}

public extension PrayerDatabase {
    func homeMetrics(now: Date, overdueDayThreshold: Int = 30) throws -> HomeMetrics {
        let active = try PrayerStore(database: self).prayers(segment: .active)
        return HomeMetrics(
            overdue: try overduePrayers(now: now, overdueDayThreshold: overdueDayThreshold),
            activeCards: Set(active.map(\.record.prayerCardId)).count,
            unanswered: active.count,
            lastPrayed: try lastPrayed(),
            answeredOnThisDay: try answeredOnThisDay(now: now))
    }

    func interactionCount(prayerId: Int64) throws -> Int {
        try dbQueue.read { db in
            try Int.fetchOne(
                db, sql: "SELECT COUNT(*) FROM PrayerInteraction WHERE PrayerId = ?", arguments: [prayerId]) ?? 0
        }
    }

    func overduePrayers(now: Date, overdueDayThreshold: Int = 30) throws -> [PrayerRequestRecord] {
        let active = try PrayerStore(database: self).prayers(segment: .active).map(\.record)
        let latest: [Int64: Int64] = try dbQueue.read { db in
            let rows = try Row.fetchAll(
                db,
                sql: "SELECT PrayerId, MAX(InteractionAt) AS LatestInteractionAt FROM PrayerInteraction GROUP BY PrayerId")
            return Dictionary(
                rows.compactMap { row -> (Int64, Int64)? in
                    guard let id: Int64 = row["PrayerId"], let at: Int64 = row["LatestInteractionAt"] else { return nil }
                    return (id, at)
                },
                uniquingKeysWith: { first, _ in first })
        }
        let cutoff = Calendar.current.date(byAdding: .day, value: -max(1, overdueDayThreshold), to: now)!.ticks
        return active
            .compactMap { record in latest[record.id].map { (record, $0) } }
            .filter { $0.1 < cutoff }
            .sorted { ($0.1, $0.0.id) < ($1.1, $1.0.id) }
            .map(\.0)
    }

    func lastPrayed() throws -> Date? {
        let value = try dbQueue.read { db in
            try DatabaseValue.fetchOne(db, sql: "SELECT MAX(InteractionAt) FROM PrayerInteraction") ?? .null
        }
        return try TicksDate.decode(value, column: "InteractionAt")
    }

    func answeredOnThisDay(now: Date) throws -> [PrayerRequestRecord] {
        let calendar = Calendar.current
        let today = calendar.dateComponents([.year, .month, .day], from: now)
        var matches: [PrayerRequestRecord] = []
        for item in try PrayerStore(database: self).prayers(segment: .answered) {
            guard let ticks = item.record.answeredAtTicks else { continue }
            let answered = calendar.dateComponents([.year, .month, .day], from: try Date(ticks: ticks))
            if answered.month == today.month, answered.day == today.day, answered.year! < today.year! {
                matches.append(item.record)
            }
        }
        return matches.sorted { $0.id < $1.id }
    }
}
