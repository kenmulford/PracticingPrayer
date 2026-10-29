#if DEBUG
import Foundation
import OSLog
import PrayerKit
import SwiftUI

/// Upgrade-in-place gate for the 2.0.0 spike. Runs inside the app because the XCUITest runner
/// has its own Keychain groups and sandbox and cannot read the app's Keychain item or database.
struct UpgradeGateView: View {
    static let argument = "-upgradeGate"

    @State private var result = "running"

    var body: some View {
        Text(result)
            .accessibilityIdentifier("UpgradeGate_Result")
            .task {
                let arguments = ProcessInfo.processInfo.arguments
                let outcome = await Task.detached { Self.evaluate(arguments: arguments) }.value
                result = outcome.joined(separator: "\n")
                Self.log.debug("result label set, lines=\(outcome.count)")
            }
    }

    private nonisolated static let log = Logger(subsystem: "com.multithreadedllc.prayercards", category: "UpgradeGate")

    private nonisolated static func evaluate(arguments: [String]) -> [String] {
        log.debug("evaluate started, argument count=\(arguments.count)")
        guard let index = arguments.firstIndex(of: argument), arguments.indices.contains(index + 1) else {
            log.debug("launch argument or counts missing")
            return ["expected counts argument missing"]
        }
        var expected: [String: Int] = [:]
        for pair in arguments[index + 1].split(separator: ",") {
            let parts = pair.split(separator: "=")
            if parts.count == 2, let value = Int(parts[1]) { expected[String(parts[0])] = value }
        }
        log.debug("launch arguments parsed, expected tables=\(expected.count)")

        var failures: [String] = []

        do {
            let library = try FileManager.default.url(
                for: .libraryDirectory, in: .userDomainMask, appropriateFor: nil, create: false)
            let path = library.appendingPathComponent("prayer_app.db").path
            log.debug("database path resolved, file exists=\(FileManager.default.fileExists(atPath: path))")
            let database = try PrayerDatabase(path: path)
            log.debug("database opened")
            let actual = try database.rowCounts()
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
}
#endif
