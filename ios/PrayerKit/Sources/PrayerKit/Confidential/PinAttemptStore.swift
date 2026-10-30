import Foundation
import Security

public struct PinAttemptState: Codable, Equatable, Sendable {
    public var failures: Int
    public var lockedUntil: Date?

    public static let cleared = PinAttemptState(failures: 0, lockedUntil: nil)

    public init(failures: Int, lockedUntil: Date?) {
        self.failures = failures
        self.lockedUntil = lockedUntil
    }

    private enum CodingKeys: String, CodingKey { case failures, lockedUntil }

    // Synthesized encoding omits a nil key; the stored record always carries "lockedUntil".
    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(failures, forKey: .failures)
        if let lockedUntil {
            try container.encode(lockedUntil, forKey: .lockedUntil)
        } else {
            try container.encodeNil(forKey: .lockedUntil)
        }
    }
}

public enum PinAttemptStoreError: Error, Equatable {
    case keychain(OSStatus)
    case malformed
}

/// The failed-attempt counter in the Keychain, beside the PIN record, so it survives relaunch.
public struct PinAttemptStore {
    public static let service = PinRecord.service
    public static let account = "confidential_pin_attempts"

    private let store: any SecItemStore

    public init(store: any SecItemStore = SystemSecItemStore()) {
        self.store = store
    }

    public func read() throws(PinAttemptStoreError) -> PinAttemptState {
        let result = store.copyData(service: Self.service, account: Self.account)
        switch result.status {
        case errSecSuccess:
            guard let data = result.data else { throw .malformed }
            let decoder = JSONDecoder()
            decoder.dateDecodingStrategy = .iso8601
            guard let state = try? decoder.decode(PinAttemptState.self, from: data) else { throw .malformed }
            return state
        case errSecItemNotFound:
            return .cleared
        default:
            throw .keychain(result.status)
        }
    }

    public func write(_ state: PinAttemptState) throws(PinAttemptStoreError) {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(state) else { throw .malformed }
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
