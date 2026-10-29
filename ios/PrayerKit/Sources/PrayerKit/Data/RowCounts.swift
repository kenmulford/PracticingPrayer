public struct RowCounts: Equatable, Sendable {
    public let cards: Int
    public let prayers: Int
    public let tags: Int
    public let collections: Int

    public init(cards: Int, prayers: Int, tags: Int, collections: Int) {
        self.cards = cards
        self.prayers = prayers
        self.tags = tags
        self.collections = collections
    }
}

public extension PrayerDatabase {
    func rowCounts() throws -> RowCounts {
        try dbQueue.read { db in
            RowCounts(
                cards: try PrayerCardRecord.fetchCount(db),
                prayers: try PrayerRequestRecord.fetchCount(db),
                tags: try PrayerTagRecord.fetchCount(db),
                collections: try CardBoxRecord.fetchCount(db)
            )
        }
    }
}
