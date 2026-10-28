import Foundation
import Testing
@testable import PrayerKit

private struct PinFixture: Decodable {
    let pin: String
    let record: String
    let swiftRecord: String

    static func load() throws -> PinFixture {
        var url = URL(fileURLWithPath: #filePath)
        for _ in 0..<5 { url.deleteLastPathComponent() }
        url.appendPathComponent("contracts/pin/pin-record.json")
        return try JSONDecoder().decode(PinFixture.self, from: Data(contentsOf: url))
    }
}

@Suite struct PinHasherTests {
    @Test func verifiesMauiRecord() throws {
        let f = try PinFixture.load()
        #expect(PinHasher.verify(f.pin, storedRecord: f.record))
    }

    @Test func verifiesSwiftRecord() throws {
        let f = try PinFixture.load()
        #expect(PinHasher.verify(f.pin, storedRecord: f.swiftRecord))
    }

    @Test func freshHashVerifies() {
        #expect(PinHasher.verify("123456", storedRecord: PinHasher.hash("123456")))
    }

    @Test func wrongPinIsFalse() throws {
        let f = try PinFixture.load()
        #expect(!PinHasher.verify("654321", storedRecord: f.record))
    }

    @Test func missingRecordIsFalse() {
        #expect(!PinHasher.verify("123456", storedRecord: nil))
    }

    @Test func recordWithoutColonIsFalse() {
        #expect(!PinHasher.verify("123456", storedRecord: "not-a-valid-record"))
    }

    @Test func invalidBase64IsFalse() {
        #expect(!PinHasher.verify("123456", storedRecord: "!!!:@@@"))
    }

    @Test func nonAsciiPinUsesUtf8Bytes() throws {
        let pin = "١٢٣٤٥٦"
        let salt = Data((0..<16).map { UInt8($0) })
        let derived = try #require(PinHasher.derive(passwordBytes: Array(pin.utf8), salt: salt))
        let record = "\(salt.base64EncodedString()):\(derived.base64EncodedString())"
        #expect(PinHasher.verify(pin, storedRecord: record))
        #expect(PinHasher.verify(pin, storedRecord: PinHasher.hash(pin)))
    }

    @Test func printsSwiftRecordWhenRegenerating() throws {
        guard ProcessInfo.processInfo.environment["PIN_FIXTURE_REGEN"] == "1" else { return }
        let f = try PinFixture.load()
        print("SWIFT_RECORD=\(PinHasher.hash(f.pin))")
    }
}
