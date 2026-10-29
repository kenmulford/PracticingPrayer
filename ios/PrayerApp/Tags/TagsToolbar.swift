import SwiftUI

struct TagsToolbar: ToolbarContent {
    let newTag: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) { addButton }
                .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { addButton }
        }
    }

    private var addButton: some View {
        Button(action: newTag) {
            Label("New Tag", systemImage: "plus")
        }
    }
}

struct TagPrayersToolbar: ToolbarContent {
    let canDelete: Bool
    let edit: () -> Void
    let requestDelete: () -> Void

    var body: some ToolbarContent {
        if #available(iOS 27, *) {
            ToolbarItemGroup(placement: .topBarTrailing) {
                editButton
                moreMenu
            }
            .visibilityPriority(.high)
        } else {
            ToolbarItem(placement: .topBarTrailing) { editButton }
            ToolbarItem(placement: .topBarTrailing) { moreMenu }
        }
    }

    private var editButton: some View {
        Button("Edit", action: edit)
    }

    @ViewBuilder
    private var moreMenu: some View {
        if canDelete {
            Menu {
                Button("Delete Tag", systemImage: "trash", role: .destructive, action: requestDelete)
            } label: {
                Label("More", systemImage: "ellipsis.circle")
            }
        }
    }
}
