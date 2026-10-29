#if DEBUG
import SwiftUI

/// UI-test host for the tag picker, shaped like the Prayer Editor's Tags section.
struct TagPickerHarness: View {
    static let argument = "-tagPickerHarness"

    @Environment(AppServices.self) private var services
    @State private var selection: Set<Int64> = []
    @State private var isPresented = false

    var body: some View {
        Color.clear
            .task {
                await services.open()
                isPresented = true
            }
            .sheet(isPresented: $isPresented) {
                NavigationStack {
                    Form {
                        NavigationLink("Tags") { TagPickerView(selection: $selection) }
                        Text(verbatim: "\(selection.count)")
                            .accessibilityIdentifier("TagPickerHarness_Count")
                    }
                }
            }
    }
}
#endif
