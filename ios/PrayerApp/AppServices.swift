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
    let preferences: AppPreferences
    let onboarding: OnboardingCoordinator
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
