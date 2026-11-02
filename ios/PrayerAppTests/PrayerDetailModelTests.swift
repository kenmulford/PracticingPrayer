import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(
    title: String, tagNames: [String] = [], interactions: Int = 0,
    _ body: (PrayerDetailModel, PrayerDatabase, Int64, RecordingReconciler) async throws -> Void
) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("PrayerDetailModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    var tagIds: [Int64] = []
    for name in tagNames {
        tagIds.append(try TagStore(database: database).createTag(name: name, color: "#336699", now: .now).id)
    }
    let store = PrayerStore(database: database)
    let id = try store.create(PrayerDraft(prayerCardId: 2, title: title, tagIds: tagIds), now: .now)
    for _ in 0..<interactions {
        _ = try PrayerInteractionRecorder(database: database).recordPrayed(prayerId: id, now: .now)
    }
    let reconciler = RecordingReconciler()
    let model = PrayerDetailModel(prayerId: id, database: database, reminders: reconciler)
    model.load()
    try await body(model, database, id, reconciler)
}

@MainActor
@Suite struct PrayerDetailModelTests {
    @Test func reloadAfterRelockHidesProtectedPrayer() async throws {
        try await withModel(title: "reloadAfterRelockHidesProtectedPrayer") { _, database, _, reconciler in
            let card = try CardMutations(database: database, preferences: AppPreferences(defaults: UserDefaults(suiteName: "PrayerDetailModelTests-\(UUID().uuidString)")!))
                .create(CardDraft(title: "Hidden reloadAfterRelock", protectionMode: .hidden), now: .now)
            let id = try PrayerStore(database: database)
                .create(PrayerDraft(prayerCardId: card.id, title: "secret"), now: .now)
            var unlocked = true
            let model = PrayerDetailModel(
                prayerId: id, database: database, reminders: reconciler, isSessionUnlocked: { unlocked })
            model.load()
            #expect(model.phase == .loaded)
            unlocked = false
            model.load()
            #expect(model.phase == .notFound)
            #expect(model.prayer == nil)
        }
    }

    @Test func loadShowsPrayer() async throws {
        try await withModel(title: "loadShowsPrayer") { model, database, _, _ in
            let cardTitle = try database.cardList(isSessionUnlocked: false).sections
                .flatMap(\.cards).first { $0.card.id == 2 }?.card.title
            #expect(model.phase == .loaded)
            #expect(model.prayer?.title == "loadShowsPrayer")
            #expect(model.cardTitle == cardTitle)
        }
    }

    @Test func missingIdIsNotFound() async throws {
        try await withModel(title: "missingIdIsNotFound") { _, database, _, _ in
            let model = PrayerDetailModel(prayerId: 999_999, database: database, reminders: RecordingReconciler())
            model.load()
            #expect(model.phase == .notFound)
            #expect(model.prayer == nil)
        }
    }

    @Test func prayedSummaryHiddenAtZero() async throws {
        try await withModel(title: "prayedSummaryHiddenAtZero") { model, _, _, _ in
            #expect(model.prayedSummary == nil)
        }
    }

    @Test func prayedSummarySingular() async throws {
        try await withModel(title: "prayedSummarySingular", interactions: 1) { model, _, _, _ in
            #expect(model.prayedSummary?.hasPrefix("1 time since ") == true)
        }
    }

    @Test func prayedSummaryPlural() async throws {
        try await withModel(title: "prayedSummaryPlural", interactions: 3) { model, _, _, _ in
            #expect(model.prayedSummary?.hasPrefix("3 times since ") == true)
        }
    }

    @Test func markAnsweredStampsToday() async throws {
        try await withModel(title: "markAnsweredStampsToday") { model, _, _, _ in
            let now = Date.now
            await model.markAnswered(now: now)
            #expect(model.prayer?.isAnswered == true)
            #expect(model.prayer?.answeredAtTicks == Calendar.current.startOfDay(for: now).ticks)
        }
    }

    @Test func markActiveClearsAnswered() async throws {
        try await withModel(title: "markActiveClearsAnswered") { model, _, _, _ in
            await model.markAnswered()
            await model.markActive()
            #expect(model.prayer?.answeredAtTicks == nil)
            #expect(model.prayer?.isAnswered == false)
        }
    }

    @Test func markAnsweredReconciles() async throws {
        try await withModel(title: "markAnsweredReconciles") { model, _, _, reconciler in
            await model.markAnswered()
            #expect(reconciler.count == 1)
        }
    }

    @Test func reminderSummaryWordings() {
        let locale = Locale(identifier: "en_US")
        var calendar = Calendar(identifier: .gregorian)
        calendar.locale = locale
        let march5 = calendar.date(from: DateComponents(year: 2027, month: 3, day: 5)) ?? .now
        let expectedTime = (calendar.date(bySettingHour: 9, minute: 0, second: 0, of: march5) ?? march5)
            .formatted(Date.FormatStyle(date: .omitted, time: .shortened, locale: locale, calendar: calendar, timeZone: calendar.timeZone))

        func summary(
            _ frequency: PrayerFrequency, canNotify: Bool = true, dayOfWeek: Int? = nil, dayOfMonth: Int? = nil,
            dateTicks: Int64? = nil
        ) -> String {
            let record = PrayerRequestRecord(
                id: 1, prayerCardId: 1, title: "t", details: nil, canNotify: canNotify, prayerFrequency: frequency,
                isAnswered: false, answeredAtTicks: nil, notifyHour: 9, notifyMinute: 0,
                notifyDayOfWeek: dayOfWeek, notifyDayOfMonth: dayOfMonth, notifyDateTicks: dateTicks,
                isImported: nil, createdAtTicks: 0, updatedAtTicks: 0)
            return PrayerDetailModel.reminderSummary(record, calendar: calendar, locale: locale)
        }

        #expect(summary(.weekly, canNotify: false) == "Off")
        #expect(summary(.weekly, dayOfWeek: 0) == "Weekly on Sunday at \(expectedTime)")
        #expect(summary(.daily) == "Daily at \(expectedTime)")
        #expect(summary(.monthly, dayOfMonth: 3) == "Monthly on the 3rd at \(expectedTime)")
        #expect(summary(.yearly, dateTicks: march5.ticks) == "Yearly on 5 Mar at \(expectedTime)")
        #expect(summary(.oneTime, dateTicks: march5.ticks) == "Once on 5 Mar 2027 at \(expectedTime)")
    }

    @Test func deleteRemovesPrayer() async throws {
        try await withModel(title: "deleteRemovesPrayer") { model, _, _, reconciler in
            #expect(await model.delete())
            #expect(reconciler.count == 1)
            model.load()
            #expect(model.phase == .notFound)
        }
    }

    @Test func mutationOnMissingPrayerSetsError() async throws {
        try await withModel(title: "mutationOnMissingPrayerSetsError") { model, database, id, _ in
            try PrayerStore(database: database).delete(id: id)
            await model.markAnswered()
            #expect(model.errorMessage != nil)
            #expect(!model.isMutating)
        }
    }

    @Test func tagsLoadForPrayer() async throws {
        try await withModel(title: "tagsLoadForPrayer", tagNames: ["tagsLoadForPrayer"]) { model, _, _, _ in
            #expect(model.tags.map(\.name) == ["tagsLoadForPrayer"])
        }
    }

    @Test func tagTextContrast() {
        #expect(PrayerDetailModel.prefersDarkText(onTagHex: "#FFFF00"))
        #expect(!PrayerDetailModel.prefersDarkText(onTagHex: "#000080"))
    }
}
