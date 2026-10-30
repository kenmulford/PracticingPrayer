import Foundation
import Testing

struct ShareExtensionEmbedTests {
    @Test func extensionIsEmbeddedForTextOnly() throws {
        let plugIns = try #require(Bundle.main.builtInPlugInsURL)
        let appex = try #require(Bundle(url: plugIns.appendingPathComponent("ShareExtension.appex")))
        #expect(appex.bundleIdentifier == "com.multithreadedllc.prayercards.actionextension")
        let ext = try #require(appex.object(forInfoDictionaryKey: "NSExtension") as? [String: Any])
        #expect(ext["NSExtensionPointIdentifier"] as? String == "com.apple.share-services")
        let attributes = try #require(ext["NSExtensionAttributes"] as? [String: Any])
        let rule = try #require(attributes["NSExtensionActivationRule"] as? [String: Any])
        #expect(Array(rule.keys) == ["NSExtensionActivationSupportsText"])
    }

    @Test func hostRegistersScheme() throws {
        let types = try #require(Bundle.main.object(forInfoDictionaryKey: "CFBundleURLTypes") as? [[String: Any]])
        let schemes = types.flatMap { $0["CFBundleURLSchemes"] as? [String] ?? [] }
        #expect(schemes.contains("practicingprayer"))
    }
}
