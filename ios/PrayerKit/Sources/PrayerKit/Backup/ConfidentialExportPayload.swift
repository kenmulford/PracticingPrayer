import Foundation

/// The JSON inside `confidential.enc`, in the System.Text.Json shape MAUI writes: PascalCase keys,
/// enums as integers, dates as wall-clock ISO 8601.
public struct ConfidentialExportPayload: Codable, Equatable, Sendable {
    public var schemaVersion: Int
    public var cards: [PrayerCardRecord]
    public var prayers: [PrayerRequestRecord]
    public var interactions: [PrayerInteractionRecord]
    public var tagJunctions: [PrayerCardTagRecord]

    public init(
        schemaVersion: Int = 1, cards: [PrayerCardRecord], prayers: [PrayerRequestRecord],
        interactions: [PrayerInteractionRecord], tagJunctions: [PrayerCardTagRecord]
    ) {
        self.schemaVersion = schemaVersion
        self.cards = cards
        self.prayers = prayers
        self.interactions = interactions
        self.tagJunctions = tagJunctions
    }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion = "SchemaVersion", cards = "Cards", prayers = "Prayers"
        case interactions = "Interactions", tagJunctions = "TagJunctions"
    }

    public func toJSON() throws -> String {
        String(decoding: try JSONEncoder().encode(self), as: UTF8.self)
    }

    public static func fromJSON(_ json: String) throws -> ConfidentialExportPayload {
        try JSONDecoder().decode(ConfidentialExportPayload.self, from: Data(json.utf8))
    }
}

enum PayloadDateError: Error, Equatable {
    case invalid(String)
}

/// Ticks are wall-clock time with no zone, so this is integer math on the fields and never a
/// `Date` or `TimeZone` conversion. `ISO8601DateFormatter` is avoided because it stops at milliseconds.
enum PayloadDate {
    private static let ticksPerSecond: Int64 = 10_000_000
    private static let secondsPerDay: Int64 = 86_400

    static func string(ticks: Int64) -> String {
        let sinceUnix = ticks - TicksDate.unixEpochTicks
        let seconds = floorDiv(sinceUnix, ticksPerSecond)
        let fraction = sinceUnix - seconds * ticksPerSecond
        let days = floorDiv(seconds, secondsPerDay)
        let secondOfDay = seconds - days * secondsPerDay
        let (year, month, day) = civil(fromDays: days)
        return String(
            format: "%04lld-%02lld-%02lldT%02lld:%02lld:%02lld.%07lld",
            year, month, day, secondOfDay / 3600, secondOfDay % 3600 / 60, secondOfDay % 60, fraction)
    }

    /// Reads `yyyy-MM-ddTHH:mm:ss` with 0 to 7 fraction digits; a trailing `Z` or `+hh:mm` is dropped.
    static func ticks(_ text: String) throws -> Int64 {
        let bytes = Array(text.utf8)
        func number(_ range: Range<Int>) -> Int64? {
            guard range.upperBound <= bytes.count else { return nil }
            var value: Int64 = 0
            for byte in bytes[range] {
                guard byte >= 0x30, byte <= 0x39 else { return nil }
                value = value * 10 + Int64(byte - 0x30)
            }
            return value
        }
        guard bytes.count >= 19, bytes[4] == 0x2D, bytes[7] == 0x2D, bytes[10] == 0x54 || bytes[10] == 0x20,
              bytes[13] == 0x3A, bytes[16] == 0x3A,
              let year = number(0..<4), let month = number(5..<7), let day = number(8..<10),
              let hour = number(11..<13), let minute = number(14..<16), let second = number(17..<19),
              (1...12).contains(month), (1...31).contains(day), hour < 24, minute < 60, second < 60
        else { throw PayloadDateError.invalid(text) }

        var fraction: Int64 = 0
        if bytes.count > 19, bytes[19] == 0x2E {
            var index = 20
            var digits = 0
            while index < bytes.count, bytes[index] >= 0x30, bytes[index] <= 0x39 {
                if digits < 7 {
                    fraction = fraction * 10 + Int64(bytes[index] - 0x30)
                    digits += 1
                }
                index += 1
            }
            guard digits > 0 else { throw PayloadDateError.invalid(text) }
            for _ in digits..<7 { fraction *= 10 }
        }
        let days = days(fromCivilYear: year, month: month, day: day)
        let seconds = days * secondsPerDay + hour * 3600 + minute * 60 + second
        return TicksDate.unixEpochTicks + seconds * ticksPerSecond + fraction
    }

    private static func floorDiv(_ a: Int64, _ b: Int64) -> Int64 {
        let q = a / b
        return a % b < 0 ? q - 1 : q
    }

    /// Days since 1970-01-01 for a proleptic Gregorian date.
    private static func days(fromCivilYear y: Int64, month m: Int64, day d: Int64) -> Int64 {
        let year = m <= 2 ? y - 1 : y
        let era = floorDiv(year, 400)
        let yoe = year - era * 400
        let doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        return era * 146_097 + doe - 719_468
    }

    private static func civil(fromDays days: Int64) -> (Int64, Int64, Int64) {
        let z = days + 719_468
        let era = floorDiv(z, 146_097)
        let doe = z - era * 146_097
        let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let day = doy - (153 * mp + 2) / 5 + 1
        let month = mp < 10 ? mp + 3 : mp - 9
        return (yoe + era * 400 + (month <= 2 ? 1 : 0), month, day)
    }
}

private func encodeDate(_ ticks: Int64) -> String { PayloadDate.string(ticks: ticks) }

private func decodeDate<K: CodingKey>(_ c: KeyedDecodingContainer<K>, _ key: K) throws -> Int64 {
    let text = try c.decode(String.self, forKey: key)
    do {
        return try PayloadDate.ticks(text)
    } catch {
        throw DecodingError.dataCorruptedError(forKey: key, in: c, debugDescription: "Invalid date")
    }
}

private func decodeOptionalDate<K: CodingKey>(_ c: KeyedDecodingContainer<K>, _ key: K) throws -> Int64? {
    guard let text = try c.decodeIfPresent(String.self, forKey: key) else { return nil }
    do {
        return try PayloadDate.ticks(text)
    } catch {
        throw DecodingError.dataCorruptedError(forKey: key, in: c, debugDescription: "Invalid date")
    }
}

private func decodeEnum<E: RawRepresentable, K: CodingKey>(
    _ type: E.Type, _ c: KeyedDecodingContainer<K>, _ key: K
) throws -> E where E.RawValue == Int64 {
    let raw = try c.decode(Int64.self, forKey: key)
    guard let value = E(rawValue: raw) else {
        throw DecodingError.dataCorruptedError(forKey: key, in: c, debugDescription: "Unknown value \(raw)")
    }
    return value
}

extension PrayerCardRecord: Codable {
    private enum CodingKeys: String, CodingKey {
        case id = "Id", title = "Title", canNotify = "CanNotify", prayerFrequency = "PrayerFrequency"
        case isAnswered = "IsAnswered", isFavorite = "IsFavorite", isSystem = "IsSystem"
        case isImported = "IsImported", protectionMode = "ProtectionMode", boxId = "BoxId"
        case preArchiveBoxId = "PreArchiveBoxId", systemKey = "SystemKey"
        case createdAt = "CreatedAt", updatedAt = "UpdatedAt"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(Int64.self, forKey: .id)
        title = try c.decode(String.self, forKey: .title)
        canNotify = try c.decode(Bool.self, forKey: .canNotify)
        prayerFrequency = try decodeEnum(PrayerFrequency.self, c, .prayerFrequency)
        isAnswered = try c.decode(Bool.self, forKey: .isAnswered)
        isFavorite = try c.decode(Bool.self, forKey: .isFavorite)
        isSystem = try c.decodeIfPresent(Bool.self, forKey: .isSystem)
        isImported = try c.decodeIfPresent(Bool.self, forKey: .isImported)
        protectionMode = try c.decodeIfPresent(Int64.self, forKey: .protectionMode).map { raw in
            guard let mode = CardProtectionMode(rawValue: raw) else {
                throw DecodingError.dataCorruptedError(
                    forKey: .protectionMode, in: c, debugDescription: "Unknown value \(raw)")
            }
            return mode
        }
        boxId = try c.decodeIfPresent(Int64.self, forKey: .boxId)
        preArchiveBoxId = try c.decodeIfPresent(Int64.self, forKey: .preArchiveBoxId)
        systemKey = try c.decodeIfPresent(String.self, forKey: .systemKey)
        createdAtTicks = try decodeDate(c, .createdAt)
        updatedAtTicks = try decodeDate(c, .updatedAt)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(title, forKey: .title)
        try c.encode(canNotify, forKey: .canNotify)
        try c.encode(prayerFrequency.rawValue, forKey: .prayerFrequency)
        try c.encode(isAnswered, forKey: .isAnswered)
        try c.encode(isFavorite, forKey: .isFavorite)
        try c.encodeIfPresent(isSystem, forKey: .isSystem)
        try c.encodeIfPresent(isImported, forKey: .isImported)
        try c.encodeIfPresent(protectionMode?.rawValue, forKey: .protectionMode)
        try c.encodeIfPresent(boxId, forKey: .boxId)
        try c.encodeIfPresent(preArchiveBoxId, forKey: .preArchiveBoxId)
        try c.encodeIfPresent(systemKey, forKey: .systemKey)
        try c.encode(encodeDate(createdAtTicks), forKey: .createdAt)
        try c.encode(encodeDate(updatedAtTicks), forKey: .updatedAt)
    }
}

extension PrayerRequestRecord: Codable {
    private enum CodingKeys: String, CodingKey {
        case id = "Id", prayerCardId = "PrayerCardId", title = "Title", details = "Details"
        case canNotify = "CanNotify", prayerFrequency = "PrayerFrequency", notifyHour = "NotifyHour"
        case notifyMinute = "NotifyMinute", notifyDayOfWeek = "NotifyDayOfWeek"
        case notifyDayOfMonth = "NotifyDayOfMonth", notifyDate = "NotifyDate", isImported = "IsImported"
        case isAnswered = "IsAnswered", answeredAt = "AnsweredAt", createdAt = "CreatedAt"
        case updatedAt = "UpdatedAt"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(Int64.self, forKey: .id)
        prayerCardId = try c.decode(Int64.self, forKey: .prayerCardId)
        title = try c.decode(String.self, forKey: .title)
        details = try c.decodeIfPresent(String.self, forKey: .details)
        canNotify = try c.decode(Bool.self, forKey: .canNotify)
        prayerFrequency = try decodeEnum(PrayerFrequency.self, c, .prayerFrequency)
        notifyHour = try c.decodeIfPresent(Int.self, forKey: .notifyHour)
        notifyMinute = try c.decodeIfPresent(Int.self, forKey: .notifyMinute)
        notifyDayOfWeek = try c.decodeIfPresent(Int.self, forKey: .notifyDayOfWeek)
        notifyDayOfMonth = try c.decodeIfPresent(Int.self, forKey: .notifyDayOfMonth)
        notifyDateTicks = try decodeOptionalDate(c, .notifyDate)
        isImported = try c.decodeIfPresent(Bool.self, forKey: .isImported)
        isAnswered = try c.decode(Bool.self, forKey: .isAnswered)
        answeredAtTicks = try decodeOptionalDate(c, .answeredAt)
        createdAtTicks = try decodeDate(c, .createdAt)
        updatedAtTicks = try decodeDate(c, .updatedAt)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(prayerCardId, forKey: .prayerCardId)
        try c.encode(title, forKey: .title)
        try c.encodeIfPresent(details, forKey: .details)
        try c.encode(canNotify, forKey: .canNotify)
        try c.encode(prayerFrequency.rawValue, forKey: .prayerFrequency)
        try c.encodeIfPresent(notifyHour, forKey: .notifyHour)
        try c.encodeIfPresent(notifyMinute, forKey: .notifyMinute)
        try c.encodeIfPresent(notifyDayOfWeek, forKey: .notifyDayOfWeek)
        try c.encodeIfPresent(notifyDayOfMonth, forKey: .notifyDayOfMonth)
        try c.encodeIfPresent(notifyDateTicks.map(encodeDate), forKey: .notifyDate)
        try c.encodeIfPresent(isImported, forKey: .isImported)
        try c.encode(isAnswered, forKey: .isAnswered)
        try c.encodeIfPresent(answeredAtTicks.map(encodeDate), forKey: .answeredAt)
        try c.encode(encodeDate(createdAtTicks), forKey: .createdAt)
        try c.encode(encodeDate(updatedAtTicks), forKey: .updatedAt)
    }
}

extension PrayerInteractionRecord: Codable {
    private enum CodingKeys: String, CodingKey {
        case id = "Id", prayerId = "PrayerId", interactionType = "InteractionType"
        case interactionAt = "InteractionAt", createdAt = "CreatedAt", updatedAt = "UpdatedAt"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(Int64.self, forKey: .id)
        prayerId = try c.decode(Int64.self, forKey: .prayerId)
        interactionType = try c.decode(String.self, forKey: .interactionType)
        interactionAtTicks = try decodeDate(c, .interactionAt)
        createdAtTicks = try decodeDate(c, .createdAt)
        updatedAtTicks = try decodeDate(c, .updatedAt)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(prayerId, forKey: .prayerId)
        try c.encode(interactionType, forKey: .interactionType)
        try c.encode(encodeDate(interactionAtTicks), forKey: .interactionAt)
        try c.encode(encodeDate(createdAtTicks), forKey: .createdAt)
        try c.encode(encodeDate(updatedAtTicks), forKey: .updatedAt)
    }
}

extension PrayerCardTagRecord: Codable {
    private enum CodingKeys: String, CodingKey {
        case id = "Id", prayerCardId = "PrayerCardId", prayerTagId = "PrayerTagId"
        case prayerRequestId = "PrayerRequestId", createdAt = "CreatedAt"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(Int64.self, forKey: .id)
        prayerCardId = try c.decode(Int64.self, forKey: .prayerCardId)
        prayerTagId = try c.decode(Int64.self, forKey: .prayerTagId)
        prayerRequestId = try c.decodeIfPresent(Int64.self, forKey: .prayerRequestId)
        createdAtTicks = try decodeDate(c, .createdAt)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(prayerCardId, forKey: .prayerCardId)
        try c.encode(prayerTagId, forKey: .prayerTagId)
        try c.encodeIfPresent(prayerRequestId, forKey: .prayerRequestId)
        try c.encode(encodeDate(createdAtTicks), forKey: .createdAt)
    }
}
