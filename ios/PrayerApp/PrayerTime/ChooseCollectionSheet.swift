import PrayerKit
import SwiftUI

struct ChooseCollectionSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var state = ScopeLoadState<[PrayerTimeOption]>.loading
    @State private var selected: Int64?

    init(launcher: PrayerTimeLauncher, onStart: @escaping (PrayerTimeScope) -> Void) {
        self.launcher = launcher
        self.onStart = onStart
    }

    var body: some View {
        NavigationStack {
            Group {
                switch state {
                case .loading:
                    ProgressView()
                case .failed:
                    ContentUnavailableView {
                        Label("Unable to Load", systemImage: "exclamationmark.triangle")
                    } actions: {
                        Button("Retry") { Task { await load() } }
                    }
                case .loaded(let options):
                    if options.isEmpty {
                        ContentUnavailableView("No Collections", systemImage: "rectangle.stack")
                    } else {
                        List(options) { option in
                            ScopeOptionRow(option: option, isSelected: selected == option.id) {
                                selected = option.id
                            }
                        }
                        .accessibilityIdentifier("PrayerTimeScope_CollectionList")
                    }
                }
            }
            .navigationTitle("By Collection")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark") { dismiss() }
                        .accessibilityIdentifier("PrayerTimeScope_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Start") {
                        if let selected { onStart(.collection(selected)) }
                    }
                    .disabled(selected == nil || !isLoaded)
                    .accessibilityIdentifier("PrayerTimeScope_Start")
                }
            }
            .task { await load() }
        }
    }

    private var isLoaded: Bool {
        if case .loaded = state { return true }
        return false
    }

    private func load() async {
        let launcher = launcher
        let result = await Task.detached { try? launcher.collectionOptions() }.value
        state = result.map { .loaded($0) } ?? .failed
    }
}
