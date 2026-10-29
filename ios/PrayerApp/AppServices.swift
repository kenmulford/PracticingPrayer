import Foundation
import Observation
import PrayerKit

@Observable
final class AppServices {
    enum Phase: Equatable { case opening, open, failed }

    private(set) var phase: Phase = .opening
    let preferences: AppPreferences

    @ObservationIgnored private var openedDatabase: PrayerDatabase?
    @ObservationIgnored private let databasePath: String

    var database: PrayerDatabase {
        guard phase == .open, let openedDatabase else {
            preconditionFailure("AppServices.database read before the database opened")
        }
        return openedDatabase
    }

    init(preferences: AppPreferences = AppPreferences(), databasePath: String = AppServices.defaultDatabasePath) {
        self.preferences = preferences
        self.databasePath = databasePath
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
