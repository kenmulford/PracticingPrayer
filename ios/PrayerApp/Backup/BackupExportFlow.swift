import Foundation
import Observation
import PrayerKit

@Observable
final class BackupExportFlow {
    enum Phase: Equatable { case idle, choosing, promptingPassphrase, exporting, sharing(URL) }

    private(set) var phase: Phase = .idle
    var showsFailure = false

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let exportDirectory: URL

    init(database: PrayerDatabase, exportDirectory: URL = BackupExportFlow.defaultExportDirectory) {
        self.database = database
        self.exportDirectory = exportDirectory
    }

    // BackupArchive.export deletes every .pcrd in its directory, so it gets a folder of its own.
    static var defaultExportDirectory: URL {
        URL.cachesDirectory.appending(path: "Backups")
    }

    var isBusy: Bool { phase != .idle }

    func backUpNow() async {
        phase = .exporting
        let database = database
        do {
            let hasConfidential = try await Task.detached { try BackupExporter.hasConfidentialCards(in: database) }.value
            if hasConfidential {
                phase = .choosing
            } else {
                await export(passphrase: nil)
            }
        } catch {
            fail()
        }
    }

    func chooseProtect() {
        phase = .promptingPassphrase
    }

    func continueUnencrypted() async {
        phase = .exporting
        await export(passphrase: nil, acknowledgingPlaintext: true)
    }

    func protect(passphrase: String) async {
        phase = .exporting
        await export(passphrase: passphrase)
    }

    func cancel() {
        phase = .idle
    }

    // The share sheet dismisses on completion and on failure; the file is a throwaway copy either way.
    func finishSharing() {
        if case .sharing(let url) = phase { try? FileManager.default.removeItem(at: url) }
        phase = .idle
    }

    private func export(passphrase: String?, acknowledgingPlaintext: Bool = false) async {
        let database = database
        let directory = exportDirectory
        do {
            let url = try await Task.detached {
                try BackupExporter.export(
                    database: database, passphrase: passphrase, acknowledgingPlaintext: acknowledgingPlaintext,
                    to: directory)
            }.value
            phase = .sharing(url)
        } catch BackupExporterError.confidentialCardsPresent {
            phase = .choosing
        } catch {
            fail()
        }
    }

    private func fail() {
        phase = .idle
        showsFailure = true
    }
}
