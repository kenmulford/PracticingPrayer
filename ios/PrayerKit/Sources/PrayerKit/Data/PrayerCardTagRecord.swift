import GRDB

public struct PrayerCardTagRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "PrayerCardTag"

    public var id: Int64
    public var prayerCardId: Int64
    public var prayerTagId: Int64
    public var prayerRequestId: Int64?
    public var createdAtTicks: Int64

    public init(
        id: Int64, prayerCardId: Int64, prayerTagId: Int64, prayerRequestId: Int64?,
        createdAtTicks: Int64
    ) {
        self.id = id
        self.prayerCardId = prayerCardId
        self.prayerTagId = prayerTagId
        self.prayerRequestId = prayerRequestId
        self.createdAtTicks = createdAtTicks
    }

    public init(row: Row) throws {
        id = try row.decode(forColumn: "Id")
        prayerCardId = try row.decode(forColumn: "PrayerCardId")
        prayerTagId = try row.decode(forColumn: "PrayerTagId")
        prayerRequestId = try row.decode(forColumn: "PrayerRequestId")
        createdAtTicks = try row.requiredTicks("CreatedAt", table: Self.databaseTableName)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["PrayerCardId"] = prayerCardId
        container["PrayerTagId"] = prayerTagId
        container["PrayerRequestId"] = prayerRequestId
        container["CreatedAt"] = createdAtTicks
    }
}
