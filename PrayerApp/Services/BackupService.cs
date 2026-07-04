using System.IO.Compression;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.Messaging;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services.Confidential;

namespace PrayerApp.Services;

public class BackupService : IBackupService
{
    private readonly IDBService _dbService;
    private readonly ICardService _cardService;
    private readonly IPrayerService _prayerService;
    private readonly ITagService _tagService;
    private readonly IBoxService _boxService;
    private readonly INotificationService _notificationService;
    private readonly IPassphrasePrompt _passphrasePrompt;
    private readonly IMessenger _messenger;
    private readonly string _dbPath;

    private const string ProtectAction = "Protect with a Passphrase";
    private const string UnencryptedAction = "Continue Unencrypted";

    public BackupService(IDBService dbService, ICardService cardService,
        IPrayerService prayerService, ITagService tagService, IBoxService boxService,
        INotificationService notificationService, IPassphrasePrompt passphrasePrompt,
        IMessenger messenger)
    {
        _dbService = dbService;
        _cardService = cardService;
        _prayerService = prayerService;
        _tagService = tagService;
        _boxService = boxService;
        _notificationService = notificationService;
        _passphrasePrompt = passphrasePrompt;
        _messenger = messenger;
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "prayer_app.db");
    }

    public async Task<bool> ExportAsync()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var fileName = $"practicing_prayer_{today}.pcrd";
        var tempZipPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        var tempScrubbedDbPath = Path.Combine(FileSystem.CacheDirectory, $"prayer_app_scrub_{Guid.NewGuid():N}.db");

        try
        {
            // Clear any stale backup files from cache before creating a new one.
            // We do NOT delete after sharing because the receiving app (Google Drive,
            // Files, etc.) reads the file asynchronously after the share sheet closes.
            // The OS will evict the cache directory on its own schedule.
            foreach (var old in Directory.GetFiles(FileSystem.CacheDirectory, "*.pcrd"))
                File.Delete(old);

            // Confidential-card detection (#256, depends on #250's GetEffectiveProtectionMode):
            // load cards + boxes to resolve each card's effective protection mode. Cards and
            // boxes are cheap, cached reads — no DB close needed for this check.
            var cards = await _cardService.GetCardsAsync();
            var boxes = await _boxService.GetBoxesAsync();
            var hasConfidentialCards = ConfidentialExportSplitter.HasAnyConfidentialCard(cards, boxes);

            string? passphrase = null;
            if (hasConfidentialCards)
            {
                var choice = await Shell.Current.DisplayActionSheetAsync(
                    "This backup includes confidential cards",
                    "Cancel", null, ProtectAction, UnencryptedAction);

                if (choice is null or "Cancel")
                    return false;

                if (choice == ProtectAction)
                {
                    passphrase = await _passphrasePrompt.PromptForExportPassphraseAsync();
                    if (passphrase is null)
                        return false; // user canceled the passphrase entry
                }
                else
                {
                    // Unencrypted path: warm, plain notice before the OS share sheet — the user
                    // must see this before their confidential cards leave the device in plaintext.
                    var confirmed = await Shell.Current.DisplayAlertAsync(
                        "Backup Will Include Confidential Cards",
                        "Your confidential cards will be saved in this backup as plain text, just like the rest of your data. Anyone who opens the backup file can read them.",
                        "Continue", "Cancel");
                    if (!confirmed)
                        return false;
                }
            }

            // Close DB with WAL checkpoint to ensure the .db file is complete
            await _dbService.CloseAsync();

            // Read the DB bytes while the connection is closed
            byte[] dbBytes = await File.ReadAllBytesAsync(_dbPath);

            // Reopen the DB immediately — connection is unavailable for milliseconds only
            await _dbService.ReinitializeAsync(_dbPath);

            byte[]? confidentialBlob = null;
            byte[] dbEntryBytes = dbBytes;

            if (passphrase is not null)
            {
                // Passphrase path: operate on a COPY of the db bytes — the live DB (already
                // reopened above) is never touched. Scrub the copy of confidential rows, and
                // encrypt those same rows into the confidential.enc sidecar payload.
                var prayers = await _prayerService.GetAllPrayersAsync();
                var interactions = await _dbService.GetAllAsync<PrayerInteraction>();
                var tagJunctions = await _dbService.GetAllAsync<PrayerCardTag>();

                var split = ConfidentialExportSplitter.Split(cards, boxes, prayers, interactions, tagJunctions);

                var payload = new ConfidentialExportPayload
                {
                    Cards = split.ConfidentialCards,
                    Prayers = split.ConfidentialPrayers,
                    Interactions = split.ConfidentialInteractions,
                    TagJunctions = split.ConfidentialTagJunctions
                };
                confidentialBlob = ConfidentialBackupCrypto.Encrypt(payload.ToJson(), passphrase);

                dbEntryBytes = await BuildScrubbedDbBytesAsync(dbBytes, tempScrubbedDbPath, split);
            }

            // Build the .pcrd ZIP in the cache directory
            await using (var zipStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
            {
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

                var dbEntry = archive.CreateEntry("prayer_app.db", CompressionLevel.Optimal);
                await using (var dbEntryStream = dbEntry.Open())
                    await dbEntryStream.WriteAsync(dbEntryBytes);

                if (confidentialBlob is not null)
                {
                    var encEntry = archive.CreateEntry("confidential.enc", CompressionLevel.Optimal);
                    await using var encEntryStream = encEntry.Open();
                    await encEntryStream.WriteAsync(confidentialBlob);
                }
            }

            // Share via OS share sheet — lets user save to Google Drive, Files, email, etc.
            // More reliable than IFileSaver on Android (avoids onActivityResult crash on API 36).
            // Share.RequestAsync returns immediately after dispatching the intent —
            // there is no completion callback, so no success toast here.
            // The share sheet itself is the UX confirmation.
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Save Practicing Prayer Backup",
                File = new ShareFile(tempZipPath, "application/zip")
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupService.ExportAsync] {ex}");
            await Toast.Make("Backup failed").Show();
            return false;
        }
        finally
        {
            if (File.Exists(tempScrubbedDbPath))
                File.Delete(tempScrubbedDbPath);
        }
    }

    /// <summary>
    /// Writes <paramref name="sourceDbBytes"/> to a temp file, opens a SEPARATE connection to
    /// that copy (the live DB, already reopened by the caller, is never touched), deletes the
    /// confidential rows identified by <paramref name="split"/>, checkpoints, and returns the
    /// scrubbed bytes. This is what becomes the ZIP's <c>prayer_app.db</c> entry when a
    /// passphrase sidecar is present.
    /// </summary>
    private static async Task<byte[]> BuildScrubbedDbBytesAsync(
        byte[] sourceDbBytes, string scrubDbPath,
        ConfidentialExportSplitter.SplitResult split)
    {
        await File.WriteAllBytesAsync(scrubDbPath, sourceDbBytes);

        var scrubConnection = new SQLite.SQLiteAsyncConnection(scrubDbPath);
        try
        {
            foreach (var card in split.ConfidentialCards)
                await scrubConnection.ExecuteAsync("DELETE FROM PrayerCard WHERE Id = ?", card.Id);
            foreach (var prayer in split.ConfidentialPrayers)
                await scrubConnection.ExecuteAsync("DELETE FROM PrayerRequest WHERE Id = ?", prayer.Id);
            foreach (var interaction in split.ConfidentialInteractions)
                await scrubConnection.ExecuteAsync("DELETE FROM PrayerInteraction WHERE Id = ?", interaction.Id);
            foreach (var junction in split.ConfidentialTagJunctions)
                await scrubConnection.ExecuteAsync("DELETE FROM PrayerCardTag WHERE Id = ?", junction.Id);

            // Checkpoint so the scrubbed rows are flushed from WAL into the main file before
            // it's read back off disk (same PRAGMA gotcha as DBService.CloseAsync — this PRAGMA
            // returns a result row, so it must use ExecuteScalarAsync, not ExecuteAsync).
            await scrubConnection.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE)");
        }
        finally
        {
            await scrubConnection.CloseAsync();
        }

        return await File.ReadAllBytesAsync(scrubDbPath);
    }

    public async Task<bool> ImportAsync()
    {
        // Pick file BEFORE showing any modal (iOS constraint: UIDocumentPicker conflicts with modal)
        FileResult? picked = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Select a Practicing Prayer backup (.pcrd)"
        });
        if (picked is null) return false;

        // Validate: must be a ZIP containing prayer_app.db
        byte[] dbBytes;
        try
        {
            await using var pickedStream = await picked.OpenReadAsync();
            using var archive = new ZipArchive(pickedStream, ZipArchiveMode.Read);
            var entry = archive.GetEntry("prayer_app.db");
            if (entry is null)
            {
                await Shell.Current.DisplayAlertAsync("Invalid Backup",
                    "This file doesn't appear to be a valid Practicing Prayer backup.", "OK");
                return false;
            }
            await using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms);
            dbBytes = ms.ToArray();
        }
        catch
        {
            await Shell.Current.DisplayAlertAsync("Invalid Backup",
                "This file doesn't appear to be a valid Practicing Prayer backup.", "OK");
            return false;
        }

        // Push blocking modal — only after successful validation
        var progressPage = new Views.Backup.RestoreProgressPage();
        await Shell.Current.Navigation.PushModalAsync(progressPage);

        try
        {
            var dir = Path.GetDirectoryName(_dbPath)!;
            var restorePath = Path.Combine(dir, "prayer_app_restore.db");
            var backupTmpPath = Path.Combine(dir, "prayer_app_backup.tmp");

            // Phase 1 — Write incoming DB (original untouched)
            await File.WriteAllBytesAsync(restorePath, dbBytes);

            // Phase 2 — Swap
            await _dbService.CloseAsync();
            File.Move(_dbPath, backupTmpPath, overwrite: true);
            File.Move(restorePath, _dbPath, overwrite: true);

            // Phase 3 — Reinitialize + invalidate all caches
            await _dbService.ReinitializeAsync(_dbPath);
            _cardService.InvalidateCache();
            _prayerService.InvalidateCache();
            _tagService.InvalidateCache();
            File.Delete(backupTmpPath);

            // Single summary signal — restore touched every entity table.
            _messenger.Send(new BulkChangedMessage());

            // Phase 4 — Reschedule notifications for restored prayers
            try
            {
                var prayers = await _prayerService.GetAllPrayersAsync();
                foreach (var prayer in prayers.Where(p => p.CanNotify))
                    await _notificationService.ScheduleAsync(prayer);
            }
            catch (Exception notifyEx)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] Notification reschedule failed: {notifyEx.Message}");
            }

            await Shell.Current.GoToAsync("//MainPage");
            await Shell.Current.Navigation.PopModalAsync();
            await Toast.Make("Restore complete").Show();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupService.ImportAsync] {ex}");

            // Attempt to reopen the DB so the app remains usable
            try { await _dbService.ReinitializeAsync(_dbPath); }
            catch { /* DB may be gone — user must restart */ }

            await Shell.Current.Navigation.PopModalAsync();
            await Shell.Current.DisplayAlertAsync("Restore Failed",
                "Restore failed. Please restart the app.", "OK");
            return false;
        }
    }
}
