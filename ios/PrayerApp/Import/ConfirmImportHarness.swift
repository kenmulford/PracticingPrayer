#if DEBUG
import PrayerKit
import SwiftUI

/// UI-test host for the Confirm Import sheet.
struct ConfirmImportHarness: View {
    static let argument = "-confirmImportHarness"

    @Environment(AppServices.self) private var services
    @State private var isPresented = false

    var body: some View {
        Color.clear
            .task {
                await services.open()
                isPresented = true
            }
            .sheet(isPresented: $isPresented) {
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
