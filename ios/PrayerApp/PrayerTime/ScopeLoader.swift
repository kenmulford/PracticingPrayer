import Observation
import SwiftUI

/// Loads a Choose sheet's options off the main actor; the failure message is shown, never swallowed.
@MainActor
@Observable
final class ScopeLoader<Value: Sendable> {
    enum State {
        case loading
        case loaded(Value)
        case failed(String)
    }

    private(set) var state = State.loading

    var isLoaded: Bool {
        if case .loaded = state { return true }
        return false
    }

    func load(_ work: @escaping @Sendable () throws -> Value) async {
        let result = await Task.detached { Result { try work() } }.value
        guard !Task.isCancelled else { return }
        switch result {
        case .success(let value): state = .loaded(value)
        case .failure(let error): state = .failed(error.localizedDescription)
        }
    }
}

/// Shared loading and failure presentation for the Choose sheets.
struct ScopeLoadView<Value: Sendable, Loaded: View>: View {
    let loader: ScopeLoader<Value>
    let retry: () -> Void
    @ViewBuilder let loaded: (Value) -> Loaded

    var body: some View {
        switch loader.state {
        case .loading:
            ProgressView()
        case .failed(let message):
            ContentUnavailableView {
                Label("Unable to Load", systemImage: "exclamationmark.triangle")
            } description: {
                Text(message)
            } actions: {
                Button("Retry", action: retry)
            }
        case .loaded(let value):
            loaded(value)
        }
    }
}
