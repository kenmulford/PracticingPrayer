import PrayerKit
import SwiftUI

struct ChooseCardsSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var searchText = ""
    @State private var state = ScopeLoadState<[PrayerTimeSection]>.loading
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
                case .loaded(let sections):
                    list(sections)
                }
            }
            .navigationTitle("Choose Cards")
            .navigationBarTitleDisplayMode(.inline)
            .searchable(text: $searchText, prompt: "Search Cards")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel", systemImage: "xmark") { dismiss() }
                        .accessibilityIdentifier("PrayerTimeScope_Cancel")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button { onStart(.cards(selected)) } label: {
                        Text("Start (\(selected.count))").monospacedDigit()
                    }
                    .disabled(selected.isEmpty)
                    .accessibilityIdentifier("PrayerTimeScope_Start")
                }
            }
            .task(id: searchText) { await load() }
        }
    }

    private func list(_ sections: [PrayerTimeSection]) -> some View {
        List(selection: $selected) {
            ForEach(sections, id: \.boxId) { section in
                Section {
                    ForEach(section.cards, id: \.card.id) { item in
                        HStack {
                            Text(item.card.title)
                            Spacer()
                            Text(item.activePrayerCount ?? 0, format: .number)
                                .foregroundStyle(.secondary)
                                .monospacedDigit()
                        }
                        .accessibilityElement(children: .combine)
                    }
                } header: {
                    if section.boxId == 0 {
                        Text("Loose Cards")
                    } else {
                        Text(section.box?.name ?? "")
                    }
                }
            }
        }
        .environment(\.editMode, .constant(.active))
        .accessibilityIdentifier("PrayerTimeScope_CardsList")
        .overlay {
            if sections.isEmpty, !searchText.isEmpty {
                ContentUnavailableView.search(text: searchText)
            } else if sections.isEmpty {
                ContentUnavailableView("No Cards", systemImage: "rectangle.stack")
            }
        }
    }

    private func load() async {
        let launcher = launcher
        let search = searchText
        let result = await Task.detached { try? launcher.eligibleCards(search: search) }.value
        guard !Task.isCancelled else { return }
        state = result.map { .loaded($0) } ?? .failed
    }
}
