using System.Security.Cryptography;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// Salted-hash + constant-time verify for the 6-digit confidential-access PIN. The PIN is
/// never stored in plaintext: only "salt:hash" (both Base64) is persisted, via
/// <see cref="ISecureStore"/>. Uses PBKDF2-HMACSHA256 (non-reversible, purpose-built for
/// low-entropy secrets like a 6-digit PIN) with a random per-install salt, so two installs
/// choosing the same PIN never produce the same stored hash.
/// </summary>
internal static class PinHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    /// <summary>Generates "saltBase64:hashBase64" for a new PIN. Store this string; never the raw PIN.</summary>
    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Derive(pin, salt);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies <paramref name="pin"/> against a "saltBase64:hashBase64" record produced by
    /// <see cref="Hash"/>. Uses a constant-time comparison so response timing does not leak
    /// how many leading bytes matched. Returns false (never throws) for a malformed record.
    /// </summary>
    public static bool Verify(string pin, string storedRecord)
    {
        var parts = storedRecord.Split(':', 2);
        if (parts.Length != 2)
            return false;

        byte[] salt, expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expectedHash = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Derive(pin, salt);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
}
