import GRDB

public struct PrayerTagRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "PrayerTag"

    public var id: Int64
    public var name: String
    public var color: String?
    public var isSystem: Bool?
    public var createdAtTicks: Int64
    public var updatedAtTicks: Int64

    public init(
        id: Int64, name: String, color: String?, isSystem: Bool?,
        createdAtTicks: Int64, updatedAtTicks: Int64
    ) {
        self.id = id
        self.name = name
        self.color = color
        self.isSystem = isSystem
        self.createdAtTicks = createdAtTicks
        self.updatedAtTicks = updatedAtTicks
    }

    public init(row: Row) throws {
        let table = Self.databaseTableName
        id = try row.decode(forColumn: "Id")
        name = try row.decode(forColumn: "Name")
        color = try row.decode(forColumn: "Color")
        isSystem = try row.decode(forColumn: "IsSystem")
        createdAtTicks = try row.requiredTicks("CreatedAt", table: table)
        updatedAtTicks = try row.requiredTicks("UpdatedAt", table: table)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["Name"] = name
        container["Color"] = color
        container["IsSystem"] = isSystem
        container["CreatedAt"] = createdAtTicks
        container["UpdatedAt"] = updatedAtTicks
    }
}
