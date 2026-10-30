import PrayerKit
import SwiftUI

struct ChooseCardsSheet: View {
    let launcher: PrayerTimeLauncher
    let onStart: (PrayerTimeScope) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var searchText = ""
    @State private var loader = ScopeLoader<[PrayerTimeSection]>()
    @State private var selected = Set<Int64>()

    init(launcher: PrayerTimeLauncher, onStart: @escaping (PrayerTimeScope) -> Void) {
        self.launcher = launcher
        self.onStart = onStart
    }

    var body: some View {
        NavigationStack {
            ScopeLoadView(loader: loader, retry: { Task { await load() } }) { list($0) }
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
        await loader.load { try launcher.eligibleCards(search: search) }
    }
}
