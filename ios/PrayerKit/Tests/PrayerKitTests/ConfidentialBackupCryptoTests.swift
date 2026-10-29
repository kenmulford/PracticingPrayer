import Foundation
import Testing
@testable import PrayerKit

private func backupDir() -> URL {
    var url = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 { url.deleteLastPathComponent() }
    return url.appendingPathComponent("contracts/backup")
}

private func readText(_ name: String) throws -> String {
    try String(contentsOf: backupDir().appendingPathComponent(name), encoding: .utf8)
}

private func passphrase() throws -> String {
    try readText("passphrase.txt").trimmingCharacters(in: .whitespacesAndNewlines)
}

private let sample = #"{"SchemaVersion":1,"Cards":[],"Prayers":[]}"#

@Suite struct ConfidentialBackupCryptoTests {
    @Test func decryptsMauiFixture() throws {
        let blob = try Data(contentsOf: backupDir().appendingPathComponent("maui/confidential.enc"))
        let decrypted = try ConfidentialBackupCrypto.decrypt(blob, passphrase: passphrase())
        let payload = try ConfidentialExportPayload.fromJSON(readText("payload.json"))
        #expect(try ConfidentialExportPayload.fromJSON(decrypted) == payload)
        #expect(try ConfidentialExportPayload.fromJSON(payload.toJSON()) == payload)
    }

    @Test func decryptsNativeFixture() throws {
        let blob = try Data(contentsOf: backupDir().appendingPathComponent("native/confidential.enc"))
        let decrypted = try ConfidentialBackupCrypto.decrypt(blob, passphrase: passphrase())
        let payload = try ConfidentialExportPayload.fromJSON(readText("payload.json"))
        #expect(try ConfidentialExportPayload.fromJSON(decrypted) == payload)
    }

    @Test func roundTripReturnsOriginalString() throws {
        let blob = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "correct horse battery staple")
        #expect(try ConfidentialBackupCrypto.decrypt(blob, passphrase: "correct horse battery staple") == sample)
    }

    @Test func twoEncryptsDiffer() throws {
        let a = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "shared-passphrase-1234")
        let b = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "shared-passphrase-1234")
        #expect(a != b)
    }

    @Test func emptyPayloadRoundTrips() throws {
        let empty = ConfidentialExportPayload(cards: [], prayers: [], interactions: [], tagJunctions: [])
        let blob = try ConfidentialBackupCrypto.encrypt(empty.toJSON(), passphrase: "empty-payload-passphrase")
        let json = try ConfidentialBackupCrypto.decrypt(blob, passphrase: "empty-payload-passphrase")
        #expect(try ConfidentialExportPayload.fromJSON(json) == empty)
    }

    @Test func wrongPassphraseThrows() throws {
        let blob = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "correct horse battery staple")
        #expect(throws: ConfidentialBackupError.authenticationFailed) {
            try ConfidentialBackupCrypto.decrypt(blob, passphrase: "wrong passphrase entirely")
        }
    }

    @Test func flippedCiphertextByteThrows() throws {
        var blob = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "correct horse battery staple")
        blob[blob.count - 1] ^= 0xFF
        #expect(throws: ConfidentialBackupError.authenticationFailed) {
            try ConfidentialBackupCrypto.decrypt(blob, passphrase: "correct horse battery staple")
        }
    }

    @Test func shortBlobThrows() {
        #expect(throws: ConfidentialBackupError.truncatedBlob) {
            try ConfidentialBackupCrypto.decrypt(Data(count: 48), passphrase: "anything")
        }
    }

    @Test func versionTwoThrows() throws {
        var blob = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "correct horse battery staple", iterations: 1)
        blob[0] = 2
        #expect(throws: ConfidentialBackupError.unsupportedVersion) {
            try ConfidentialBackupCrypto.decrypt(blob, passphrase: "correct horse battery staple")
        }
    }

    @Test(arguments: [Int32(0), Int32(-1)])
    func nonPositiveIterationsThrow(iterations: Int32) throws {
        var blob = try ConfidentialBackupCrypto.encrypt(sample, passphrase: "correct horse battery staple", iterations: 1)
        withUnsafeBytes(of: iterations.littleEndian) { blob.replaceSubrange(1..<5, with: $0) }
        #expect(throws: ConfidentialBackupError.invalidIterations) {
            try ConfidentialBackupCrypto.decrypt(blob, passphrase: "correct horse battery staple")
        }
    }

    @Test func passphraseGate() {
        #expect(!PassphrasePolicy.meetsMinimumLength(String(repeating: "a", count: 11)))
        #expect(PassphrasePolicy.meetsMinimumLength(String(repeating: "a", count: 12)))
        #expect(PassphrasePolicy.meetsMinimumLength(String(repeating: "😀", count: 6)))
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["PP_WRITE_BACKUP_FIXTURES"] == "1"))
    func writeNativeFixture() throws {
        let payload = try ConfidentialExportPayload.fromJSON(readText("payload.json"))
        let blob = try ConfidentialBackupCrypto.encrypt(payload.toJSON(), passphrase: passphrase())
        try blob.write(to: backupDir().appendingPathComponent("native/confidential.enc"))
    }
}
