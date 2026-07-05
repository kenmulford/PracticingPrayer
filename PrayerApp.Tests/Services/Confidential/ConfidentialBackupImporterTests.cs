using PrayerApp.Models;
using PrayerApp.Services.Confidential;
using SQLite;
using Xunit;

namespace PrayerApp.Tests.Services.Confidential;

/// <summary>
/// Round-trip coverage for issue #258's confidential-backup import: decrypt the
/// <c>confidential.enc</c> sidecar (#256's <see cref="ConfidentialBackupCrypto"/>), deserialize it
/// (#256's <see cref="ConfidentialExportPayload"/>), and insert the rows back into a real SQLite
/// file with their ORIGINAL primary-key IDs preserved, so PrayerCardTag junctions resolve to the
/// restored cards/prayers by FK.
///
/// Explicit-PK insert requires a raw parameterized SQL statement (not IDBService.InsertAsync,
/// which — per sqlite-net's TableMapping.InsertColumns — always excludes [AutoIncrement] columns
/// from the INSERT list and lets SQLite assign a new rowid, discarding any Id the caller set).
/// This mirrors BackupService.BuildScrubbedDbBytesAsync's own pattern of opening a second raw
/// SQLiteAsyncConnection for row-level surgery outside the app's live IDBService connection —
/// see PrayerApp/Services/BackupService.cs:184-207 (the export-side scrub).
///
/// The importer is parameterized by a db file PATH (not IDBService), so it is directly
/// constructable here against a temp SQLite file with no MAUI runtime dependency, and
/// BackupService.ImportAsync (MAUI-only) just wires it in after Phase 3 without itself needing
/// test coverage.
/// </summary>
public class ConfidentialBackupImporterTests : IDisposable
{
    private const string Passphrase = "correct horse battery staple";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pp_import_test_{Guid.NewGuid():N}.db");

    static ConfidentialBackupImporterTests()
    {
        // The app's MauiProgram.cs calls this once at startup (see MauiProgram.cs:39). The desktop
        // test host never runs MauiProgram, so this is the sole real-SQLite test fixture that needs
        // to register the native SQLitePCLRaw provider itself before opening a SQLiteAsyncConnection.
        SQLitePCL.Batteries_V2.Init();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    /// <summary>Creates the minimal schema the importer writes into — mirrors DBService.UpdateSchema's CreateTableAsync calls for the four confidential-carrying tables.</summary>
    private async Task<SQLiteAsyncConnection> CreateSchemaAsync()
    {
        var connection = new SQLiteAsyncConnection(_dbPath);
        await connection.CreateTableAsync<PrayerCard>();
        await connection.CreateTableAsync<Prayer>();
        await connection.CreateTableAsync<PrayerInteraction>();
        await connection.CreateTableAsync<PrayerCardTag>();
        await connection.CreateTableAsync<PrayerTag>();
        return connection;
    }

    private static byte[] BuildSidecarBlob(ConfidentialExportPayload payload, string passphrase) =>
        ConfidentialBackupCrypto.Encrypt(payload.ToJson(), passphrase);

    private static ConfidentialExportPayload SamplePayload() => new()
    {
        Cards = new List<PrayerCard>
        {
            new() { Id = 42, Title = "Secret Card", ProtectionMode = CardProtectionMode.Hidden }
        },
        Prayers = new List<Prayer>
        {
            new() { Id = 99, PrayerCardId = 42, Title = "Secret Prayer" }
        },
        Interactions = new List<PrayerInteraction>
        {
            new() { Id = 7, PrayerId = 99, InteractionType = "Prayed" }
        },
        TagJunctions = new List<PrayerCardTag>
        {
            new() { Id = 5, PrayerRequestId = 99, PrayerTagId = 3 }
        }
    };

    // ── Correct passphrase: full round-trip with FK resolution ─────────

    [Fact]
    public async Task ImportAsync_CorrectPassphrase_InsertsCardsWithOriginalIds()
    {
        var connection = await CreateSchemaAsync();
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, Passphrase);

        Assert.True(result.Success);
        var cards = await connection.Table<PrayerCard>().ToListAsync();
        Assert.Single(cards);
        Assert.Equal(42, cards[0].Id);
        Assert.Equal("Secret Card", cards[0].Title);
    }

    [Fact]
    public async Task ImportAsync_CorrectPassphrase_JunctionsResolveToRestoredCardsAndPrayers()
    {
        var connection = await CreateSchemaAsync();
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, Passphrase);

        Assert.True(result.Success);

        var prayers = await connection.Table<Prayer>().ToListAsync();
        Assert.Single(prayers);
        Assert.Equal(99, prayers[0].Id);
        Assert.Equal(42, prayers[0].PrayerCardId); // FK resolves to the restored card's original Id

        var interactions = await connection.Table<PrayerInteraction>().ToListAsync();
        Assert.Single(interactions);
        Assert.Equal(99, interactions[0].PrayerId); // FK resolves to the restored prayer's original Id

        var junctions = await connection.Table<PrayerCardTag>().ToListAsync();
        Assert.Single(junctions);
        Assert.Equal(99, junctions[0].PrayerRequestId); // FK resolves to the restored prayer's original Id

        // Cross-check: the junction's FK actually matches a row that exists in the restored set.
        Assert.Contains(prayers, p => p.Id == junctions[0].PrayerRequestId);
    }

    [Fact]
    public async Task ImportAsync_CorrectPassphrase_ReturnsCountsOfEachRestoredEntityType()
    {
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);
        var connection = await CreateSchemaAsync();

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, Passphrase);

        Assert.True(result.Success);
        Assert.Equal(1, result.CardsRestored);
        Assert.Equal(1, result.PrayersRestored);
        Assert.Equal(1, result.InteractionsRestored);
        Assert.Equal(1, result.TagJunctionsRestored);
    }

    // ── Wrong passphrase: nothing inserted, main data untouched ─────────

    [Fact]
    public async Task ImportAsync_WrongPassphrase_DoesNotInsertAnyRows()
    {
        var connection = await CreateSchemaAsync();
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, "totally wrong passphrase");

        Assert.False(result.Success);
        Assert.Empty(await connection.Table<PrayerCard>().ToListAsync());
        Assert.Empty(await connection.Table<Prayer>().ToListAsync());
        Assert.Empty(await connection.Table<PrayerInteraction>().ToListAsync());
        Assert.Empty(await connection.Table<PrayerCardTag>().ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_WrongPassphrase_LeavesPreExistingMainDataUntouched()
    {
        var connection = await CreateSchemaAsync();
        // Seed a pre-existing, non-confidential row to prove the main DB is never rolled back.
        var untouchedCard = new PrayerCard { Title = "Untouched Public Card" };
        await connection.InsertAsync(untouchedCard);
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, "totally wrong passphrase");

        Assert.False(result.Success);
        var cards = await connection.Table<PrayerCard>().ToListAsync();
        Assert.Single(cards);
        Assert.Equal("Untouched Public Card", cards[0].Title);
    }

    // ── Corrupt / short blob: skipped, no throw ─────────────────────────

    [Fact]
    public async Task ImportAsync_CorruptBlob_ReturnsFailureRatherThanThrowing()
    {
        var connection = await CreateSchemaAsync();
        var blob = BuildSidecarBlob(SamplePayload(), Passphrase);
        blob[^1] ^= 0xFF; // flip a tail bit — tamper with the ciphertext

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, Passphrase);

        Assert.False(result.Success);
        Assert.Empty(await connection.Table<PrayerCard>().ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_TooShortBlob_ReturnsFailureRatherThanThrowing()
    {
        var connection = await CreateSchemaAsync();
        var tooShort = new byte[] { 1, 2, 3 };

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, tooShort, Passphrase);

        Assert.False(result.Success);
        Assert.Empty(await connection.Table<PrayerCard>().ToListAsync());
    }

    // ── Empty payload (no confidential rows) ────────────────────────────

    [Fact]
    public async Task ImportAsync_EmptyPayload_SucceedsWithZeroCounts()
    {
        var connection = await CreateSchemaAsync();
        var blob = BuildSidecarBlob(new ConfidentialExportPayload(), Passphrase);

        var result = await ConfidentialBackupImporter.ImportAsync(_dbPath, blob, Passphrase);

        Assert.True(result.Success);
        Assert.Equal(0, result.CardsRestored);
    }
}
