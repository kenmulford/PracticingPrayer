import SwiftUI
import TipKit

struct CollectionsTip: Tip {
    var title: Text { Text("Collections") }
    var message: Text? { Text("Collections help you organize your cards.") }
    var actions: [Action] {
        Action(id: "manage-collections", title: "Manage Collections")
    }
}
