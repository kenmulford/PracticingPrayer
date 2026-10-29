import Foundation
import GRDB

public enum ImportResolverError: Error, Equatable {
    case lookupFailed
    case nothingToSave
    case writeFailed(savedCount: Int)
}

public struct ImportSaveResult: Sendable, Equatable {
    public let cardId: Int64
    public let savedCount: Int
}

public struct ImportResolver: Sendable {
    private let database: PrayerDatabase
    private let locale: Locale

    public init(database: PrayerDatabase, locale: Locale = .current) {
        self.database = database
        self.locale = locale
    }

    public func resolve(_ payload: SharePayload, to target: ImportTarget) -> ImportResolution {
        let prayers: [SharedPrayer]
        switch payload {
        case .request(let prayer): prayers = [prayer]
        case .card(_, let requests): prayers = requests
        }
        guard case .existingCard(let cardId) = target else {
            return ImportResolution(target: target, toImport: prayers)
        }
        let existing: [PrayerListItem]
        do {
            existing = try PrayerStore(database: database).prayers(cardId: cardId)
        } catch {
            return ImportResolution(target: target, toImport: prayers, lookupError: .lookupFailed)
        }
        let keys = Set(existing.map { Self.matchKey($0.record.title) })
        var fresh: [SharedPrayer] = []
        var duplicates: [SharedPrayer] = []
        for prayer in prayers {
            if keys.contains(Self.matchKey(prayer.title)) {
                duplicates.append(prayer)
            } else {
                fresh.append(prayer)
            }
        }
        return ImportResolution(target: target, toImport: fresh, alreadyOnCard: duplicates)
    }

    public func save(_ resolution: ImportResolution, newCardTitle: String? = nil, now: Date) throws
        -> ImportSaveResult
    {
        if resolution.hasNothingToSave { throw ImportResolverError.nothingToSave }
        let drafts = resolution.toImport.compactMap { prayer -> (title: String, details: String?)? in
            let title = TextNormalization.normalizeQuotes(prayer.title)
                .trimmingCharacters(in: .whitespacesAndNewlines)
            if title.isEmpty { return nil }
            let notes = prayer.notes.map {
                TextNormalization.normalizeQuotes($0).trimmingCharacters(in: .whitespacesAndNewlines)
            }
            return (title, notes?.isEmpty == false ? notes : nil)
        }
        let cardTitle = newCardTitle.map {
            TextNormalization.normalizeQuotes($0).trimmingCharacters(in: .whitespacesAndNewlines)
        }.flatMap { $0.isEmpty ? nil : $0 } ?? Self.defaultCardTitle(now: now, locale: locale)
        do {
            return try database.dbQueue.write { db in
                let ticks = now.ticks
                let cardId: Int64
                switch resolution.target {
                case .newCard:
                    cardId = try CardMutations.insertRow(
                        CardDraft(title: cardTitle, boxId: 0, isImported: true), ticks: ticks, db)
                case .existingCard(let id):
                    guard try PrayerCardRecord.exists(db, key: id) else {
                        throw PrayerStoreError.cardNotFound(id)
                    }
                    cardId = id
                }
                for draft in drafts {
                    _ = try PrayerStore.insertRow(
                        PrayerDraft(
                            prayerCardId: cardId, title: draft.title, details: draft.details, isImported: true),
                        ticks: ticks, db)
                }
                return ImportSaveResult(cardId: cardId, savedCount: drafts.count)
            }
        } catch {
            // The whole import is one transaction, so a failure persists nothing.
            throw ImportResolverError.writeFailed(savedCount: 0)
        }
    }

    static func matchKey(_ title: String) -> String {
        TextNormalization.collapseWhitespace(TextNormalization.normalizeQuotes(title)).lowercased()
    }

    static func defaultCardTitle(now: Date, locale: Locale = .current) -> String {
        let formatter = DateFormatter()
        formatter.locale = locale
        formatter.setLocalizedDateFormatFromTemplate("MMMd")
        return "Imported " + formatter.string(from: now)
    }
}
