import GRDB

public struct CardBoxRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "CardBox"

    public var id: Int64
    public var name: String
    public var isSystem: Bool
    public var systemKey: String?
    public var sortOrder: Int
    public var protectAllCards: Bool
    public var cardProtectionMode: CardProtectionMode
    public var createdAtTicks: Int64
    public var updatedAtTicks: Int64

    public init(
        id: Int64, name: String, isSystem: Bool, systemKey: String?, sortOrder: Int,
        protectAllCards: Bool, cardProtectionMode: CardProtectionMode,
        createdAtTicks: Int64, updatedAtTicks: Int64
    ) {
        self.id = id
        self.name = name
        self.isSystem = isSystem
        self.systemKey = systemKey
        self.sortOrder = sortOrder
        self.protectAllCards = protectAllCards
        self.cardProtectionMode = cardProtectionMode
        self.createdAtTicks = createdAtTicks
        self.updatedAtTicks = updatedAtTicks
    }

    public init(row: Row) throws {
        let table = Self.databaseTableName
        id = try row.decode(forColumn: "Id")
        name = try row.decode(forColumn: "Name")
        isSystem = try row.decode(forColumn: "IsSystem")
        systemKey = try row.decode(forColumn: "SystemKey")
        sortOrder = try row.decode(forColumn: "SortOrder")
        protectAllCards = try row.decode(forColumn: "ProtectAllCards")
        cardProtectionMode = try row.requiredEnum(CardProtectionMode.self, "CardProtectionMode", table: table)
        createdAtTicks = try row.requiredTicks("CreatedAt", table: table)
        updatedAtTicks = try row.requiredTicks("UpdatedAt", table: table)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["Name"] = name
        container["IsSystem"] = isSystem
        container["SystemKey"] = systemKey
        container["SortOrder"] = sortOrder
        container["ProtectAllCards"] = protectAllCards
        container["CardProtectionMode"] = cardProtectionMode.rawValue
        container["CreatedAt"] = createdAtTicks
        container["UpdatedAt"] = updatedAtTicks
    }
}
