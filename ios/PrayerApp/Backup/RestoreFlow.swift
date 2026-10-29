import Foundation
import Observation
import PrayerKit
import SwiftUI

@Observable
final class RestoreFlow {
    enum Phase: Equatable { case idle, confirming, picking, promptingPassphrase, restoring }
    enum Failure: Equatable { case invalidBackup, afterSwap }

    private(set) var phase: Phase = .idle
    var failure: Failure?

    @ObservationIgnored private let services: AppServices
    @ObservationIgnored private let importDirectory: URL
    @ObservationIgnored private var stagedArchive: URL?
    @ObservationIgnored private var confidentialBlob: Data?
    @ObservationIgnored private var outcome: Failure?
    @ObservationIgnored private var didRestore = false

    init(services: AppServices, importDirectory: URL = URL.temporaryDirectory) {
        self.services = services
        self.importDirectory = importDirectory
    }

    var isBusy: Bool { phase != .idle }

    func start() {
        phase = .confirming
    }

    func confirm() {
        #if DEBUG
        if let path = ProcessInfo.processInfo.environment["PRAYER_RESTORE_IMPORT_URL"] {
            phase = .picking
            Task { await picked(.success(URL(fileURLWithPath: path))) }
            return
        }
        #endif
        phase = .picking
    }

    func cancel() {
        discardStaged()
        phase = .idle
    }

    func picked(_ result: Result<URL, any Error>) async {
        guard case .success(let url) = result else {
            phase = .idle
            if case .failure(let error) = result, (error as? CocoaError)?.code != .userCancelled {
                failure = .invalidBackup
            }
            return
        }
        phase = .idle
        let directory = importDirectory
        do {
            let (staged, blob) = try await Task.detached {
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
                let staged = directory.appendingPathComponent("restore-import.pcrd")
                try? FileManager.default.removeItem(at: staged)
                try FileManager.default.copyItem(at: url, to: staged)
                return (staged, try BackupArchive.inspect(archiveURL: staged).confidentialPayload)
            }.value
            stagedArchive = staged
            confidentialBlob = blob
            if blob == nil {
                await restore(passphrase: nil)
            } else {
                phase = .promptingPassphrase
            }
        } catch {
            discardStaged()
            failure = .invalidBackup
        }
    }

    func restore(passphrase: String?) async {
        guard let archive = stagedArchive else { phase = .idle; return }
        phase = .restoring
        AccessibilityNotification.Announcement(String(localized: "Restoring")).post()
        var payload: ConfidentialExportPayload?
        if let passphrase, let blob = confidentialBlob {
            payload = try? await Task.detached { try ConfidentialBackupImporter.decode(blob, passphrase: passphrase) }.value
        }
        let confidential = payload
        do {
            try await services.reopenDatabase { url in
                _ = try BackupArchive.restore(archiveURL: archive, databaseURL: url)
                guard let confidential else { return }
                do {
                    let database = try PrayerDatabase(path: url.path)
                    defer { try? database.close() }
                    try ConfidentialBackupImporter.insert(confidential, into: database)
                } catch {
                    ReminderScheduler.logError("Confidential import failed: \(error.localizedDescription)")
                }
            }
            didRestore = true
        } catch {
            outcome = .afterSwap
        }
        discardStaged()
        phase = .idle
    }

    func overlayDismissed() {
        if didRestore {
            didRestore = false
            services.didReplaceDatabase()
            services.reconcileReminders()
            AccessibilityNotification.Announcement(String(localized: "Restore complete")).post()
        } else if let pending = outcome {
            outcome = nil
            failure = pending
        }
    }

    func acknowledgeFailure() {
        if failure == .afterSwap { services.didReplaceDatabase() }
        failure = nil
    }

    private func discardStaged() {
        if let stagedArchive { try? FileManager.default.removeItem(at: stagedArchive) }
        stagedArchive = nil
        confidentialBlob = nil
    }
}
