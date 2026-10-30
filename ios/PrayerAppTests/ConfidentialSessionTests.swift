import Foundation
import Testing
@testable import PrayerApp

@MainActor
private final class CountingAuthenticator: DeviceOwnerAuthenticating {
    let outcome: DeviceOwnerAuthOutcome
    private(set) var calls = 0

    init(_ outcome: DeviceOwnerAuthOutcome) {
        self.outcome = outcome
    }

    func authenticate(reason: String) async -> DeviceOwnerAuthOutcome {
        calls += 1
        try? await Task.sleep(for: .milliseconds(50))
        return outcome
    }
}

@MainActor
@Suite struct ConfidentialSessionTests {
    @Test func concurrentCallsShareOneAuthentication() async {
        let authenticator = CountingAuthenticator(.success)
        let session = ConfidentialSession(authenticator: authenticator)
        async let first = session.authenticate(reason: "Open protected card")
        async let second = session.authenticate(reason: "Open protected card")
        let results = await [first, second]
        #expect(results == [true, true])
        #expect(authenticator.calls == 1)
        #expect(session.isUnlocked)
    }

    @Test func failedAuthenticationStaysLocked() async {
        let session = ConfidentialSession(authenticator: CountingAuthenticator(.failed))
        #expect(await session.authenticate(reason: "Open protected card") == false)
        #expect(!session.isUnlocked)
    }

    @Test func passcodeNotSetWithoutFallbackStaysLocked() async {
        let session = ConfidentialSession(authenticator: CountingAuthenticator(.passcodeNotSet))
        #expect(await session.authenticate(reason: "Open protected card") == false)
        #expect(!session.isUnlocked)
    }

    @Test func passcodeNotSetUsesPinFallback() async {
        let session = ConfidentialSession(authenticator: CountingAuthenticator(.passcodeNotSet))
        session.pinFallback = { true }
        #expect(await session.authenticate(reason: "Open protected card"))
        #expect(session.isUnlocked)
    }

    @Test func relockClearsUnlock() async {
        let session = ConfidentialSession(authenticator: CountingAuthenticator(.success))
        #expect(await session.authenticate(reason: "Open protected card"))
        session.relock()
        #expect(!session.isUnlocked)
    }

    @Test func successAndFailedNeverCallPinFallback() async {
        for outcome in [DeviceOwnerAuthOutcome.success, .failed] {
            let session = ConfidentialSession(authenticator: CountingAuthenticator(outcome))
            var fallbackCalls = 0
            session.pinFallback = {
                fallbackCalls += 1
                return true
            }
            _ = await session.authenticate(reason: "Open protected card")
            #expect(fallbackCalls == 0)
        }
    }
}
