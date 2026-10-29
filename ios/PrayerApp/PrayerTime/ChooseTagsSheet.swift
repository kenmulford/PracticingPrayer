import PrayerKit
import SwiftUI

struct ChooseTagsSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var state = ScopeLoadState<[PrayerTimeOption]>.loading
    @State private var selected = Set<Int64>()

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
                        ContentUnavailableView("No Tags", systemImage: "tag")
                    } else {
                        List(options) { option in
                            ScopeOptionRow(option: option, isSelected: selected.contains(option.id)) {
                                if !selected.insert(option.id).inserted { selected.remove(option.id) }
                            }
                        }
                        .accessibilityIdentifier("PrayerTimeScope_TagsList")
                    }
                }
            }
            .navigationTitle("By Tags")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark") { dismiss() }
                        .accessibilityIdentifier("PrayerTimeScope_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Start") { onStart(.tags(selected)) }
                        .disabled(selected.isEmpty || !isLoaded)
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
        let result = await Task.detached { try? launcher.tagOptions() }.value
        state = result.map { .loaded($0) } ?? .failed
    }
}

struct ScopeOptionRow: View {
    let option: PrayerTimeOption
    let isSelected: Bool
    let action: () -> Void

    @ScaledMetric(relativeTo: .body) private var dot = 12

    var body: some View {
        Button(action: action) {
            HStack {
                if let color = option.color {
                    Image(systemName: "circle.fill")
                        .resizable()
                        .frame(width: dot, height: dot)
                        .foregroundStyle(Color.tag(color))
                        .accessibilityHidden(true)
                }
                Text(option.name)
                Spacer()
                Text(option.activeCount, format: .number)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
                if isSelected {
                    Image(systemName: "checkmark")
                        .accessibilityHidden(true)
                }
            }
        }
        .foregroundStyle(.primary)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}
