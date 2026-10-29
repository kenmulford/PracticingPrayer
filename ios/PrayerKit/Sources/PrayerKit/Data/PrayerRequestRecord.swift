import GRDB

public struct PrayerRequestRecord: FetchableRecord, PersistableRecord, Equatable, Sendable {
    public static let databaseTableName = "PrayerRequest"

    public var id: Int64
    public var prayerCardId: Int64
    public var title: String
    public var details: String?
    public var canNotify: Bool
    public var prayerFrequency: PrayerFrequency
    public var isAnswered: Bool
    public var answeredAtTicks: Int64?
    public var notifyHour: Int?
    public var notifyMinute: Int?
    public var notifyDayOfWeek: Int?
    public var notifyDayOfMonth: Int?
    public var notifyDateTicks: Int64?
    public var isImported: Bool?
    public var createdAtTicks: Int64
    public var updatedAtTicks: Int64

    public init(
        id: Int64, prayerCardId: Int64, title: String, details: String?, canNotify: Bool,
        prayerFrequency: PrayerFrequency, isAnswered: Bool, answeredAtTicks: Int64?,
        notifyHour: Int?, notifyMinute: Int?, notifyDayOfWeek: Int?, notifyDayOfMonth: Int?,
        notifyDateTicks: Int64?, isImported: Bool?, createdAtTicks: Int64, updatedAtTicks: Int64
    ) {
        self.id = id
        self.prayerCardId = prayerCardId
        self.title = title
        self.details = details
        self.canNotify = canNotify
        self.prayerFrequency = prayerFrequency
        self.isAnswered = isAnswered
        self.answeredAtTicks = answeredAtTicks
        self.notifyHour = notifyHour
        self.notifyMinute = notifyMinute
        self.notifyDayOfWeek = notifyDayOfWeek
        self.notifyDayOfMonth = notifyDayOfMonth
        self.notifyDateTicks = notifyDateTicks
        self.isImported = isImported
        self.createdAtTicks = createdAtTicks
        self.updatedAtTicks = updatedAtTicks
    }

    public init(row: Row) throws {
        let table = Self.databaseTableName
        id = try row.decode(forColumn: "Id")
        prayerCardId = try row.decode(forColumn: "PrayerCardId")
        title = try row.decode(forColumn: "Title")
        details = try row.decode(forColumn: "Details")
        canNotify = try row.decode(forColumn: "CanNotify")
        prayerFrequency = try row.requiredEnum(PrayerFrequency.self, "PrayerFrequency", table: table)
        isAnswered = try row.decode(forColumn: "IsAnswered")
        answeredAtTicks = try row.ticks("AnsweredAt", table: table)
        notifyHour = try row.decode(forColumn: "NotifyHour")
        notifyMinute = try row.decode(forColumn: "NotifyMinute")
        notifyDayOfWeek = try row.decode(forColumn: "NotifyDayOfWeek")
        notifyDayOfMonth = try row.decode(forColumn: "NotifyDayOfMonth")
        notifyDateTicks = try row.ticks("NotifyDate", table: table)
        isImported = try row.decode(forColumn: "IsImported")
        createdAtTicks = try row.requiredTicks("CreatedAt", table: table)
        updatedAtTicks = try row.requiredTicks("UpdatedAt", table: table)
    }

    public func encode(to container: inout PersistenceContainer) throws {
        container["Id"] = id
        container["PrayerCardId"] = prayerCardId
        container["Title"] = title
        container["Details"] = details
        container["CanNotify"] = canNotify
        container["PrayerFrequency"] = prayerFrequency.rawValue
        container["IsAnswered"] = isAnswered
        container["AnsweredAt"] = answeredAtTicks
        container["NotifyHour"] = notifyHour
        container["NotifyMinute"] = notifyMinute
        container["NotifyDayOfWeek"] = notifyDayOfWeek
        container["NotifyDayOfMonth"] = notifyDayOfMonth
        container["NotifyDate"] = notifyDateTicks
        container["IsImported"] = isImported
        container["CreatedAt"] = createdAtTicks
        container["UpdatedAt"] = updatedAtTicks
    }
}
