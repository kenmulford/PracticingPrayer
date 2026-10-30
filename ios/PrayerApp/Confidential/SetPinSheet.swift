import SwiftUI

struct SetPinSheet: View {
    let flow: PinFallbackFlow
    @State private var pin = ""
    @State private var confirm = ""

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    SecureField("PIN", text: $pin)
                        .accessibilityIdentifier("SetPin_Pin")
                    SecureField("Confirm PIN", text: $confirm)
                        .accessibilityIdentifier("SetPin_Confirm")
                } footer: {
                    if PinFallbackFlow.isMismatch(pin: pin, confirm: confirm) {
                        Text("PINs don't match.").font(.footnote)
                    } else if let text = flow.messageText {
                        Text(text).font(.footnote)
                    }
                }
            }
            .keyboardType(.numberPad)
            .onChange(of: pin) { _, new in
                let clean = PinFallbackFlow.sanitized(new)
                if clean != new { pin = clean }
            }
            .onChange(of: confirm) { _, new in
                let clean = PinFallbackFlow.sanitized(new)
                if clean != new { confirm = clean }
            }
            .navigationTitle("Set PIN")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark", role: .cancel) { flow.cancel() }
                        .accessibilityIdentifier("SetPin_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", systemImage: "checkmark", role: .confirm) {
                        Task { await flow.save(pin: pin) }
                    }
                    .disabled(!PinFallbackFlow.canSave(pin: pin, confirm: confirm))
                    .accessibilityIdentifier("SetPin_Save")
                }
            }
        }
    }
}
