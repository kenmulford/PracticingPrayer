import GRDB

public struct PrayerInteractionRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "PrayerInteraction"

    public var id: Int64
    public var prayerId: Int64
    public var interactionType: String
    public var interactionAtTicks: Int64
    public var createdAtTicks: Int64
    public var updatedAtTicks: Int64

    public init(
        id: Int64, prayerId: Int64, interactionType: String, interactionAtTicks: Int64,
        createdAtTicks: Int64, updatedAtTicks: Int64
    ) {
        self.id = id
        self.prayerId = prayerId
        self.interactionType = interactionType
        self.interactionAtTicks = interactionAtTicks
        self.createdAtTicks = createdAtTicks
        self.updatedAtTicks = updatedAtTicks
    }

    public init(row: Row) throws {
        let table = Self.databaseTableName
        id = try row.decode(forColumn: "Id")
        prayerId = try row.decode(forColumn: "PrayerId")
        interactionType = try row.decode(forColumn: "InteractionType")
        interactionAtTicks = try row.requiredTicks("InteractionAt", table: table)
        createdAtTicks = try row.requiredTicks("CreatedAt", table: table)
        updatedAtTicks = try row.requiredTicks("UpdatedAt", table: table)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["PrayerId"] = prayerId
        container["InteractionType"] = interactionType
        container["InteractionAt"] = interactionAtTicks
        container["CreatedAt"] = createdAtTicks
        container["UpdatedAt"] = updatedAtTicks
    }
}
