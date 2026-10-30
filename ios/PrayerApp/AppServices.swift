import Foundation
import Observation
import PrayerKit

protocol ReminderReconciling {
    func reconcile(now: Date) async throws
}

extension ReminderScheduler: ReminderReconciling {}

@Observable
final class AppServices {
    enum Phase: Equatable { case opening, open, restoring, failed }

    private(set) var phase: Phase = .opening
    private(set) var databaseGeneration = 0
    let preferences: AppPreferences
    let onboarding: OnboardingCoordinator
    let inboundShare: InboundShareHandler
    let clipboardHandoff: ClipboardHandoffCoordinator
    let confidential: ConfidentialSession
    var prayerSession: PrayerSession?

    @ObservationIgnored private var openedDatabase: PrayerDatabase?
    @ObservationIgnored private let databasePath: String
    @ObservationIgnored private let reminderCenter: any ReminderCenter

    var database: PrayerDatabase {
        guard phase == .open || phase == .restoring, let openedDatabase else {
            preconditionFailure("AppServices.database read before the database opened")
        }
        return openedDatabase
    }

    init(preferences: AppPreferences = AppServices.defaultPreferences, databasePath: String = AppServices.defaultDatabasePath,
         inboundShare: InboundShareHandler = .shared, reminderCenter: any ReminderCenter = SystemReminderCenter()) {
        self.reminderCenter = reminderCenter
        self.preferences = preferences
        self.inboundShare = inboundShare
        clipboardHandoff = ClipboardHandoffCoordinator(
            preferences: preferences, pasteboard: Self.defaultPasteboard, inboundShare: inboundShare)
        onboarding = OnboardingCoordinator(preferences: preferences, isSuppressed: Self.suppressesOnboarding)
        self.databasePath = databasePath
        confidential = ConfidentialSession(authenticator: Self.defaultAuthenticator)
        let pinStore = Self.defaultPinStore
        let pinFlow = PinFallbackFlow(
            record: PinRecord(store: pinStore),
            tracker: PinAttemptTracker(store: PinAttemptStore(store: pinStore)),
            presenter: PinSheetWindow())
        confidential.pinFallback = { await pinFlow.run() }
    }

    static var defaultPinStore: any SecItemStore {
        #if DEBUG
        if ProcessInfo.processInfo.environment["CONFIDENTIAL_PIN_STORE"] == "memory" {
            return InMemorySecItemStore()
        }
        #endif
        return SystemSecItemStore()
    }

    static var defaultAuthenticator: any DeviceOwnerAuthenticating {
        #if DEBUG
        if let stub = StubDeviceOwnerAuthenticator(
            environmentValue: ProcessInfo.processInfo.environment["CONFIDENTIAL_AUTH_STUB"]) {
            return stub
        }
        #endif
        return SystemDeviceOwnerAuthenticator()
    }

    static var defaultPreferences: AppPreferences {
        #if DEBUG
        if let suite = ProcessInfo.processInfo.environment["PRAYER_DEFAULTS_SUITE"],
           let defaults = UserDefaults(suiteName: suite) {
            return AppPreferences(defaults: defaults)
        }
        #endif
        return AppPreferences()
    }

    // UI suites that point at a fixture database skip the tour unless they pass an onboarding argument.
    static var suppressesOnboarding: Bool {
        #if DEBUG
        let arguments = ProcessInfo.processInfo.arguments
        return ProcessInfo.processInfo.environment["PRAYER_DB_PATH"] != nil
            && !arguments.contains("-OnboardingStep") && !arguments.contains("-OnboardingComplete")
        #else
        return false
        #endif
    }

    static var defaultPasteboard: any PasteboardReading {
        #if DEBUG
        if let text = UserDefaults.standard.string(forKey: "StubClipboard") {
            return StubPasteboard(text: text)
        }
        if ProcessInfo.processInfo.environment["PRAYER_DB_PATH"] != nil {
            return StubPasteboard(text: "")
        }
        #endif
        return SystemPasteboard()
    }

    static var defaultDatabasePath: String {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["PRAYER_DB_PATH"] {
            return override
        }
        #endif
        return URL.libraryDirectory.appendingPathComponent("prayer_app.db").path
    }

    var reminders: any ReminderReconciling {
        ReminderScheduler(database: database, preferences: preferences, center: reminderCenter)
    }

    @discardableResult
    func reconcileReminders() -> Task<Void, Never> {
        let reminders = reminders
        return Task {
            do {
                try await reminders.reconcile(now: .now)
            } catch {
                ReminderScheduler.logError("Reminder reconcile failed: \(error.localizedDescription)")
            }
        }
    }

    func didReplaceDatabase() {
        databaseGeneration += 1
    }

    /// Closes the live connection, runs `swap` on the database file, then reopens it. Holds `.restoring` for the
    /// swap (the shell keeps its tabs and the restore overlay mounted, while every `.open` gate pauses) and never
    /// leaves `.opening`, so nothing queries the closed connection.
    func reopenDatabase<T: Sendable>(after swap: @escaping @Sendable (URL) throws -> T) async throws -> T {
        phase = .restoring
        let closing = openedDatabase
        let path = databasePath
        let (outcome, reopened) = await Task.detached {
            try? closing?.close()
            let outcome = Result { try swap(URL(fileURLWithPath: path)) }
            return (outcome, Result { try Self.openPrepared(path: path) })
        }.value
        switch reopened {
        case .success(let (database, archivedBoxId)):
            openedDatabase = database
            preferences.archivedFolderId = archivedBoxId
            phase = .open
        case .failure:
            openedDatabase = nil
            phase = .failed
        }
        return try outcome.get()
    }

    func open() async {
        phase = .opening
        let path = databasePath
        do {
            let (database, archivedBoxId) = try await Task.detached { try Self.openPrepared(path: path) }.value
            openedDatabase = database
            preferences.archivedFolderId = archivedBoxId
            phase = .open
            reconcileReminders()
        } catch {
            openedDatabase = nil
            phase = .failed
        }
    }

    /// Opens (creating a missing file on a fresh install), runs the 1.7.2 schema upkeep, and returns the
    /// Archived box id that `AppPreferences.archivedFolderId` mirrors. Blocking; call off the main thread.
    nonisolated static func openPrepared(path: String) throws -> (PrayerDatabase, Int) {
        let database = try PrayerDatabase.openOrCreate(path: path)
        do {
            try SchemaUpkeep.run(on: database)
            let now = Date()
            try TagStore(database: database).seedSystemTags(now: now)
            try UserColorStore(database: database).seedDefaults(now: now)
            return (database, Int(try SchemaUpkeep.archivedBoxId(on: database) ?? 0))
        } catch {
            try? database.close()
            throw error
        }
    }
}
