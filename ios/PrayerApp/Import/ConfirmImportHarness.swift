#if DEBUG
import PrayerKit
import SwiftUI

/// UI-test host for the Confirm Import sheet.
struct ConfirmImportHarness: View {
    static let argument = "-confirmImportHarness"

    @Environment(AppServices.self) private var services
    @State private var isPresented = false

    var body: some View {
        Group {
            if services.phase == .failed {
                Text("Harness could not open the database")
            } else {
                Color.clear
            }
        }
        .task {
            await services.open()
            isPresented = services.phase == .open
        }
        .sheet(isPresented: $isPresented) {
            // `isPresented` only turns on once the database is open, so `database` is safe to read here.
            ConfirmImportView(
                database: services.database,
                payload: .card(
                    title: "Shared",
                    requests: [
                        SharedPrayer(title: "Sample Prayer Entry 1", notes: nil),
                        SharedPrayer(title: "New One", notes: nil),
                    ]))
        }
    }
}
#endif
