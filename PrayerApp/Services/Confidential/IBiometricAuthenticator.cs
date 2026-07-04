namespace PrayerApp.Services.Confidential;

/// <summary>
/// Thin seam over the third-party biometric plugin (Oscore.Maui.Biometric — Maui.Biometric
/// namespace, v2.5.1), internal to <see cref="ConfidentialAccessService"/>. Lets unit tests
/// substitute a fake instead of depending on the platform biometric sensor, which cannot
/// run in the desktop unit-test host.
/// </summary>
public interface IBiometricAuthenticator
{
    /// <summary>
    /// Attempts biometric authentication with <paramref name="reason"/> shown as the prompt
    /// copy. Returns false for any non-success outcome (unavailable, failed, canceled,
    /// too-many-attempts, denied) — callers fall back to the PIN in every false case.
    /// </summary>
    Task<bool> AuthenticateAsync(string reason);
}
