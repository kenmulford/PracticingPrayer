using PrayerApp.Models;
using SQLite;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// Decrypts and restores the <c>confidential.enc</c> backup sidecar (issue #258, the import side
/// of #256's export). Parameterized by a db file PATH rather than <see cref="IDBService"/>: the
/// rows carry their own original primary-key IDs (so PrayerCardTag junctions resolve to the
/// restored cards/prayers by FK), and sqlite-net-pcl's <c>InsertAsync</c> cannot preserve an
/// explicit value on an <c>[AutoIncrement]</c> column — <c>TableMapping.InsertColumns</c> always
/// excludes autoincrement columns from the generated INSERT statement, so the database would
/// assign a brand-new id and every FK in the payload would break. This importer instead opens its
/// own <see cref="SQLiteAsyncConnection"/> and writes explicit, parameterized
/// <c>INSERT INTO ... (Id, ...) VALUES (?, ...)</c> statements that name the Id column, mirroring
/// <see cref="Services.BackupService"/>'s own <c>BuildScrubbedDbBytesAsync</c> pattern of opening a
/// second raw connection for row-level surgery outside the app's live IDBService connection
/// (see PrayerApp/Services/BackupService.cs — the export-side scrub).
///
/// Because the main <c>prayer_app.db</c> restored by <see cref="Services.BackupService.ImportAsync"/>
/// is the SCRUBBED copy produced by export (confidential rows were deleted before the backup was
/// written), there is no PK collision here — a plain INSERT is correct; INSERT OR REPLACE is not
/// needed for idempotency because a restore is never re-run against rows it already inserted.
///
/// Never throws on a wrong passphrase or corrupt/short blob — those are ordinary, expected outcomes
/// of a user-supplied file, and the caller's contract is "skip the confidential rows, leave the
/// already-restored main DB standing" rather than propagating an exception up into the restore flow.
/// </summary>
internal static class ConfidentialBackupImporter
{
    public readonly record struct ImportResult(
        bool Success,
        int CardsRestored,
        int PrayersRestored,
        int InteractionsRestored,
        int TagJunctionsRestored);

    private static readonly ImportResult Failure = new(false, 0, 0, 0, 0);

    /// <summary>
    /// Decrypts <paramref name="blob"/> with <paramref name="passphrase"/> and inserts the
    /// recovered rows into the SQLite file at <paramref name="dbPath"/>, preserving their original
    /// primary-key IDs. Returns a failed <see cref="ImportResult"/> (never throws) when the
    /// passphrase is wrong or the blob is corrupt/too short/an unsupported format version.
    /// </summary>
    public static async Task<ImportResult> ImportAsync(string dbPath, byte[] blob, string passphrase)
    {
        string json;
        try
        {
            json = ConfidentialBackupCrypto.Decrypt(blob, passphrase);
        }
        catch (Exception)
        {
            // Wrong passphrase (CryptographicException / AuthenticationTagMismatchException) or a
            // malformed/short/unsupported-version header (InvalidDataException / NotSupportedException).
            // Every case is treated the same: skip the confidential restore, touch nothing.
            return Failure;
        }

        ConfidentialExportPayload payload;
        try
        {
            payload = ConfidentialExportPayload.FromJson(json);
        }
        catch (Exception)
        {
            return Failure;
        }

        var connection = new SQLiteAsyncConnection(dbPath);
        try
        {
            // Parents before children so FK columns always reference an already-inserted row:
            // cards -> prayers -> interactions -> tag junctions.
            foreach (var card in payload.Cards)
                await InsertCardAsync(connection, card);
            foreach (var prayer in payload.Prayers)
                await InsertPrayerAsync(connection, prayer);
            foreach (var interaction in payload.Interactions)
                await InsertInteractionAsync(connection, interaction);
            foreach (var junction in payload.TagJunctions)
                await InsertTagJunctionAsync(connection, junction);

            return new ImportResult(
                Success: true,
                CardsRestored: payload.Cards.Count,
                PrayersRestored: payload.Prayers.Count,
                InteractionsRestored: payload.Interactions.Count,
                TagJunctionsRestored: payload.TagJunctions.Count);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static Task<int> InsertCardAsync(SQLiteAsyncConnection connection, PrayerCard card) =>
        connection.ExecuteAsync(
            """
            INSERT INTO PrayerCard
                (Id, Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, IsSystem,
                 IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            card.Id, card.Title, card.CanNotify, card.PrayerFrequency, card.IsAnswered, card.IsFavorite,
            card.IsSystem, card.IsImported, card.ProtectionMode, card.BoxId, card.PreArchiveBoxId,
            card.SystemKey, card.CreatedAt, card.UpdatedAt);

    private static Task<int> InsertPrayerAsync(SQLiteAsyncConnection connection, Prayer prayer) =>
        connection.ExecuteAsync(
            """
            INSERT INTO PrayerRequest
                (Id, PrayerCardId, Title, Details, CanNotify, PrayerFrequency, NotifyHour, NotifyMinute,
                 NotifyDayOfWeek, NotifyDayOfMonth, IsImported, IsAnswered, AnsweredAt, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            prayer.Id, prayer.PrayerCardId, prayer.Title, prayer.Details, prayer.CanNotify,
            prayer.PrayerFrequency, prayer.NotifyHour, prayer.NotifyMinute, prayer.NotifyDayOfWeek,
            prayer.NotifyDayOfMonth, prayer.IsImported, prayer.IsAnswered, prayer.AnsweredAt,
            prayer.CreatedAt, prayer.UpdatedAt);

    private static Task<int> InsertInteractionAsync(SQLiteAsyncConnection connection, PrayerInteraction interaction) =>
        connection.ExecuteAsync(
            """
            INSERT INTO PrayerInteraction (Id, PrayerId, InteractionType, InteractionAt, CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            interaction.Id, interaction.PrayerId, interaction.InteractionType, interaction.InteractionAt,
            interaction.CreatedAt, interaction.UpdatedAt);

    private static Task<int> InsertTagJunctionAsync(SQLiteAsyncConnection connection, PrayerCardTag junction) =>
        connection.ExecuteAsync(
            """
            INSERT INTO PrayerCardTag (Id, PrayerCardId, PrayerTagId, PrayerRequestId, CreatedAt)
            VALUES (?, ?, ?, ?, ?)
            """,
            junction.Id, junction.PrayerCardId, junction.PrayerTagId, junction.PrayerRequestId, junction.CreatedAt);
}
