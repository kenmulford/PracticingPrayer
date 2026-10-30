import SwiftUI

struct ShareMenuItem: View {
    let item: ShareItem?

    var body: some View {
        switch item?.content {
        case .text(let text)?:
            ShareLink(item: text, subject: Text(verbatim: item?.title ?? "")) { label }
        case .file(let file)?:
            ShareLink(item: file, preview: SharePreview(Text(verbatim: item?.title ?? ""))) { label }
        case nil:
            Button {} label: { label }.disabled(true)
        }
    }

    private var label: some View {
        Label("Share", systemImage: "square.and.arrow.up").accessibilityLabel("Share")
    }
}
