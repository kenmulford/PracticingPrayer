import Foundation
import Observation
import PrayerKit
import SwiftUI

@Observable
final class PrayerDetailModel {
    enum Phase: Equatable { case loading, loaded, notFound }

    @ObservationIgnored private let prayerId: Int64
    @ObservationIgnored private let database: PrayerDatabase

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

    init(prayerId: Int64, database: PrayerDatabase) {
        self.prayerId = prayerId
        self.database = database
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

    func markAnswered(now: Date = .now) {
        if mutate({ try store.markAnswered(id: prayerId, now: now) }) {
            AccessibilityNotification.Announcement(String(localized: "Prayer marked as answered")).post()
        }
    }

    func markActive(now: Date = .now) {
        if mutate({ try store.markActive(id: prayerId, now: now) }) {
            AccessibilityNotification.Announcement(String(localized: "Prayer marked as active")).post()
        }
    }

    func delete() -> Bool {
        mutate { try store.delete(id: prayerId) }
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
