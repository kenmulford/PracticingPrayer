import PrayerKit
import SwiftUI

struct BackupRestoreView: View {
    @Environment(AppServices.self) private var services
    @State private var flow: BackupExportFlow?
    @State private var passphrase = ""

    var body: some View {
        Form {
            Section {
                Button("Back Up Now", systemImage: "arrow.up.doc") {
                    guard let flow else { return }
                    Task { await flow.backUpNow() }
                }
                .disabled(flow?.isBusy ?? true)
                .overlay {
                    if flow?.phase == .exporting { ProgressView() }
                }
                .accessibilityElement(children: .combine)
                .accessibilityIdentifier("backup.backUpNow")
            } header: {
                Text("Backup").accessibilityAddTraits(.isHeader)
            } footer: {
                Text("Export your prayer data to a file you can restore on another device.")
            }
        }
        .navigationTitle(Text("Backup & Restore"))
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if flow == nil { flow = BackupExportFlow(database: services.database) }
        }
        .confirmationDialog("This backup includes confidential cards", isPresented: choosingBinding, titleVisibility: .visible) {
            Button("Protect with a Passphrase") { flow?.chooseProtect() }
                .accessibilityIdentifier("backup.protectChoice")
            Button("Continue Unencrypted") {
                guard let flow else { return }
                Task { await flow.continueUnencrypted() }
            }
            .accessibilityIdentifier("backup.unencryptedChoice")
            Button("Cancel", role: .cancel) { flow?.cancel() }
        }
        .alert("Protect with a Passphrase", isPresented: promptingBinding) {
            SecureField("Passphrase (12+ characters)", text: $passphrase)
                .accessibilityLabel("Passphrase")
                .accessibilityIdentifier("backup.passphraseField")
            Button("Cancel", role: .cancel) {
                passphrase = ""
                flow?.cancel()
            }
            Button("Protect Backup") {
                let entered = passphrase
                passphrase = ""
                guard let flow else { return }
                Task { await flow.protect(passphrase: entered) }
            }
            .disabled(!PassphrasePolicy.meetsMinimumLength(passphrase))
            .accessibilityIdentifier("backup.protectBackup")
        } message: {
            Text("Choose a passphrase to encrypt your confidential cards in this backup. You'll need it to restore them later \u{2014} we can't recover it for you.")
        }
        .sheet(isPresented: sharingBinding, onDismiss: { flow?.finishSharing() }) {
            if case .sharing(let url)? = flow?.phase { ActivityView(url: url) }
        }
        .alert("Backup failed", isPresented: failureBinding) {
            Button("OK") {}
        }
    }

    private var choosingBinding: Binding<Bool> {
        Binding(
            get: { flow?.phase == .choosing },
            set: { if !$0, flow?.phase == .choosing { flow?.cancel() } })
    }

    private var promptingBinding: Binding<Bool> {
        Binding(get: { flow?.phase == .promptingPassphrase }, set: { _ in })
    }

    private var sharingBinding: Binding<Bool> {
        Binding(
            get: { if case .sharing? = flow?.phase { true } else { false } },
            set: { _ in })
    }

    private var failureBinding: Binding<Bool> {
        Binding(get: { flow?.showsFailure ?? false }, set: { flow?.showsFailure = $0 })
    }
}
