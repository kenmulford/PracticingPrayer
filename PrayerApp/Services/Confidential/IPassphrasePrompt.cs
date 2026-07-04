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
}
