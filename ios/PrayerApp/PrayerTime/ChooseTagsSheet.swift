import PrayerKit
import SwiftUI

struct ChooseTagsSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var loader = ScopeLoader<[PrayerTimeOption]>()
    @State private var selected = Set<Int64>()

    init(launcher: PrayerTimeLauncher, onStart: @escaping (PrayerTimeScope) -> Void) {
        self.launcher = launcher
        self.onStart = onStart
    }

    var body: some View {
        NavigationStack {
            ScopeLoadView(loader: loader, retry: { Task { await load() } }) { options in
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
            .navigationTitle("By Tags")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark") { dismiss() }
                        .accessibilityIdentifier("PrayerTimeScope_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Start") { onStart(.tags(selected)) }
                        .disabled(selected.isEmpty || !loader.isLoaded)
                        .accessibilityIdentifier("PrayerTimeScope_Start")
                }
            }
            .task { await load() }
        }
    }

    private func load() async {
        let launcher = launcher
        await loader.load { try launcher.tagOptions() }
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
