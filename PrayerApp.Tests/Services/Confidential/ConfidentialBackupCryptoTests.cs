using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services.Confidential;

/// <summary>
/// Direct unit coverage for the confidential-backup AES-GCM + PBKDF2 envelope (issue #256).
/// Exercises the round-trip, wrong-passphrase-fails, and nonce/salt-freshness invariants that
/// the export/import contract depends on, independently of BackupService's file/DB orchestration.
/// </summary>
public sealed class BackupFixtureFactAttribute : FactAttribute
{
    public const string VariableName = "PP_WRITE_BACKUP_FIXTURES";

    public BackupFixtureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(VariableName) != "1")
            Skip = $"{VariableName} is not 1; the fixture writer runs on demand only";
    }
}

public class ConfidentialBackupCryptoTests
{
    private const string FixtureDirectory = "contracts/backup";

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixtureDirectory, name));

    private static string FixturePassphrase() => ReadFixture("passphrase.txt").Trim();

    private const string SamplePayload = """{"SchemaVersion":1,"Cards":[],"Prayers":[]}""";

    [Fact]
    public void Decrypt_CorrectPassphrase_RecoversExactPayload()
    {
        var blob = ConfidentialBackupCrypto.Encrypt(SamplePayload, "correct horse battery staple");

        var recovered = ConfidentialBackupCrypto.Decrypt(blob, "correct horse battery staple");

        Assert.Equal(SamplePayload, recovered);
    }

    [Fact]
    public void Decrypt_WrongPassphrase_ThrowsRatherThanReturningGarbage()
    {
        var blob = ConfidentialBackupCrypto.Encrypt(SamplePayload, "correct horse battery staple");

        Assert.ThrowsAny<CryptographicException>(() =>
            ConfidentialBackupCrypto.Decrypt(blob, "wrong passphrase entirely"));
    }

    [Fact]
    public void Encrypt_TwoCallsSamePassphrase_ProduceDifferentBlobs()
    {
        // Fresh random salt + nonce per export — the defining property that prevents two
        // exports of the same data with the same passphrase from ever producing identical
        // ciphertext (which would otherwise leak that the underlying data hadn't changed).
        var blobA = ConfidentialBackupCrypto.Encrypt(SamplePayload, "shared-passphrase-1234");
        var blobB = ConfidentialBackupCrypto.Encrypt(SamplePayload, "shared-passphrase-1234");

        Assert.NotEqual(blobA, blobB);

        // Both still decrypt correctly with their own salt/nonce embedded in the header.
        Assert.Equal(SamplePayload, ConfidentialBackupCrypto.Decrypt(blobA, "shared-passphrase-1234"));
        Assert.Equal(SamplePayload, ConfidentialBackupCrypto.Decrypt(blobB, "shared-passphrase-1234"));
    }

    [Fact]
    public void Encrypt_BlobStartsWithCurrentFormatVersion()
    {
        var blob = ConfidentialBackupCrypto.Encrypt(SamplePayload, "some-passphrase-1234");

        Assert.Equal(ConfidentialBackupCrypto.FormatVersion, blob[0]);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsDueToTagMismatch()
    {
        var blob = ConfidentialBackupCrypto.Encrypt(SamplePayload, "correct horse battery staple");
        blob[^1] ^= 0xFF; // flip a bit in the ciphertext tail

        Assert.ThrowsAny<CryptographicException>(() =>
            ConfidentialBackupCrypto.Decrypt(blob, "correct horse battery staple"));
    }

    [Fact]
    public void Decrypt_NativeFixture_YieldsPayloadFixture() => AssertFixtureYieldsPayload("native/confidential.enc");

    [Fact]
    public void Decrypt_MauiFixture_YieldsPayloadFixture() => AssertFixtureYieldsPayload("maui/confidential.enc");

    private static void AssertFixtureYieldsPayload(string relativePath)
    {
        var blob = File.ReadAllBytes(Path.Combine(FixtureDirectory, relativePath));

        var decrypted = ConfidentialBackupCrypto.Decrypt(blob, FixturePassphrase());

        Assert.Equal(
            ConfidentialExportPayload.FromJson(ReadFixture("payload.json")).ToJson(),
            ConfidentialExportPayload.FromJson(decrypted).ToJson());
    }

    [BackupFixtureFact]
    public void WriteMauiFixture()
    {
        var backupDir = RepoBackupDirectory();
        var payload = File.ReadAllText(Path.Combine(backupDir, "payload.json"));
        var passphrase = File.ReadAllText(Path.Combine(backupDir, "passphrase.txt")).Trim();
        File.WriteAllBytes(
            Path.Combine(backupDir, "maui", "confidential.enc"),
            ConfidentialBackupCrypto.Encrypt(payload, passphrase));
    }

    private static string RepoBackupDirectory([CallerFilePath] string sourceFile = "")
    {
        var repoRoot = sourceFile;
        for (var i = 0; i < 4; i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        return Path.Combine(repoRoot, "contracts", "backup");
    }
}
