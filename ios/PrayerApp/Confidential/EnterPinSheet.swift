import SwiftUI

struct EnterPinSheet: View {
    let flow: PinFallbackFlow
    @State private var pin = ""
    @FocusState private var focused: Bool

    private var isLocked: Bool {
        guard let until = flow.lockedUntil else { return false }
        return until > .now
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 16) { dots(0..<6) }
                    VStack(spacing: 16) {
                        HStack(spacing: 16) { dots(0..<3) }
                        HStack(spacing: 16) { dots(3..<6) }
                    }
                }
                .accessibilityHidden(true)
                .overlay {
                    SecureField("PIN", text: $pin)
                        .keyboardType(.numberPad)
                        .focused($focused)
                        .foregroundStyle(.clear)
                        .tint(.clear)
                        .disabled(isLocked)
                        .accessibilityValue("\(pin.count) of 6 digits entered")
                        .accessibilityIdentifier("EnterPin_Field")
                }
                status
            }
            .padding()
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .navigationTitle("Enter PIN")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark", role: .cancel) { flow.cancel() }
                        .accessibilityIdentifier("EnterPin_Cancel")
                }
            }
            .onAppear { focused = true }
            .onChange(of: pin) { _, new in
                let clean = PinFallbackFlow.sanitized(new)
                if clean != new { pin = clean }
                if clean.count == 6 {
                    Task {
                        await flow.submit(pin: clean)
                        pin = ""
                    }
                }
            }
            .task(id: flow.lockedUntil) {
                guard let until = flow.lockedUntil else { return }
                try? await Task.sleep(for: .seconds(max(0, until.timeIntervalSinceNow)))
                flow.refreshLockout()
                focused = true
            }
        }
    }

    private func dots(_ range: Range<Int>) -> some View {
        ForEach(range, id: \.self) { index in
            Image(systemName: index < pin.count ? "circle.fill" : "circle")
                .imageScale(.large)
        }
    }

    @ViewBuilder private var status: some View {
        if isLocked, let until = flow.lockedUntil {
            Text("Too many attempts. Try again in \(Text(timerInterval: .now...until, countsDown: true)).")
                .monospacedDigit()
                .font(.footnote)
        } else if let text = flow.messageText {
            Text(text).font(.footnote)
        }
    }
}
