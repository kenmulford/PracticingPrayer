import PrayerKit
import SwiftUI

struct CardPickerView: View {
    private enum LoadState {
        case loading
        case loaded([CardSection])
        case failed
    }

    let database: PrayerDatabase
    let selectedCardId: Int64?
    let onSelect: (Int64, String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var searchText = ""
    @State private var state = LoadState.loading

    init(database: PrayerDatabase, selectedCardId: Int64?, onSelect: @escaping (Int64, String) -> Void) {
        self.database = database
        self.selectedCardId = selectedCardId
        self.onSelect = onSelect
    }

    var body: some View {
        Group {
            switch state {
            case .loading:
                ProgressView()
            case .failed:
                ContentUnavailableView("Can't Load Cards", systemImage: "exclamationmark.triangle")
            case .loaded(let sections):
                list(sections)
            }
        }
        .navigationTitle("Select Card")
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $searchText, prompt: "Search Cards")
        .task(id: searchText) { await load() }
    }

    private func list(_ sections: [CardSection]) -> some View {
        let visible = sections.filter { !$0.cards.isEmpty }
        return List {
            ForEach(visible, id: \.boxId) { section in
                Section {
                    ForEach(section.cards, id: \.card.id) { item in
                        row(item)
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
        .accessibilityIdentifier("CardPicker_List")
        .overlay {
            if visible.isEmpty, !searchText.isEmpty {
                ContentUnavailableView.search(text: searchText)
            }
        }
    }

    private func row(_ item: CardListItem) -> some View {
        Button {
            onSelect(item.card.id, item.card.title)
            dismiss()
        } label: {
            HStack {
                Text(item.card.title)
                Spacer()
                if item.card.id == selectedCardId {
                    Image(systemName: "checkmark")
                        .accessibilityHidden(true)
                }
            }
        }
        .foregroundStyle(.primary)
        .disabled(item.isMasked)
        .accessibilityAddTraits(item.card.id == selectedCardId ? .isSelected : [])
    }

    private func load() async {
        let database = database
        let filter = CardListFilter(searchText: searchText)
        let result = await Task.detached {
            try? database.cardList(filter: filter, isSessionUnlocked: false)
        }.value
        guard !Task.isCancelled else { return }
        state = result.map { .loaded($0.sections) } ?? .failed
    }
}
