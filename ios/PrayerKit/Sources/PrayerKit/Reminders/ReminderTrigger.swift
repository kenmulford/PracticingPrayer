import Foundation

public struct ReminderRequest: Equatable, Sendable {
    public var identifier: String
    public var title: String
    public var body: String
    public var dateComponents: DateComponents
    public var repeats: Bool

    public init(identifier: String, title: String, body: String, dateComponents: DateComponents, repeats: Bool) {
        self.identifier = identifier
        self.title = title
        self.body = body
        self.dateComponents = dateComponents
        self.repeats = repeats
    }
}

/// Pure schedule math ported from the MAUI app's `NotificationHelper` and `NotificationService.ScheduleAsync`.
/// Dates are local wall-clock values, so all day arithmetic goes through the supplied calendar.
public enum ReminderTrigger {
    static let notificationTitle = "Practicing Prayer"
    static let recentWindow: TimeInterval = 24 * 60 * 60

    static func identifier(for record: PrayerRequestRecord) -> String {
        record.prayerFrequency == .monthly ? "monthly_\(record.id)" : "\(record.id)"
    }

    static func firstFireTime(createdAt: Date, hour: Int, minute: Int, calendar: Calendar) -> Date {
        let sameDay = atTime(createdAt, hour: hour, minute: minute, calendar: calendar)
        return createdAt < sameDay ? sameDay : addDays(1, to: sameDay, calendar: calendar)
    }

    static func oneTimeFire(notifyDate: Date?, createdAt: Date, hour: Int, minute: Int, calendar: Calendar) -> Date {
        if let notifyDate {
            return atTime(notifyDate, hour: hour, minute: minute, calendar: calendar)
        }
        return firstFireTime(createdAt: createdAt, hour: hour, minute: minute, calendar: calendar)
    }

    /// Feb 29 maps to Feb 28 in non-leap years.
    static func yearlyFireOn(year: Int, notifyDate: Date, hour: Int, minute: Int, calendar: Calendar) -> Date {
        let month = calendar.component(.month, from: notifyDate)
        let day = min(calendar.component(.day, from: notifyDate), daysInMonth(year: year, month: month, calendar: calendar))
        return calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))
            ?? notifyDate
    }

    /// The next yearly fire strictly after `now`.
    static func nextYearlyFire(notifyDate: Date, hour: Int, minute: Int, now: Date, calendar: Calendar) -> Date {
        let year = calendar.component(.year, from: now)
        let thisYear = yearlyFireOn(year: year, notifyDate: notifyDate, hour: hour, minute: minute, calendar: calendar)
        return thisYear > now
            ? thisYear
            : yearlyFireOn(year: year + 1, notifyDate: notifyDate, hour: hour, minute: minute, calendar: calendar)
    }

    /// The latest fire at or before `now`; weekly and yearly step by fixed 7 and 365 day intervals as in 1.7.2.
    static func mostRecentFireTime(
        firstFire: Date, frequency: PrayerFrequency, now: Date, hour: Int, minute: Int, calendar: Calendar
    ) -> Date? {
        guard firstFire <= now else { return nil }
        switch frequency {
        case .daily:
            let today = atTime(now, hour: hour, minute: minute, calendar: calendar)
            return now >= today ? today : addDays(-1, to: today, calendar: calendar)
        case .weekly:
            return mostRecentByInterval(firstFire: firstFire, days: 7, now: now, calendar: calendar)
        case .monthly:
            return mostRecentMonthly(firstFire: firstFire, now: now, hour: hour, minute: minute, calendar: calendar)
        case .yearly:
            return mostRecentByInterval(firstFire: firstFire, days: 365, now: now, calendar: calendar)
        case .oneTime:
            return firstFire
        }
    }

    static func request(
        for record: PrayerRequestRecord, hour: Int, minute: Int, now: Date, calendar: Calendar
    ) throws -> ReminderRequest? {
        let createdAt = try Date(ticks: record.createdAtTicks)
        var notifyDate: Date?
        if let ticks = record.notifyDateTicks { notifyDate = try Date(ticks: ticks) }

        let components: DateComponents
        let repeats: Bool
        switch record.prayerFrequency {
        case .daily:
            components = DateComponents(hour: hour, minute: minute)
            repeats = true
        case .weekly:
            let weekday: Int
            if let day = record.notifyDayOfWeek, (0...6).contains(day) {
                weekday = day + 1
            } else {
                let today = atTime(now, hour: hour, minute: minute, calendar: calendar)
                let next = today <= now ? addDays(1, to: today, calendar: calendar) : today
                weekday = calendar.component(.weekday, from: next)
            }
            components = DateComponents(hour: hour, minute: minute, weekday: weekday)
            repeats = true
        case .monthly:
            let configured = record.notifyDayOfMonth ?? 0
            let day = configured > 0 ? configured : calendar.component(.day, from: createdAt)
            components = DateComponents(day: day, hour: hour, minute: minute)
            repeats = true
        case .oneTime:
            let fire = oneTimeFire(notifyDate: notifyDate, createdAt: createdAt, hour: hour, minute: minute, calendar: calendar)
            guard fire > now else { return nil }
            components = fullComponents(fire, calendar: calendar)
            repeats = false
        case .yearly:
            let anchor = notifyDate
                ?? calendar.startOfDay(for: firstFireTime(createdAt: createdAt, hour: hour, minute: minute, calendar: calendar))
            let fire = nextYearlyFire(notifyDate: anchor, hour: hour, minute: minute, now: now, calendar: calendar)
            components = fullComponents(fire, calendar: calendar)
            repeats = false
        }
        return ReminderRequest(
            identifier: identifier(for: record), title: notificationTitle, body: record.title,
            dateComponents: components, repeats: repeats)
    }

    /// Ids of notifying, unanswered prayers whose scheduled fire fell within the last 24 hours.
    static func recentlyNotifiedIds(
        _ records: [PrayerRequestRecord], defaultHour: Int, defaultMinute: Int, now: Date, calendar: Calendar
    ) throws -> [Int64] {
        var result: [Int64] = []
        for record in records where record.canNotify && !record.isAnswered {
            let hour = record.notifyHour ?? defaultHour
            let minute = record.notifyMinute ?? defaultMinute
            let createdAt = try Date(ticks: record.createdAtTicks)

            if let ticks = record.notifyDateTicks,
                record.prayerFrequency == .oneTime || record.prayerFrequency == .yearly
            {
                let notifyDate = try Date(ticks: ticks)
                var fire: Date
                if record.prayerFrequency == .oneTime {
                    fire = oneTimeFire(notifyDate: notifyDate, createdAt: createdAt, hour: hour, minute: minute, calendar: calendar)
                } else {
                    let year = calendar.component(.year, from: now)
                    fire = yearlyFireOn(year: year, notifyDate: notifyDate, hour: hour, minute: minute, calendar: calendar)
                    if fire > now {
                        fire = yearlyFireOn(year: year - 1, notifyDate: notifyDate, hour: hour, minute: minute, calendar: calendar)
                    }
                }
                if fire >= createdAt && fire <= now && now.timeIntervalSince(fire) <= recentWindow {
                    result.append(record.id)
                }
                continue
            }

            let firstFire = firstFireTime(createdAt: createdAt, hour: hour, minute: minute, calendar: calendar)
            guard firstFire <= now else { continue }
            if let recent = mostRecentFireTime(
                firstFire: firstFire, frequency: record.prayerFrequency, now: now,
                hour: hour, minute: minute, calendar: calendar),
                now.timeIntervalSince(recent) <= recentWindow
            {
                result.append(record.id)
            }
        }
        return result
    }

    private static func atTime(_ day: Date, hour: Int, minute: Int, calendar: Calendar) -> Date {
        calendar.date(bySettingHour: hour, minute: minute, second: 0, of: day) ?? day
    }

    private static func addDays(_ days: Int, to date: Date, calendar: Calendar) -> Date {
        calendar.date(byAdding: .day, value: days, to: date) ?? date
    }

    private static func daysInMonth(year: Int, month: Int, calendar: Calendar) -> Int {
        guard let first = calendar.date(from: DateComponents(year: year, month: month, day: 1)),
            let range = calendar.range(of: .day, in: .month, for: first)
        else { return 28 }
        return range.count
    }

    private static func fullComponents(_ date: Date, calendar: Calendar) -> DateComponents {
        let parts = calendar.dateComponents([.year, .month, .day, .hour, .minute], from: date)
        return DateComponents(year: parts.year, month: parts.month, day: parts.day, hour: parts.hour, minute: parts.minute)
    }

    private static func mostRecentByInterval(firstFire: Date, days: Int, now: Date, calendar: Calendar) -> Date {
        let elapsed = calendar.dateComponents([.day], from: firstFire, to: now).day ?? 0
        let candidate = addDays((elapsed / days) * days, to: firstFire, calendar: calendar)
        return candidate > now ? addDays(-days, to: candidate, calendar: calendar) : candidate
    }

    private static func mostRecentMonthly(firstFire: Date, now: Date, hour: Int, minute: Int, calendar: Calendar) -> Date? {
        let dayOfMonth = calendar.component(.day, from: firstFire)
        for offset in [0, -1] {
            guard let shifted = calendar.date(byAdding: .month, value: offset, to: now) else { continue }
            let year = calendar.component(.year, from: shifted)
            let month = calendar.component(.month, from: shifted)
            let day = min(dayOfMonth, daysInMonth(year: year, month: month, calendar: calendar))
            guard let fire = calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))
            else { continue }
            if fire >= firstFire && fire <= now { return fire }
        }
        return nil
    }
}
