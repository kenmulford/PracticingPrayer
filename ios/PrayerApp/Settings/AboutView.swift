import SwiftUI

struct AboutView: View {
    #if DEBUG
    static let seedArgument = "-seedDiagnosticLog"
    #endif

    private static let privacyURL = URL(string: "https://practicingprayerapp.com/privacy")!
    private static let websiteURL = URL(string: "https://practicingprayerapp.com")!

    private let log = DiagnosticLog(directory: AboutView.defaultDirectory)
    @State private var hasEntries = false

    private var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
    }

    private var build: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? ""
    }

    init() {}

    static var defaultDirectory: URL {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["PRAYER_DIAGNOSTICS_DIR"] {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        #endif
        return URL.applicationSupportDirectory
    }

    var body: some View {
        List {
            Section {
                header
            }
            Section {
                linkRow("Privacy Policy", url: Self.privacyURL, identifier: "about.privacy")
                linkRow("Website", url: Self.websiteURL, identifier: "about.website")
            }
            if hasEntries {
                Section {
                    ShareLink(item: log.logURL, preview: SharePreview(Text("Diagnostic Log"))) {
                        Text("Send Diagnostic Info")
                    }
                    .accessibilityIdentifier("about.sendDiagnostics")
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .frame(maxWidth: 700)
        .frame(maxWidth: .infinity)
        .background(Color(.systemGroupedBackground))
        .navigationTitle(Text("About"))
        .navigationBarTitleDisplayMode(.inline)
        .task {
            let log = log
            #if DEBUG
            let seed = ProcessInfo.processInfo.arguments.contains(Self.seedArgument)
            #else
            let seed = false
            #endif
            hasEntries = await Task.detached {
                if seed { log.log(category: "UITest", message: "seed") }
                log.trim()
                return log.hasEntries
            }.value
        }
    }

    private var header: some View {
        VStack(spacing: 8) {
            Image(.aboutIcon)
                .resizable()
                .scaledToFit()
                .frame(width: 64, height: 64)
                .accessibilityHidden(true)
            Text("Practicing Prayer")
                .font(.title2.bold())
            Text("Version \(version) (\(build))")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Text("A personal worship tool for organizing your prayer life. Create prayer cards, track requests, set reminders, and spend focused time in prayer.")
                .font(.body)
        }
        .frame(maxWidth: .infinity)
        .multilineTextAlignment(.center)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("about.header")
    }

    private func linkRow(_ title: LocalizedStringKey, url: URL, identifier: String) -> some View {
        Link(destination: url) {
            HStack {
                Text(title)
                Spacer()
                Image(systemName: "arrow.up.right")
                    .accessibilityHidden(true)
            }
        }
        .accessibilityIdentifier(identifier)
    }
}
