#if DEBUG
import Foundation
import OSLog
import PrayerKit
import SwiftUI
import UserNotifications

/// Upgrade-in-place gate for the 2.0.0 spike. Runs inside the app because the XCUITest runner
/// has its own Keychain groups and sandbox and cannot read the app's Keychain item or database.
struct UpgradeGateView: View {
    nonisolated static let argument = "-upgradeGate"

    @State private var result = "running"

    var body: some View {
        Text(result)
            .accessibilityIdentifier("UpgradeGate_Result")
            .task {
                let arguments = ProcessInfo.processInfo.arguments
                let outcome = await Task.detached { await Self.evaluate(arguments: arguments) }.value
                result = outcome.joined(separator: "\n")
                Self.log.debug("result label set, lines=\(outcome.count)")
            }
    }

    private nonisolated static let log = Logger(subsystem: "com.multithreadedllc.prayercards", category: "UpgradeGate")

    private nonisolated static func evaluate(arguments: [String]) async -> [String] {
        log.debug("evaluate started, argument count=\(arguments.count)")
        guard let index = arguments.firstIndex(of: argument), arguments.indices.contains(index + 1) else {
            log.debug("launch argument or counts missing")
            return ["expected counts argument missing"]
        }
        let expected = UpgradeGateChecks.counts(arguments[index + 1])
        log.debug("launch arguments parsed, expected tables=\(expected.count)")

        var failures: [String] = []
        var database: PrayerDatabase?

        do {
            let library = try FileManager.default.url(
                for: .libraryDirectory, in: .userDomainMask, appropriateFor: nil, create: false)
            let path = library.appendingPathComponent("prayer_app.db").path
            log.debug("database path resolved, file exists=\(FileManager.default.fileExists(atPath: path))")
            let opened = try PrayerDatabase(path: path)
            database = opened
            log.debug("database opened")
            let actual = try opened.rowCounts()
            let checks: [(String, Int)] = [
                ("PrayerCard", actual.cards), ("PrayerRequest", actual.prayers),
                ("PrayerTag", actual.tags), ("CardBox", actual.collections),
            ]
            for (table, count) in checks {
                log.debug("counted \(table, privacy: .public)=\(count), matches expected=\(expected[table] == count)")
                if expected[table] != count {
                    failures.append("\(table) expected \(expected[table].map(String.init) ?? "missing") actual \(count)")
                }
            }
        } catch {
            log.debug("database step failed")
            failures.append("database error \(error)")
        }

        failures += await reminderFailures(arguments: arguments, database: database)

        do {
            let record = try PinRecord().read()
            log.debug("keychain read, record found=\(record != nil)")
            let correct = PinHasher.verify("135790", storedRecord: record)
            let wrong = PinHasher.verify("135791", storedRecord: record)
            log.debug("pin verify, correct=\(correct) wrong=\(wrong)")
            if !correct { failures.append("PIN did not verify") }
            if wrong { failures.append("wrong PIN verified") }
        } catch PinRecordError.keychain(let status) {
            log.debug("keychain read failed, status=\(status)")
            failures.append("keychain error \(status)")
        } catch {
            log.debug("keychain read failed")
            failures.append("keychain error \(error)")
        }

        return failures.isEmpty ? ["PASS"] : failures
    }

    private nonisolated static func reminderFailures(arguments: [String], database: PrayerDatabase?) async -> [String] {
        let preferences = AppPreferences()
        var failures = UpgradeGateChecks.settingFailures(preferences)
        guard let index = arguments.firstIndex(of: UpgradeGateChecks.remindersArgument),
              arguments.indices.contains(index + 1)
        else { return failures + ["expected reminders argument missing"] }
        guard let database else { return failures + ["reminder check skipped, database unavailable"] }
        let parsed = UpgradeGateChecks.reminders(arguments[index + 1])
        failures += parsed.failures
        let expected = parsed.rows
        do {
            let records = try await database.dbQueue.read { db in
                try PrayerRequestRecord.fetchAll(db, keys: expected.map(\.id))
            }
            failures += UpgradeGateChecks.reminderFailures(expected: expected, actual: records.map(UpgradeGateChecks.reminder))
            let derived = try await UpgradeGateChecks.derivedIdentifiers(database: database, preferences: preferences)
            let pending = await UNUserNotificationCenter.current().pendingNotificationRequests()
            if let diff = UpgradeGateChecks.identifierDiff(expected: derived, actual: Set(pending.map(\.identifier))) {
                failures.append(diff)
            }
        } catch {
            failures.append("reminder check error \(error)")
        }
        return failures
    }
}
#endif
