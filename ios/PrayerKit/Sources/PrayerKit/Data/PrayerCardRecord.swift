import GRDB

public struct PrayerCardRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "PrayerCard"

    public var id: Int64
    public var title: String
    public var canNotify: Bool
    public var prayerFrequency: PrayerFrequency
    public var isAnswered: Bool
    public var isFavorite: Bool
    public var isSystem: Bool?
    public var isImported: Bool?
    public var protectionMode: CardProtectionMode?
    public var boxId: Int64?
    public var preArchiveBoxId: Int64?
    public var systemKey: String?
    public var createdAtTicks: Int64
    public var updatedAtTicks: Int64

    public init(
        id: Int64, title: String, canNotify: Bool, prayerFrequency: PrayerFrequency,
        isAnswered: Bool, isFavorite: Bool, isSystem: Bool?, isImported: Bool?,
        protectionMode: CardProtectionMode?, boxId: Int64?, preArchiveBoxId: Int64?,
        systemKey: String?, createdAtTicks: Int64, updatedAtTicks: Int64
    ) {
        self.id = id
        self.title = title
        self.canNotify = canNotify
        self.prayerFrequency = prayerFrequency
        self.isAnswered = isAnswered
        self.isFavorite = isFavorite
        self.isSystem = isSystem
        self.isImported = isImported
        self.protectionMode = protectionMode
        self.boxId = boxId
        self.preArchiveBoxId = preArchiveBoxId
        self.systemKey = systemKey
        self.createdAtTicks = createdAtTicks
        self.updatedAtTicks = updatedAtTicks
    }

    public init(row: Row) throws {
        let table = Self.databaseTableName
        id = try row.decode(forColumn: "Id")
        title = try row.decode(forColumn: "Title")
        canNotify = try row.decode(forColumn: "CanNotify")
        prayerFrequency = try row.requiredEnum(PrayerFrequency.self, "PrayerFrequency", table: table)
        isAnswered = try row.decode(forColumn: "IsAnswered")
        isFavorite = try row.decode(forColumn: "IsFavorite")
        isSystem = try row.decode(forColumn: "IsSystem")
        isImported = try row.decode(forColumn: "IsImported")
        protectionMode = try row.enumValue(CardProtectionMode.self, "ProtectionMode", table: table)
        boxId = try row.decode(forColumn: "BoxId")
        preArchiveBoxId = try row.decode(forColumn: "PreArchiveBoxId")
        systemKey = try row.decode(forColumn: "SystemKey")
        createdAtTicks = try row.requiredTicks("CreatedAt", table: table)
        updatedAtTicks = try row.requiredTicks("UpdatedAt", table: table)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["Title"] = title
        container["CanNotify"] = canNotify
        container["PrayerFrequency"] = prayerFrequency.rawValue
        container["IsAnswered"] = isAnswered
        container["IsFavorite"] = isFavorite
        container["IsSystem"] = isSystem
        container["IsImported"] = isImported
        container["ProtectionMode"] = protectionMode?.rawValue
        container["BoxId"] = boxId
        container["PreArchiveBoxId"] = preArchiveBoxId
        container["SystemKey"] = systemKey
        container["CreatedAt"] = createdAtTicks
        container["UpdatedAt"] = updatedAtTicks
    }
}
