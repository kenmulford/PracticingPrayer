import Foundation
import Observation
import PrayerKit

@Observable
final class AppServices {
    enum Phase: Equatable { case opening, open, failed }

    private(set) var phase: Phase = .opening
    let preferences: AppPreferences
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

    static var defaultDatabasePath: String {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["PRAYER_DB_PATH"] {
            return override
        }
        #endif
        return URL.libraryDirectory.appendingPathComponent("prayer_app.db").path
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
