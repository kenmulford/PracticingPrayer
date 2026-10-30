struct ShareItem: Equatable {
    enum Content: Equatable {
        case text(String)
        case file(PrayerCardFile)
    }

    let title: String
    let content: Content
}
