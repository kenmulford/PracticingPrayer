import Foundation
import Testing
@testable import PrayerKit

struct SharePayloadClassifierTests {
    @Test func plainWinsOverAttributedAndBytes() {
        let result = SharePayloadClassifier.classify(
            plainString: "plain", attributedString: "attr", rawBytes: Data("bytes".utf8))
        #expect(result == .text("plain"))
    }

    @Test func attributedUsedWithoutPlain() {
        let result = SharePayloadClassifier.classify(
            plainString: nil, attributedString: "attr", rawBytes: Data("bytes".utf8))
        #expect(result == .text("attr"))
    }

    @Test func bytesDecodedAsUTF8() {
        let result = SharePayloadClassifier.classify(
            plainString: nil, attributedString: nil, rawBytes: Data("Jos\u{00E9}".utf8))
        #expect(result == .text("Jos\u{00E9}"))
    }

    @Test func allNilIsUnsupportedType() {
        let result = SharePayloadClassifier.classify(plainString: nil, attributedString: nil, rawBytes: nil)
        #expect(result == .failure(.unsupportedType))
    }

    @Test func whitespaceOnlyIsEmptyText() {
        let result = SharePayloadClassifier.classify(plainString: " \n\t ", attributedString: nil, rawBytes: nil)
        #expect(result == .failure(.emptyText))
    }

    @Test func bytesOverCapAreOversized() {
        let bytes = Data(repeating: 0x61, count: SharePayloadClassifier.maxPayloadBytes + 1)
        #expect(bytes.count == 262_145)
        let result = SharePayloadClassifier.classify(plainString: nil, attributedString: nil, rawBytes: bytes)
        #expect(result == .failure(.oversized))
    }
}
