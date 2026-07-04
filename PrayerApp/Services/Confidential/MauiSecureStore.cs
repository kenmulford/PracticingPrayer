namespace PrayerApp.Services.Confidential;

/// <summary>
/// Real <see cref="ISecureStore"/> — thin wrapper over <c>SecureStorage.Default</c> (net-new
/// to this app; no prior SecureStorage usage exists). On Android, a corrupted value restored
/// from Auto Backup with an invalid encryption key throws on read; that is treated the same
/// as "no PIN set yet" rather than crashing, since the safe fallback is to re-run PIN
/// onboarding. See maui-secure-storage skill guidance on this platform gotcha.
/// </summary>
public class MauiSecureStore : ISecureStore
{
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception)
        {
            SecureStorage.Default.RemoveAll();
            return null;
        }
    }
}
