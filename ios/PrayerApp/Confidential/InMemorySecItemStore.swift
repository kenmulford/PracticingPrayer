#if DEBUG
import Foundation
import PrayerKit
import Security

/// Keychain stand-in for UI launches and unit tests; the simulator Keychain persists across installs.
final class InMemorySecItemStore: SecItemStore {
    /// When set, every call returns this status.
    var failure: OSStatus?
    private var items: [String: Data] = [:]

    func copyData(service: String, account: String) -> (status: OSStatus, data: Data?) {
        if let failure { return (failure, nil) }
        guard let data = items[key(service, account)] else { return (errSecItemNotFound, nil) }
        return (errSecSuccess, data)
    }

    func update(service: String, account: String, data: Data) -> OSStatus {
        if let failure { return failure }
        guard items[key(service, account)] != nil else { return errSecItemNotFound }
        items[key(service, account)] = data
        return errSecSuccess
    }

    func add(service: String, account: String, data: Data) -> OSStatus {
        if let failure { return failure }
        items[key(service, account)] = data
        return errSecSuccess
    }

    private func key(_ service: String, _ account: String) -> String { "\(service)|\(account)" }
}
#endif
