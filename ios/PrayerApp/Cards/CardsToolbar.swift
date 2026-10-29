import SwiftUI

struct CardsToolbar: ToolbarContent {
    let addCard: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) { addButton }
                .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var addButton: some View {
        Button(action: addCard) {
            Label("Add Card", systemImage: "plus")
        }
    }
}
