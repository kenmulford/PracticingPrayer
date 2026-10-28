import Foundation
import Security
import Testing
@testable import PrayerKit

private final class FakeSecItemStore: SecItemStore, @unchecked Sendable {
    var copyResult: (status: OSStatus, data: Data?) = (errSecSuccess, nil)
    var updateStatus: OSStatus = errSecSuccess
    var addStatus: OSStatus = errSecSuccess
    var copyCalls: [(service: String, account: String)] = []
    var updateCalls: [(service: String, account: String, data: Data)] = []
    var addCalls: [(service: String, account: String, data: Data)] = []

    func copyData(service: String, account: String) -> (status: OSStatus, data: Data?) {
        copyCalls.append((service, account))
        return copyResult
    }

    func update(service: String, account: String, data: Data) -> OSStatus {
        updateCalls.append((service, account, data))
        return updateStatus
    }

    func add(service: String, account: String, data: Data) -> OSStatus {
        addCalls.append((service, account, data))
        return addStatus
    }
}

@Suite struct PinRecordTests {
    @Test func readReturnsStoredString() throws {
        let store = FakeSecItemStore()
        store.copyResult = (errSecSuccess, Data("abc:def".utf8))
        #expect(try PinRecord(store: store).read() == "abc:def")
        #expect(store.copyCalls.count == 1)
        #expect(store.copyCalls[0].service == PinRecord.service)
        #expect(store.copyCalls[0].account == PinRecord.account)
    }

    @Test func readReturnsNilWhenNotFound() throws {
        let store = FakeSecItemStore()
        store.copyResult = (errSecItemNotFound, nil)
        #expect(try PinRecord(store: store).read() == nil)
    }

    @Test func readThrowsOnOtherStatus() {
        let store = FakeSecItemStore()
        store.copyResult = (errSecInteractionNotAllowed, nil)
        #expect(throws: PinRecordError.keychain(errSecInteractionNotAllowed)) {
            try PinRecord(store: store).read()
        }
    }

    @Test func writeUpdatesExistingItem() throws {
        let store = FakeSecItemStore()
        try PinRecord(store: store).write("abc:def")
        #expect(store.updateCalls.count == 1)
        #expect(store.updateCalls[0].data == Data("abc:def".utf8))
        #expect(store.addCalls.isEmpty)
    }

    @Test func writeAddsWhenAbsent() throws {
        let store = FakeSecItemStore()
        store.updateStatus = errSecItemNotFound
        try PinRecord(store: store).write("abc:def")
        #expect(store.addCalls.count == 1)
        #expect(store.addCalls[0].service == PinRecord.service)
        #expect(store.addCalls[0].account == PinRecord.account)
        #expect(store.addCalls[0].data == Data("abc:def".utf8))
    }

    @Test func writeThrowsOnUpdateFailure() {
        let store = FakeSecItemStore()
        store.updateStatus = errSecInteractionNotAllowed
        #expect(throws: PinRecordError.keychain(errSecInteractionNotAllowed)) {
            try PinRecord(store: store).write("abc:def")
        }
        #expect(store.addCalls.isEmpty)
    }

    @Test func writeThrowsOnAddFailure() {
        let store = FakeSecItemStore()
        store.updateStatus = errSecItemNotFound
        store.addStatus = errSecDuplicateItem
        #expect(throws: PinRecordError.keychain(errSecDuplicateItem)) {
            try PinRecord(store: store).write("abc:def")
        }
    }
}
