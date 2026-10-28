import Foundation
import Security

/// Keychain access seam so the PIN record logic runs under `swift test`, where the Keychain is unavailable.
public protocol SecItemStore {
    func copyData(service: String, account: String) -> (status: OSStatus, data: Data?)
    func update(service: String, account: String, data: Data) -> OSStatus
    func add(service: String, account: String, data: Data) -> OSStatus
}

public struct SystemSecItemStore: SecItemStore {
    public init() {}

    public func copyData(service: String, account: String) -> (status: OSStatus, data: Data?) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        return (status, result as? Data)
    }

    public func update(service: String, account: String, data: Data) -> OSStatus {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        return SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
    }

    // No kSecAttrAccessible and no kSecAttrAccessGroup: the Keychain default and the app's default access group apply.
    public func add(service: String, account: String, data: Data) -> OSStatus {
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecValueData as String: data,
        ]
        return SecItemAdd(item as CFDictionary, nil)
    }
}

public enum PinRecordError: Error, Equatable {
    case keychain(OSStatus)
}

/// The PIN record in the Keychain item that MAUI SecureStorage wrote, so an upgraded install keeps its PIN.
public struct PinRecord {
    public static let service = "com.multithreadedllc.prayercards.microsoft.maui.essentials.preferences"
    public static let account = "confidential_access_pin"

    private let store: any SecItemStore

    public init(store: any SecItemStore = SystemSecItemStore()) {
        self.store = store
    }

    public func read() throws(PinRecordError) -> String? {
        let result = store.copyData(service: Self.service, account: Self.account)
        switch result.status {
        case errSecSuccess:
            return result.data.flatMap { String(data: $0, encoding: .utf8) }
        case errSecItemNotFound:
            return nil
        default:
            throw .keychain(result.status)
        }
    }

    public func write(_ record: String) throws(PinRecordError) {
        let data = Data(record.utf8)
        let updateStatus = store.update(service: Self.service, account: Self.account, data: data)
        switch updateStatus {
        case errSecSuccess:
            return
        case errSecItemNotFound:
            let addStatus = store.add(service: Self.service, account: Self.account, data: data)
            if addStatus != errSecSuccess { throw .keychain(addStatus) }
        default:
            throw .keychain(updateStatus)
        }
    }
}
