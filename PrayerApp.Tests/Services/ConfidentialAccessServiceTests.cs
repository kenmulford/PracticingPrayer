using PrayerApp.Services;
using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Unit coverage for issue #251's confidential-access auth primitive. Biometric, SecureStorage,
/// and the PIN-entry UI are all behind injectable seams (see PrayerApp/Services/Confidential/)
/// so this exercises only the service's own logic: biometric-first/PIN-fallback routing,
/// PIN onboarding on first use, the session-unlock state machine, and (indirectly, since the
/// hasher/tracker are internal) the salted-hash verify and escalating-backoff behavior that
/// AuthenticateAsync drives through PinHasher and PinAttemptTracker.
/// </summary>
public class ConfidentialAccessServiceTests
{
    private const string Reason = "Unlock protected cards";

    private readonly FakeBiometricAuthenticator _biometric = new();
    private readonly FakeSecureStore _secureStore = new();
    private readonly FakePinPrompt _pinPrompt = new();

    private ConfidentialAccessService CreateSut() =>
        new(_biometric, _secureStore, _pinPrompt);

    // ── session state machine ───────────────────────────────────────

    [Fact]
    public void IsSessionUnlocked_Initially_IsFalse()
    {
        var sut = CreateSut();

        Assert.False(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task AuthenticateAsync_BiometricSucceeds_UnlocksSession()
    {
        _biometric.NextResult = true;
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.True(result);
        Assert.True(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task RelockSession_AfterUnlock_SetsIsSessionUnlockedFalse()
    {
        _biometric.NextResult = true;
        var sut = CreateSut();
        await sut.AuthenticateAsync(Reason);
        Assert.True(sut.IsSessionUnlocked);

        sut.RelockSession();

        Assert.False(sut.IsSessionUnlocked);
    }

    // ── biometric-first / PIN-fallback routing ──────────────────────

    [Fact]
    public async Task AuthenticateAsync_BiometricUnavailable_FallsBackToPin_FirstUse_RunsOnboarding()
    {
        _biometric.NextResult = false; // unavailable/failed
        _pinPrompt.SetPinToReturn = "123456";
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.True(result);
        Assert.True(sut.IsSessionUnlocked);
        Assert.True(_pinPrompt.SetPinWasPrompted); // onboarding ran because no PIN existed
        Assert.NotNull(await _secureStore.GetAsync("confidential_access_pin")); // hash persisted
    }

    [Fact]
    public async Task AuthenticateAsync_FirstUseOnboarding_NeverStoresPlaintextPin()
    {
        _biometric.NextResult = false;
        _pinPrompt.SetPinToReturn = "123456";
        var sut = CreateSut();

        await sut.AuthenticateAsync(Reason);

        var stored = await _secureStore.GetAsync("confidential_access_pin");
        Assert.NotNull(stored);
        Assert.DoesNotContain("123456", stored);
    }

    [Fact]
    public async Task AuthenticateAsync_FirstUseOnboarding_UserCancels_ReturnsFalse_SessionStaysLocked()
    {
        _biometric.NextResult = false;
        _pinPrompt.SetPinToReturn = null; // user canceled onboarding
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.False(result);
        Assert.False(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task AuthenticateAsync_PinAlreadySet_CorrectPin_Unlocks()
    {
        _secureStore.Seed("confidential_access_pin", PinHasher.Hash("654321"));
        _biometric.NextResult = false;
        _pinPrompt.EnterPinToReturn = "654321";
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.True(result);
        Assert.True(sut.IsSessionUnlocked);
        Assert.False(_pinPrompt.SetPinWasPrompted); // no re-onboarding when a PIN exists
    }

    [Fact]
    public async Task AuthenticateAsync_PinAlreadySet_WrongPin_DoesNotUnlock()
    {
        _secureStore.Seed("confidential_access_pin", PinHasher.Hash("654321"));
        _biometric.NextResult = false;
        _pinPrompt.EnterPinToReturn = "000000";
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.False(result);
        Assert.False(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task AuthenticateAsync_PinEntryCanceled_ReturnsFalse()
    {
        _secureStore.Seed("confidential_access_pin", PinHasher.Hash("654321"));
        _biometric.NextResult = false;
        _pinPrompt.EnterPinToReturn = null;
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync(Reason);

        Assert.False(result);
    }

    // ── escalating lockout after repeated failures ──────────────────

    [Fact]
    public async Task AuthenticateAsync_FiveFailedPinAttempts_SixthIsLockedOutRegardlessOfPin()
    {
        _secureStore.Seed("confidential_access_pin", PinHasher.Hash("654321"));
        _biometric.NextResult = false;
        _pinPrompt.EnterPinToReturn = "000000"; // wrong every time
        var sut = CreateSut();

        for (var i = 0; i < 5; i++)
            Assert.False(await sut.AuthenticateAsync(Reason));

        // 6th attempt: even the CORRECT pin must be rejected while locked out.
        _pinPrompt.EnterPinToReturn = "654321";
        var result = await sut.AuthenticateAsync(Reason);

        Assert.False(result);
        Assert.False(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task AuthenticateAsync_SuccessfulPin_ResetsFailureCount()
    {
        _secureStore.Seed("confidential_access_pin", PinHasher.Hash("654321"));
        _biometric.NextResult = false;
        var sut = CreateSut();

        // 4 failures — under the 5-failure threshold, no lockout yet
        _pinPrompt.EnterPinToReturn = "000000";
        for (var i = 0; i < 4; i++)
            await sut.AuthenticateAsync(Reason);

        // Correct PIN on the 5th try succeeds and clears the counter
        _pinPrompt.EnterPinToReturn = "654321";
        Assert.True(await sut.AuthenticateAsync(Reason));
        sut.RelockSession();

        // Next attempt is fresh — no residual lockout from the earlier failures
        _pinPrompt.EnterPinToReturn = "654321";
        Assert.True(await sut.AuthenticateAsync(Reason));
    }

    // ── fakes ────────────────────────────────────────────────────────

    private sealed class FakeBiometricAuthenticator : IBiometricAuthenticator
    {
        public bool NextResult { get; set; }
        public Task<bool> AuthenticateAsync(string reason) => Task.FromResult(NextResult);
    }

    private sealed class FakeSecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _values = new();

        public void Seed(string key, string value) => _values[key] = value;

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    private sealed class FakePinPrompt : IPinPrompt
    {
        public string? SetPinToReturn { get; set; }
        public string? EnterPinToReturn { get; set; }
        public bool SetPinWasPrompted { get; private set; }

        public Task<string?> PromptSetPinAsync()
        {
            SetPinWasPrompted = true;
            return Task.FromResult(SetPinToReturn);
        }

        public Task<string?> PromptEnterPinAsync() => Task.FromResult(EnterPinToReturn);
    }
}
