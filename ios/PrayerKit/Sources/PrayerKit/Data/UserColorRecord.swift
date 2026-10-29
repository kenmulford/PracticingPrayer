import GRDB

public struct UserColorRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "UserColor"

    public var id: Int64
    public var hexValue: String
    public var isDefault: Bool
    public var createdAtTicks: Int64

    public init(id: Int64, hexValue: String, isDefault: Bool, createdAtTicks: Int64) {
        self.id = id
        self.hexValue = hexValue
        self.isDefault = isDefault
        self.createdAtTicks = createdAtTicks
    }

    public init(row: Row) throws {
        id = try row.decode(forColumn: "Id")
        hexValue = try row.decode(forColumn: "HexValue")
        isDefault = try row.decode(forColumn: "IsDefault")
        createdAtTicks = try row.requiredTicks("CreatedAt", table: Self.databaseTableName)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["HexValue"] = hexValue
        container["IsDefault"] = isDefault
        container["CreatedAt"] = createdAtTicks
    }
}
