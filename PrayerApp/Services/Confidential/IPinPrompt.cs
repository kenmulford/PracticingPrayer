namespace PrayerApp.Services.Confidential;

/// <summary>
/// Thin seam over the PIN-entry UI, internal to <see cref="ConfidentialAccessService"/>.
/// Two distinct prompts: setting a new 6-digit PIN (first-enable-protection onboarding)
/// and entering the existing PIN to verify (biometric fallback). Kept separate from the
/// biometric/storage seams so unit tests can script UI-less scenarios (e.g. user cancels).
/// </summary>
public interface IPinPrompt
{
    /// <summary>
    /// Shows the first-enable-protection onboarding prompt that sets a new 6-digit PIN.
    /// Returns the chosen PIN, or null if the user canceled.
    /// </summary>
    Task<string?> PromptSetPinAsync();

    /// <summary>
    /// Shows the PIN-entry prompt used as the biometric fallback. Returns the entered
    /// PIN, or null if the user canceled.
    /// </summary>
    Task<string?> PromptEnterPinAsync();
}
