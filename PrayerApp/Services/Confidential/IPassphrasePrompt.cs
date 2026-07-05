namespace PrayerApp.Services.Confidential;

/// <summary>
/// Thin seam over the export-passphrase entry UI, internal to <see cref="IBackupService"/>.
/// Mirrors <see cref="IPinPrompt"/>'s separation of the UI shell from the caller's orchestration
/// logic, so BackupService.ExportAsync's branching (issue #256) stays unit-testable without a
/// MAUI runtime.
/// </summary>
public interface IPassphrasePrompt
{
    /// <summary>
    /// Shows the passphrase-entry popup (minimum length enforced + live strength meter).
    /// Returns the chosen passphrase, or null if the user canceled.
    /// </summary>
    Task<string?> PromptForExportPassphraseAsync();

    /// <summary>
    /// Shows a plain passphrase-ENTRY popup for import (issue #258) — no minimum-length gate and
    /// no strength meter, since the caller is entering a passphrase chosen at export time, not
    /// choosing a new one. Returns the entered passphrase, or null if the user canceled.
    /// </summary>
    Task<string?> PromptForImportPassphraseAsync();
}
