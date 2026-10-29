import Foundation
import GRDB

public struct PrayerInteractionRecorder: Sendable {
    private let database: PrayerDatabase

    public init(database: PrayerDatabase) {
        self.database = database
    }

    @discardableResult
    public func recordPrayed(prayerId: Int64, now: Date) throws -> Int64 {
        try database.dbQueue.write { db in
            let ticks = now.ticks
            try db.execute(
                sql: """
                    INSERT INTO PrayerInteraction (PrayerId, InteractionType, InteractionAt, CreatedAt, UpdatedAt)
                    VALUES (?, 'Prayed', ?, ?, ?)
                    """,
                arguments: [prayerId, ticks, ticks, ticks])
            return db.lastInsertedRowID
        }
    }
}
