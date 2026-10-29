import CommonCrypto
import Foundation
import Security

/// Salted PBKDF2-HMAC-SHA256 PIN hashing, wire-compatible with the MAUI app's PinHasher.
/// Record format: "<base64 salt>:<base64 hash>", 16-byte salt, 32-byte hash, 100,000 rounds,
/// password bytes are the PIN's UTF-8 encoding.
public enum PinHasher {
    private static let saltSizeBytes = 16
    private static let hashSizeBytes = 32
    private static let iterations: UInt32 = 100_000

    public static func hash(_ pin: String) -> String {
        var salt = [UInt8](repeating: 0, count: saltSizeBytes)
        let status = SecRandomCopyBytes(kSecRandomDefault, saltSizeBytes, &salt)
        precondition(status == errSecSuccess, "SecRandomCopyBytes failed: \(status)")
        let saltData = Data(salt)
        guard let derived = derive(passwordBytes: Array(pin.utf8), salt: saltData) else {
            preconditionFailure("PBKDF2 derivation failed")
        }
        return "\(saltData.base64EncodedString()):\(derived.base64EncodedString())"
    }

    public static func verify(_ pin: String, storedRecord: String?) -> Bool {
        guard let storedRecord else { return false }
        let parts = storedRecord.split(separator: ":", maxSplits: 1, omittingEmptySubsequences: false)
        guard parts.count == 2,
              let salt = Data(base64Encoded: String(parts[0])),
              let expected = Data(base64Encoded: String(parts[1])),
              let actual = derive(passwordBytes: Array(pin.utf8), salt: salt)
        else { return false }
        return constantTimeEquals(actual, expected)
    }

    static func derive(passwordBytes: [UInt8], salt: Data) -> Data? {
        derive(passwordBytes: passwordBytes, salt: salt, rounds: iterations)
    }

    static func derive(passwordBytes: [UInt8], salt: Data, rounds: UInt32) -> Data? {
        var derived = [UInt8](repeating: 0, count: hashSizeBytes)
        let saltBytes = [UInt8](salt)
        let status = passwordBytes.withUnsafeBufferPointer { pw in
            pw.withMemoryRebound(to: CChar.self) { pwChars in
                CCKeyDerivationPBKDF(
                    CCPBKDFAlgorithm(kCCPBKDF2),
                    pwChars.baseAddress, pwChars.count,
                    saltBytes, saltBytes.count,
                    CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256),
                    rounds,
                    &derived, derived.count)
            }
        }
        return status == 0 ? Data(derived) : nil
    }

    private static func constantTimeEquals(_ a: Data, _ b: Data) -> Bool {
        guard a.count == b.count else { return false }
        var diff: UInt8 = 0
        for (x, y) in zip(a, b) { diff |= x ^ y }
        return diff == 0
    }
}
