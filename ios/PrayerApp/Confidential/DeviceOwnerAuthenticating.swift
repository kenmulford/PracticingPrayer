import Foundation
import LocalAuthentication

enum DeviceOwnerAuthOutcome: Sendable { case success, failed, passcodeNotSet }

protocol DeviceOwnerAuthenticating {
    func authenticate(reason: String) async -> DeviceOwnerAuthOutcome
}

struct SystemDeviceOwnerAuthenticator: DeviceOwnerAuthenticating {
    func authenticate(reason: String) async -> DeviceOwnerAuthOutcome {
        let context = LAContext()
        var error: NSError?
        // .deviceOwnerAuthentication falls back to the device passcode after Face ID.
        guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else {
            return (error as? LAError)?.code == .passcodeNotSet ? .passcodeNotSet : .failed
        }
        do {
            let ok = try await context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason)
            return ok ? .success : .failed
        } catch let error as LAError where error.code == .passcodeNotSet {
            return .passcodeNotSet
        } catch {
            return .failed
        }
    }
}

#if DEBUG
/// UI tests cannot drive the system Face ID sheet, so `CONFIDENTIAL_AUTH_STUB` picks the outcome.
struct StubDeviceOwnerAuthenticator: DeviceOwnerAuthenticating {
    let outcome: DeviceOwnerAuthOutcome

    init?(environmentValue: String?) {
        switch environmentValue {
        case "success": outcome = .success
        case "cancel": outcome = .failed
        case "passcodeNotSet": outcome = .passcodeNotSet
        default: return nil
        }
    }

    func authenticate(reason: String) async -> DeviceOwnerAuthOutcome { outcome }
}
#endif
