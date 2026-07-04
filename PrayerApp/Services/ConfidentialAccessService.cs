using PrayerApp.Services.Confidential;

namespace PrayerApp.Services;

/// <summary>
/// Auth primitive for confidential (protected) cards — Issue #251. Tries device biometrics
/// first; on unavailable/failed biometric, falls back to a 6-digit PIN verified against a
/// salted hash (never plaintext) in <see cref="ISecureStore"/>. If no PIN has been set yet,
/// runs first-enable-protection PIN onboarding via <see cref="IPinPrompt.PromptSetPinAsync"/>.
/// Platform/UI dependencies (Oscore biometric call, SecureStorage, PIN-entry UI) are behind
/// injectable seams so this class's logic is unit-testable with fakes.
/// </summary>
public class ConfidentialAccessService : IConfidentialAccessService
{
    private const string PinRecordKey = "confidential_access_pin";

    private readonly IBiometricAuthenticator _biometric;
    private readonly ISecureStore _secureStore;
    private readonly IPinPrompt _pinPrompt;
    private readonly PinAttemptTracker _attempts = new();

    public bool IsSessionUnlocked { get; private set; }

    public ConfidentialAccessService(
        IBiometricAuthenticator biometric,
        ISecureStore secureStore,
        IPinPrompt pinPrompt)
    {
        _biometric = biometric;
        _secureStore = secureStore;
        _pinPrompt = pinPrompt;
    }

    public async Task<bool> AuthenticateAsync(string reason)
    {
        if (await _biometric.AuthenticateAsync(reason))
        {
            IsSessionUnlocked = true;
            return true;
        }

        return await AuthenticateWithPinAsync();
    }

    public void RelockSession()
    {
        IsSessionUnlocked = false;
    }

    private async Task<bool> AuthenticateWithPinAsync()
    {
        var storedRecord = await _secureStore.GetAsync(PinRecordKey);

        // No PIN set yet — first-enable-protection onboarding sets one now.
        if (string.IsNullOrEmpty(storedRecord))
        {
            var newPin = await _pinPrompt.PromptSetPinAsync();
            if (newPin is null)
                return false;

            await _secureStore.SetAsync(PinRecordKey, PinHasher.Hash(newPin));
            IsSessionUnlocked = true;
            return true;
        }

        if (_attempts.IsLockedOut)
            return false;

        var enteredPin = await _pinPrompt.PromptEnterPinAsync();
        if (enteredPin is null)
            return false;

        if (PinHasher.Verify(enteredPin, storedRecord))
        {
            _attempts.Reset();
            IsSessionUnlocked = true;
            return true;
        }

        _attempts.RecordFailure();
        return false;
    }
}
