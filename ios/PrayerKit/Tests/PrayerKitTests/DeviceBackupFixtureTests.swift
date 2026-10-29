import Foundation
import GRDB
import Testing
@testable import PrayerKit

private let deviceBackupVariable = "PP_DEVICE_BACKUP_DB"

private let deviceBackupPath: String? = {
    guard let path = ProcessInfo.processInfo.environment[deviceBackupVariable],
          !path.isEmpty,
          FileManager.default.fileExists(atPath: path)
    else { return nil }
    return path
}()

// Local-only. Failure messages carry the table name and error type only, because
// decoding errors can include column values.
@Suite struct DeviceBackupFixtureTests {
    @Test(.enabled(if: deviceBackupPath != nil))
    func deviceBackupDecodesEveryRow() throws {
        guard let path = deviceBackupPath else { return }
        var config = Configuration()
        config.readonly = true
        let dbQueue: DatabaseQueue
        do {
            dbQueue = try DatabaseQueue(path: path, configuration: config)
        } catch {
            Issue.record("\(deviceBackupVariable): reading PrayerCard failed (\(type(of: error)))")
            return
        }
        defer { try? dbQueue.close() }

        func check<R: FetchableRecord & TableRecord>(_ table: String, _ record: R.Type) {
            do {
                _ = try dbQueue.read { try R.fetchAll($0) }.count
            } catch {
                Issue.record("\(deviceBackupVariable): reading \(table) failed (\(type(of: error)))")
            }
        }

        check("PrayerCard", PrayerCardRecord.self)
        check("PrayerRequest", PrayerRequestRecord.self)
        check("PrayerTag", PrayerTagRecord.self)
        check("PrayerCardTag", PrayerCardTagRecord.self)
        check("PrayerInteraction", PrayerInteractionRecord.self)
        check("UserColor", UserColorRecord.self)
        check("CardBox", CardBoxRecord.self)
    }
}
