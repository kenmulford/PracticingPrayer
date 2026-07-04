namespace PrayerApp.Services.Confidential;

/// <summary>
/// Thin seam over <c>SecureStorage.Default</c>, internal to <see cref="ConfidentialAccessService"/>.
/// SecureStorage does not run in the desktop unit-test host, so tests substitute an in-memory
/// fake behind this interface. Net-new to the app — no prior SecureStorage usage exists.
/// </summary>
public interface ISecureStore
{
    Task SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
}
