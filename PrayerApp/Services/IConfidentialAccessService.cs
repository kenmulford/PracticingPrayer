namespace PrayerApp.Services;

/// <summary>
/// Auth primitive for confidential (protected) cards — Issue #251, Wave 2 of Confidential
/// Cards (#7 / milestone #10). Wraps device biometrics (Oscore.Maui.Biometric) with a
/// 6-digit PIN fallback. This service owns authentication and session state only; it does
/// NOT gate card/list rendering, Prayer Time, or edit surfaces — that wiring is #252-#255.
/// Callers combine <see cref="IsSessionUnlocked"/> with
/// <see cref="Models.PrayerCard.GetEffectiveProtectionMode"/> to decide what to reveal.
/// </summary>
public interface IConfidentialAccessService
{
    /// <summary>
    /// True once a successful <see cref="AuthenticateAsync"/> has unlocked the current
    /// session; false initially and after <see cref="RelockSession"/>.
    /// </summary>
    bool IsSessionUnlocked { get; }

    /// <summary>
    /// Authenticates the user: tries device biometrics first (shown with <paramref name="reason"/>
    /// as the prompt copy), falling back to the 6-digit PIN when biometrics are unavailable or
    /// fail. If no PIN has been set yet, this runs first-enable-protection PIN onboarding to set
    /// one. Sets <see cref="IsSessionUnlocked"/> to true on success.
    /// </summary>
    /// <param name="reason">User-facing reason shown in the biometric prompt.</param>
    /// <returns>True if the session is now unlocked.</returns>
    Task<bool> AuthenticateAsync(string reason);

    /// <summary>Locks the current session — the next protected access requires re-authentication.</summary>
    void RelockSession();
}
