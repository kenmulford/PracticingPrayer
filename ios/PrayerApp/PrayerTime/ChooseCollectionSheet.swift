import PrayerKit
import SwiftUI

struct ChooseCollectionSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var loader = ScopeLoader<[PrayerTimeOption]>()
    @State private var selected: Int64?

    init(launcher: PrayerTimeLauncher, onStart: @escaping (PrayerTimeScope) -> Void) {
        self.launcher = launcher
        self.onStart = onStart
    }

    var body: some View {
        NavigationStack {
            ScopeLoadView(loader: loader, retry: { Task { await load() } }) { options in
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
                    .disabled(selected == nil || !loader.isLoaded)
                    .accessibilityIdentifier("PrayerTimeScope_Start")
                }
            }
            .task { await load() }
        }
    }

    private func load() async {
        let launcher = launcher
        await loader.load { try launcher.collectionOptions() }
    }
}
