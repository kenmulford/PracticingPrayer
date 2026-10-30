import PrayerKit
import SwiftUI

struct ConfirmImportView: View {
    @State private var model: ConfirmImportModel
    private let database: PrayerDatabase

    init(database: PrayerDatabase, payload: SharePayload, cardTitle: String? = nil) {
        self.database = database
        self.model = ConfirmImportModel(database: database, payload: payload, cardTitle: cardTitle)
    }

    var body: some View {
        QuickAddView(database: database, model: model.form, title: "Confirm Import") {
            duplicateSections
        }
    }

    @ViewBuilder private var duplicateSections: some View {
        if model.form.addTo == .existingCard, !model.alreadyOnCard.isEmpty {
            if model.form.prayers.isEmpty {
                Section {
                    Text("Everything in this share is already on \(model.form.selectedCardTitle ?? "").")
                }
            }
            Section {
                ForEach(model.alreadyOnCard) { entry in
                    duplicateRow(entry)
                }
            } header: {
                Text("Already on This Card (\(model.alreadyOnCard.count))")
                    .monospacedDigit()
                    .accessibilityAddTraits(.isHeader)
            } footer: {
                Text("These won't be added.")
            }
        }
    }

    private func duplicateRow(_ entry: QuickAddModel.PrayerEntry) -> some View {
        HStack {
            VStack(alignment: .leading) {
                Text(verbatim: entry.title)
                Text("Already on this card")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .accessibilityElement(children: .combine)
            Spacer()
            Button("Add") { model.addBack(id: entry.id) }
                .buttonStyle(.borderless)
                .accessibilityLabel("Add \(entry.title)")
                .accessibilityIdentifier("ConfirmImport_AddBack")
        }
    }
}
