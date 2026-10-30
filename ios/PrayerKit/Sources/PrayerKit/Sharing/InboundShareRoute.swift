import Foundation

/// Classifies a URL the system hands the app: a universal share link, a `.prayercard` file, or neither.
public enum InboundShareRoute: Equatable, Sendable {
    public enum LinkKind: Equatable, Sendable { case request, card }

    case link(LinkKind)
    case file
    case ignored

    private static let shareHost = "practicingprayerapp.com"

    public init(url: URL) {
        if url.isFileURL {
            self = url.pathExtension.lowercased() == "prayercard" ? .file : .ignored
            return
        }
        guard url.scheme?.lowercased() == "https", url.host?.lowercased() == Self.shareHost else {
            self = .ignored
            return
        }
        var path = url.path
        if path.hasSuffix("/") { path.removeLast() }
        switch path {
        case "/share/r": self = .link(.request)
        case "/share/c": self = .link(.card)
        default: self = .ignored
        }
    }
}
