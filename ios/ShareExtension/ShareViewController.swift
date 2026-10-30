import PrayerKit
import SwiftUI
import UIKit
import UniformTypeIdentifiers

final class ShareViewController: UIViewController {
    private let status = ShareStatus()

    override func viewDidLoad() {
        super.viewDidLoad()
        let host = UIHostingController(rootView: ShareStatusView(status: status))
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)

        AccessibilityNotification.Announcement(String(localized: "Saving to Practicing Prayer…")).post()
        startImport()
    }

    private func startImport() {
        let containerURL = FileManager.default.containerURL(
            forSecurityApplicationGroupIdentifier: PendingImportWriter.appGroupID)
        let provider = extensionContext?.inputItems
            .compactMap { ($0 as? NSExtensionItem)?.attachments }
            .flatMap { $0 }
            .first { $0.hasItemConformingToTypeIdentifier(UTType.plainText.identifier) }

        guard let provider else {
            containerURL.map { ImportBreadcrumbLog(containerURL: $0).append(.noAttachment, byteCount: nil) }
            finish(written: false)
            return
        }

        provider.loadItem(forTypeIdentifier: UTType.plainText.identifier, options: nil) { [weak self] item, error in
            let written = Self.process(item: item, error: error, containerURL: containerURL)
            DispatchQueue.main.async { self?.finish(written: written) }
        }
    }

    /// Runs on the provider's queue, so it stays nonisolated (the module defaults to MainActor).
    nonisolated static func process(item: (any NSSecureCoding)?, error: (any Error)?, containerURL: URL?) -> Bool {
        guard let containerURL else { return false }
        let log = ImportBreadcrumbLog(containerURL: containerURL)
        if error != nil {
            log.append(.loadItemError, byteCount: nil)
            return false
        }
        let result = SharePayloadClassifier.classify(
            plainString: item as? String,
            attributedString: (item as? NSAttributedString)?.string,
            rawBytes: item as? Data)
        switch result {
        case .failure(let token):
            log.append(token, byteCount: nil)
            return false
        case .text(let text):
            return PendingImportWriter(containerURL: containerURL).write(text)
        }
    }

    private func finish(written: Bool) {
        guard written else {
            status.failed = true
            AccessibilityNotification.Announcement(String(localized: "Couldn't save. Try again.")).post()
            extensionContext?.completeRequest(returningItems: [], completionHandler: nil)
            return
        }
        extensionContext?.completeRequest(returningItems: []) { [weak self] _ in
            DispatchQueue.main.async { self?.openHostApp() }
        }
    }

    /// Share extensions have no supported host-launch API; walking the responder chain to
    /// UIApplication is the 1.7.2 behavior.
    private func openHostApp() {
        var responder: UIResponder? = self
        while let current = responder {
            if let application = current as? UIApplication {
                application.open(PendingImportWriter.hostURL, options: [:], completionHandler: nil)
                return
            }
            responder = current.next
        }
    }
}
