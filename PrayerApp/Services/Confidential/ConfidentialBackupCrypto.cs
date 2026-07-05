using System.Security.Cryptography;
using System.Text;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// AES-GCM + PBKDF2-SHA256 envelope for the <c>confidential.enc</c> backup sidecar (issue #256).
/// Mirrors <see cref="PinHasher"/>'s use of <c>System.Security.Cryptography</c> (no new
/// dependency) but derives a 256-bit AES key instead of a comparison hash, because the
/// sidecar payload must be recovered (decrypted), not just verified.
///
/// Byte layout of the blob produced by <see cref="Encrypt"/> / consumed by <see cref="Decrypt"/>
/// (all integers little-endian, via <see cref="BitConverter"/> on a little-endian runtime —
/// every .NET MAUI target platform is little-endian):
///
/// <code>
/// [0]       byte    FormatVersion (currently 1)
/// [1..4]    int32   Iterations (PBKDF2 iteration count)
/// [5..20]   byte[16] Salt (PBKDF2 salt, random per export)
/// [21..32]  byte[12] Nonce (AES-GCM nonce, random per export — NEVER reused)
/// [33..48]  byte[16] Tag (AES-GCM authentication tag)
/// [49..]    byte[]  Ciphertext (AES-GCM ciphertext of the UTF-8 JSON payload)
/// </code>
///
/// This exact layout is the contract issue #258 (import/decrypt) must parse. Any format change
/// requires bumping <see cref="FormatVersion"/> and branching on it in <see cref="Decrypt"/>.
/// </summary>
internal static class ConfidentialBackupCrypto
{
    public const byte FormatVersion = 1;

    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12; // AesGcm.NonceByteSizes — only 96-bit nonces supported
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32; // 256-bit key
    private const int DefaultIterations = 100_000;

    private const int VersionOffset = 0;
    private const int IterationsOffset = VersionOffset + 1;
    private const int SaltOffset = IterationsOffset + 4;
    private const int NonceOffset = SaltOffset + SaltSizeBytes;
    private const int TagOffset = NonceOffset + NonceSizeBytes;
    private const int CiphertextOffset = TagOffset + TagSizeBytes;

    /// <summary>
    /// Encrypts <paramref name="plaintextJson"/> (UTF-8) with a key derived from
    /// <paramref name="passphrase"/>. Generates a fresh random salt and nonce every call —
    /// callers must never cache or reuse either across exports.
    /// </summary>
    public static byte[] Encrypt(string plaintextJson, string passphrase, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var key = DeriveKey(passphrase, salt, iterations);

        var plaintext = Encoding.UTF8.GetBytes(plaintextJson);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var blob = new byte[CiphertextOffset + ciphertext.Length];
        blob[VersionOffset] = FormatVersion;
        BitConverter.GetBytes(iterations).CopyTo(blob, IterationsOffset);
        salt.CopyTo(blob, SaltOffset);
        nonce.CopyTo(blob, NonceOffset);
        tag.CopyTo(blob, TagOffset);
        ciphertext.CopyTo(blob, CiphertextOffset);

        return blob;
    }

    /// <summary>
    /// Decrypts a blob produced by <see cref="Encrypt"/>. Throws
    /// <see cref="CryptographicException"/> (or <see cref="AuthenticationTagMismatchException"/>
    /// on .NET 8+) when <paramref name="passphrase"/> is wrong — the AES-GCM tag will not
    /// verify against a key derived from the wrong passphrase.
    /// </summary>
    public static string Decrypt(byte[] blob, string passphrase)
    {
        if (blob.Length < CiphertextOffset)
            throw new InvalidDataException("Confidential backup blob is too short to contain a valid header.");

        var version = blob[VersionOffset];
        if (version != FormatVersion)
            throw new NotSupportedException($"Unsupported confidential backup format version: {version}.");

        var iterations = BitConverter.ToInt32(blob, IterationsOffset);
        var salt = blob[SaltOffset..NonceOffset];
        var nonce = blob[NonceOffset..TagOffset];
        var tag = blob[TagOffset..CiphertextOffset];
        var ciphertext = blob[CiphertextOffset..];

        var key = DeriveKey(passphrase, salt, iterations);
        var plaintext = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);
}
