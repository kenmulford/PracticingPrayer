import Foundation
import Observation
import PrayerKit
import SwiftUI

@Observable
final class PrayerDetailModel {
    enum Phase: Equatable { case loading, loaded, notFound }

    @ObservationIgnored private let prayerId: Int64
    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let reminders: any ReminderReconciling

    private(set) var phase: Phase = .loading
    private(set) var prayer: PrayerRequestRecord?
    private(set) var cardTitle = ""
    private(set) var tags: [PrayerTagRecord] = []
    private(set) var prayedCount = 0
    private(set) var isMutating = false
    var errorMessage: String?

    var prayedSummary: String? {
        guard prayedCount > 0, let prayer, let started = try? Date(ticks: prayer.createdAtTicks) else { return nil }
        let date = started.formatted(date: .abbreviated, time: .omitted)
        return prayedCount == 1
            ? String(localized: "1 time since \(date)")
            : String(localized: "\(prayedCount) times since \(date)")
    }

    init(prayerId: Int64, database: PrayerDatabase, reminders: any ReminderReconciling) {
        self.prayerId = prayerId
        self.database = database
        self.reminders = reminders
    }

    private var store: PrayerStore { PrayerStore(database: database) }

    func load() {
        do {
            let id = prayerId
            let items = try store.prayers(segment: .all, isSessionUnlocked: false)
            guard let item = items.first(where: { $0.record.id == id }) else {
                prayer = nil
                phase = .notFound
                return
            }
            let wanted = Set(item.tagIds)
            tags = try TagStore(database: database).tags().map(\.tag).filter { wanted.contains($0.id) }
            prayedCount = try database.interactionCount(prayerId: id)
            prayer = item.record
            cardTitle = item.cardTitle
            phase = .loaded
        } catch {
            errorMessage = error.localizedDescription
            if prayer == nil { phase = .notFound }
        }
    }

    func markAnswered(now: Date = .now) async {
        if await mutateAndReconcile({ try store.markAnswered(id: prayerId, now: now) }, now: now) {
            AccessibilityNotification.Announcement(String(localized: "Prayer marked as answered")).post()
        }
    }

    func markActive(now: Date = .now) async {
        if await mutateAndReconcile({ try store.markActive(id: prayerId, now: now) }, now: now) {
            AccessibilityNotification.Announcement(String(localized: "Prayer marked as active")).post()
        }
    }

    static func reminderSummary(
        _ record: PrayerRequestRecord, calendar: Calendar = .current, locale: Locale = .current
    ) -> String {
        guard record.canNotify else { return String(localized: "Off") }
        let clock = calendar.date(
            bySettingHour: record.notifyHour ?? 9, minute: record.notifyMinute ?? 0, second: 0, of: .now) ?? .now
        let time = clock.formatted(Date.FormatStyle(date: .omitted, time: .shortened, locale: locale, calendar: calendar, timeZone: calendar.timeZone))
        let stored = record.notifyDateTicks.flatMap { try? Date(ticks: $0) }
        func monthDay(_ date: Date) -> String {
            date.formatted(
                Date.VerbatimFormatStyle(
                    format: "\(day: .defaultDigits) \(month: .abbreviated)", locale: locale,
                    timeZone: calendar.timeZone, calendar: calendar))
        }
        switch record.prayerFrequency {
        case .daily:
            return String(localized: "Daily at \(time)")
        case .weekly:
            var localized = calendar
            localized.locale = locale
            let symbols = localized.weekdaySymbols
            let index = record.notifyDayOfWeek ?? 0
            let name = symbols.indices.contains(index) ? symbols[index] : ""
            return String(localized: "Weekly on \(name) at \(time)")
        case .monthly:
            let ordinal = NumberFormatter()
            ordinal.numberStyle = .ordinal
            ordinal.locale = locale
            let day = ordinal.string(from: NSNumber(value: record.notifyDayOfMonth ?? 1)) ?? ""
            return String(localized: "Monthly on the \(day) at \(time)")
        case .yearly:
            let date = stored.map(monthDay) ?? ""
            return String(localized: "Yearly on \(date) at \(time)")
        case .oneTime:
            let date = stored.map {
                $0.formatted(
                    Date.VerbatimFormatStyle(
                        format: "\(day: .defaultDigits) \(month: .abbreviated) \(year: .defaultDigits)",
                        locale: locale, timeZone: calendar.timeZone, calendar: calendar))
            } ?? ""
            return String(localized: "Once on \(date) at \(time)")
        }
    }

    func delete(now: Date = .now) async -> Bool {
        await mutateAndReconcile({ try store.delete(id: prayerId) }, now: now)
    }

    /// True when black text has the higher contrast on the tag color: the two tie at relative luminance 0.179.
    static func prefersDarkText(onTagHex hex: String?) -> Bool {
        let fallback = UserColorStore.defaultPaletteHexValues[0]
        let rgb = components(of: hex) ?? components(of: fallback) ?? (0, 0, 0)
        func linear(_ channel: Double) -> Double {
            channel <= 0.04045 ? channel / 12.92 : pow((channel + 0.055) / 1.055, 2.4)
        }
        let luminance = 0.2126 * linear(rgb.0) + 0.7152 * linear(rgb.1) + 0.0722 * linear(rgb.2)
        return luminance > 0.179
    }

    private static func components(of hex: String?) -> (Double, Double, Double)? {
        guard let hex else { return nil }
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard digits.count == 6, digits.allSatisfy(\.isASCII), let value = UInt32(digits, radix: 16) else {
            return nil
        }
        return (
            Double((value >> 16) & 0xFF) / 255, Double((value >> 8) & 0xFF) / 255, Double(value & 0xFF) / 255
        )
    }

    private func mutateAndReconcile(_ work: () throws -> Void, now: Date) async -> Bool {
        guard mutate(work) else { return false }
        do {
            try await reminders.reconcile(now: now)
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }

    @discardableResult
    private func mutate(_ work: () throws -> Void) -> Bool {
        guard !isMutating else { return false }
        isMutating = true
        defer { isMutating = false }
        do {
            try work()
            load()
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }
}
