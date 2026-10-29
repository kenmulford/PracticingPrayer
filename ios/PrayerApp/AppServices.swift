import Foundation
import Observation
import PrayerKit

protocol ReminderReconciling {
    func reconcile(now: Date) async throws
}

extension ReminderScheduler: ReminderReconciling {}

@Observable
final class AppServices {
    enum Phase: Equatable { case opening, open, failed }

    private(set) var phase: Phase = .opening
    private(set) var databaseGeneration = 0
    let preferences: AppPreferences
    let onboarding: OnboardingCoordinator
    let confidential: ConfidentialSession
    var prayerSession: PrayerSession?

    @ObservationIgnored private var openedDatabase: PrayerDatabase?
    @ObservationIgnored private let databasePath: String

    var database: PrayerDatabase {
        guard phase == .open, let openedDatabase else {
            preconditionFailure("AppServices.database read before the database opened")
        }
        return openedDatabase
    }

    init(preferences: AppPreferences = AppServices.defaultPreferences, databasePath: String = AppServices.defaultDatabasePath) {
        self.preferences = preferences
        onboarding = OnboardingCoordinator(preferences: preferences, isSuppressed: Self.suppressesOnboarding)
        self.databasePath = databasePath
        confidential = ConfidentialSession(authenticator: Self.defaultAuthenticator)
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

    static var defaultDatabasePath: String {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["PRAYER_DB_PATH"] {
            return override
        }
        #endif
        return URL.libraryDirectory.appendingPathComponent("prayer_app.db").path
    }

    var reminders: any ReminderReconciling {
        ReminderScheduler(database: database, preferences: preferences, center: SystemReminderCenter())
    }

    func reconcileReminders() {
        let reminders = reminders
        Task {
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

    /// Closes the live connection, runs `swap` on the database file, then reopens it. Never leaves `.opening`,
    /// so the shell keeps its tabs (and the restore overlay) mounted until the caller rebuilds them.
    func reopenDatabase<T: Sendable>(after swap: @escaping @Sendable (URL) throws -> T) async throws -> T {
        let closing = openedDatabase
        let path = databasePath
        let (outcome, reopened) = await Task.detached {
            try? closing?.close()
            let outcome = Result { try swap(URL(fileURLWithPath: path)) }
            return (outcome, Result { try PrayerDatabase(path: path) })
        }.value
        switch reopened {
        case .success(let database):
            openedDatabase = database
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
            openedDatabase = try await Task.detached { try PrayerDatabase(path: path) }.value
            phase = .open
        } catch {
            openedDatabase = nil
            phase = .failed
        }
    }
}
