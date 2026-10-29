import CryptoKit
import Foundation

public enum ConfidentialBackupError: Error, Equatable {
    case truncatedBlob, unsupportedVersion, invalidIterations, authenticationFailed
}

/// AES-256-GCM + PBKDF2-SHA256 envelope, byte-compatible with the MAUI `ConfidentialBackupCrypto`.
/// Blob: [0] version, [1..4] LE Int32 iterations, [5..20] salt, [21..32] nonce, [33..48] tag, [49..] ciphertext.
public enum ConfidentialBackupCrypto {
    public static let formatVersion: UInt8 = 1
    public static let defaultIterations: Int32 = 100_000

    private static let saltSize = 16
    private static let nonceSize = 12
    private static let tagSize = 16
    private static let iterationsOffset = 1
    private static let saltOffset = 5
    private static let nonceOffset = saltOffset + saltSize
    private static let tagOffset = nonceOffset + nonceSize
    private static let ciphertextOffset = tagOffset + tagSize

    public static func encrypt(
        _ plaintextJSON: String, passphrase: String, iterations: Int32 = defaultIterations
    ) throws -> Data {
        var generator = SystemRandomNumberGenerator()
        let salt = Data((0..<saltSize).map { _ in UInt8.random(in: .min ... .max, using: &generator) })
        let key = deriveKey(passphrase, salt: salt, iterations: iterations)
        let sealed = try AES.GCM.seal(Data(plaintextJSON.utf8), using: key, nonce: AES.GCM.Nonce())

        var blob = Data([formatVersion])
        withUnsafeBytes(of: iterations.littleEndian) { blob.append(contentsOf: $0) }
        blob.append(salt)
        blob.append(contentsOf: sealed.nonce)
        blob.append(sealed.tag)
        blob.append(sealed.ciphertext)
        return blob
    }

    public static func decrypt(_ blob: Data, passphrase: String) throws(ConfidentialBackupError) -> String {
        // Data(blob) rebases indices to 0, so a slice argument reads the same bytes.
        let bytes = Data(blob)
        guard bytes.count >= ciphertextOffset else { throw .truncatedBlob }
        guard bytes[0] == formatVersion else { throw .unsupportedVersion }
        let iterations = Int32(littleEndian: bytes.withUnsafeBytes {
            $0.loadUnaligned(fromByteOffset: iterationsOffset, as: Int32.self)
        })
        guard iterations > 0 else { throw .invalidIterations }

        let key = deriveKey(passphrase, salt: bytes[saltOffset..<nonceOffset], iterations: iterations)
        do {
            let box = try AES.GCM.SealedBox(
                nonce: AES.GCM.Nonce(data: bytes[nonceOffset..<tagOffset]),
                ciphertext: bytes[ciphertextOffset...],
                tag: bytes[tagOffset..<ciphertextOffset])
            return String(decoding: try AES.GCM.open(box, using: key), as: UTF8.self)
        } catch {
            throw .authenticationFailed
        }
    }

    private static func deriveKey(_ passphrase: String, salt: Data, iterations: Int32) -> SymmetricKey {
        guard let derived = PinHasher.derive(
            passwordBytes: Array(passphrase.utf8), salt: salt, rounds: UInt32(iterations))
        else { preconditionFailure("PBKDF2 derivation failed") }
        return SymmetricKey(data: derived)
    }
}
