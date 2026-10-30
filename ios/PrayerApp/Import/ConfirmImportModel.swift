import Foundation
import Observation
import PrayerKit

@Observable
final class ConfirmImportModel {
    let form: QuickAddModel
    private(set) var alreadyOnCard: [QuickAddModel.PrayerEntry] = []

    @ObservationIgnored private let resolver: ImportResolver
    @ObservationIgnored private let seedOrder: [UUID]

    init(database: PrayerDatabase, payload: SharePayload, now: Date = .now, locale: Locale = .current) {
        let shared: [SharedPrayer]
        switch payload {
        case .request(let prayer): shared = [prayer]
        case .card(_, let requests): shared = requests
        }
        let entries = shared.map {
            QuickAddModel.PrayerEntry(id: UUID(), title: $0.title, details: $0.notes ?? "")
        }
        let resolver = ImportResolver(database: database, locale: locale)
        self.resolver = resolver
        self.seedOrder = entries.map(\.id)
        self.form = QuickAddModel(
            database: database, prayers: entries,
            cardTitle: ImportResolver.defaultCardTitle(now: now, locale: locale),
            saver: { request, now in
                let target: ImportTarget
                var newCardTitle: String?
                var boxId: Int64 = 0
                switch request.target {
                case .newCard(let title, let box):
                    target = .newCard
                    newCardTitle = title
                    boxId = box
                case .existingCard(let id):
                    target = .existingCard(id: id)
                }
                let prayers = request.prayers.map { SharedPrayer(title: $0.title, notes: $0.details) }
                let resolution = ImportResolution(target: target, toImport: prayers)
                return try resolver.save(resolution, newCardTitle: newCardTitle, boxId: boxId, now: now)
                    .savedCount
            })
        form.onTargetChange = { [weak self] in self?.rematch() }
    }

    func addBack(id: UUID) {
        guard let index = alreadyOnCard.firstIndex(where: { $0.id == id }) else { return }
        form.prayers.append(alreadyOnCard.remove(at: index))
    }

    private func rematch() {
        let all = form.prayers + alreadyOnCard
        let byId = Dictionary(uniqueKeysWithValues: all.map { ($0.id, $0) })
        let seeded = seedOrder.compactMap { byId[$0] }
        let manual = all.filter { !seedOrder.contains($0.id) }
        let merged = seeded + manual
        guard form.addTo == .existingCard, let cardId = form.selectedCardId else {
            form.prayers = merged
            alreadyOnCard = []
            return
        }
        let probe = SharePayload.card(
            title: "", requests: merged.map { SharedPrayer(title: $0.title, notes: nil) })
        let duplicates = Set(
            resolver.resolve(probe, to: .existingCard(id: cardId)).alreadyOnCard.map(\.title))
        form.prayers = merged.filter { !duplicates.contains($0.title) }
        alreadyOnCard = merged.filter { duplicates.contains($0.title) }
    }
}
