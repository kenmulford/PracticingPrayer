import SwiftUI

struct RestoreSection: View {
    let flow: RestoreFlow?

    var body: some View {
        Section {
            Button("Restore from Backup\u{2026}", role: .destructive) { flow?.start() }
                .disabled(flow?.isBusy ?? true)
                .accessibilityIdentifier("backup.restore")
        } header: {
            Text("Restore").accessibilityAddTraits(.isHeader)
        } footer: {
            Text("Replace all current data with a previously exported backup. This cannot be undone.")
        }
    }
}

private struct RestorePresentation: ViewModifier {
    let flow: RestoreFlow?
    @State private var passphrase = ""

    func body(content: Content) -> some View {
        content
            .alert("Restore Backup?", isPresented: binding(for: .confirming)) {
                Button("Cancel", role: .cancel) { flow?.cancel() }
                Button("Restore", role: .destructive) { flow?.confirm() }
                    .accessibilityIdentifier("backup.restoreConfirm")
            } message: {
                Text("This will permanently replace all your current prayer data.")
            }
            .fileImporter(isPresented: binding(for: .picking), allowedContentTypes: [.pcrd]) { result in
                guard let flow else { return }
                Task { await flow.picked(result) }
            }
            .alert("Enter the backup passphrase", isPresented: promptingBinding) {
                SecureField("Passphrase", text: $passphrase)
                    .accessibilityIdentifier("backup.restorePassphraseField")
                Button("Cancel", role: .cancel) { submit(nil) }
                Button("Restore") { submit(passphrase) }
                    .accessibilityIdentifier("backup.restorePassphraseRestore")
            } message: {
                Text("This backup includes confidential cards protected by a passphrase. Enter it to restore them.")
            }
            .fullScreenCover(isPresented: restoringBinding, onDismiss: { flow?.overlayDismissed() }) {
                RestoreOverlay()
            }
            .alert("Restore Failed", isPresented: failureBinding) {
                Button("OK") { flow?.acknowledgeFailure() }
            } message: {
                Text(failureMessage)
            }
    }

    private func submit(_ entered: String?) {
        passphrase = ""
        guard let flow else { return }
        Task { await flow.restore(passphrase: entered) }
    }

    private func binding(for phase: RestoreFlow.Phase) -> Binding<Bool> {
        Binding(
            get: { flow?.phase == phase },
            set: { if !$0, flow?.phase == phase { flow?.cancel() } })
    }

    private var promptingBinding: Binding<Bool> {
        Binding(get: { flow?.phase == .promptingPassphrase }, set: { _ in })
    }

    private var restoringBinding: Binding<Bool> {
        Binding(get: { flow?.phase == .restoring }, set: { _ in })
    }

    private var failureBinding: Binding<Bool> {
        Binding(get: { flow?.failure != nil }, set: { if !$0 { flow?.acknowledgeFailure() } })
    }

    private var failureMessage: LocalizedStringKey {
        flow?.failure == .afterSwap
            ? "Restore failed. Please restart the app."
            : "This file doesn't appear to be a valid Practicing Prayer backup."
    }
}

extension View {
    func restorePresentation(_ flow: RestoreFlow?) -> some View {
        modifier(RestorePresentation(flow: flow))
    }
}
