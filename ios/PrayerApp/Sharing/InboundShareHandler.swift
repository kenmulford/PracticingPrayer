import Foundation
import Observation
import PrayerKit

/// Receives universal links and `.prayercard` files the system hands the app and queues each as a payload to
/// confirm or a failure to report. A singleton because the scene delegate gets links before `AppRoot` builds
/// its services.
@Observable
final class InboundShareHandler {
    static let shared = InboundShareHandler()

    enum Failure: Equatable {
        case invalidRequestLink, invalidCardLink, invalidFile, newerVersion

        var message: LocalizedStringResource {
            switch self {
            case .invalidRequestLink: "The shared prayer link appears to be invalid or incomplete."
            case .invalidCardLink: "The shared prayer card link appears to be invalid or incomplete."
            case .invalidFile: "The shared prayer card file appears to be invalid."
            case .newerVersion:
                "This prayer was shared from a newer version of Practicing Prayer. Please update the app to import it."
            }
        }
    }

    enum Content: Equatable {
        case payload(SharePayload)
        case failure(Failure)
    }

    struct Item: Identifiable, Equatable {
        let id: UUID
        let content: Content
    }

    private(set) var queue: [Item] = []
    /// Set once a share link opens the app, so the first-run welcome and tour never cover the import.
    private(set) var isDeepLinkSession = false

    @ObservationIgnored private let readFile: (URL) throws -> Data

    init(readFile: @escaping (URL) throws -> Data = { try Data(contentsOf: $0) }) {
        self.readFile = readFile
    }

    func handle(_ url: URL) {
        switch InboundShareRoute(url: url) {
        case .link(let kind):
            isDeepLinkSession = true
            receive(ShareCodec.decode(link: url.absoluteString), invalid: kind == .request ? .invalidRequestLink : .invalidCardLink)
        case .file:
            guard let data = try? readFile(url) else {
                enqueue(.failure(.invalidFile))
                return
            }
            receive(ShareCodec.decode(file: data), invalid: .invalidFile)
        case .ignored:
            break
        }
    }

    func handle(userActivity: NSUserActivity) {
        guard userActivity.activityType == NSUserActivityTypeBrowsingWeb, let url = userActivity.webpageURL else { return }
        handle(url)
    }

    func enqueue(_ payload: SharePayload) {
        enqueue(.payload(payload))
    }

    func dismissHead() {
        guard !queue.isEmpty else { return }
        queue.removeFirst()
    }

    #if DEBUG
    func handleLaunchArgument(defaults: UserDefaults = .standard) {
        guard let string = defaults.string(forKey: "InboundShareURL"), let url = URL(string: string) else { return }
        handle(url)
    }
    #endif

    private func receive(_ result: ShareDecodeResult, invalid: Failure) {
        switch result {
        case .payload(let payload): enqueue(payload)
        case .newerVersion: enqueue(.failure(.newerVersion))
        case .invalid: enqueue(.failure(invalid))
        case .empty, .notAShareLink: break
        }
    }

    private func enqueue(_ content: Content) {
        queue.append(Item(id: UUID(), content: content))
    }
}
