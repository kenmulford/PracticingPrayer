using CommunityToolkit.Maui.Views;
using PrayerApp.Services.Confidential;

namespace PrayerApp.Views.Confidential;

/// <summary>
/// Passphrase-entry popup shared by confidential-aware backup export (issue #256) and import
/// (issue #258). The two modes only differ in copy and validation — mirrors
/// <see cref="PinPromptPopup"/>'s single-popup, mode-flag pattern:
/// <list type="bullet">
/// <item>Export (<c>isImporting: false</c>): minimum 12 characters + live strength meter, no
/// rigid character-class rule — the user is CHOOSING a new passphrase.</item>
/// <item>Import (<c>isImporting: true</c>): plain non-empty entry, no minimum-length gate and no
/// strength meter — the user is RE-ENTERING a passphrase already chosen at export time.</item>
/// </list>
/// Mirrors the CommunityToolkit.Maui Popup + ShowPopupAsync pattern used by PinPromptPopup and
/// Views/Tags/ColorPickerPopup.
/// </summary>
public partial class PassphrasePromptPopup : Popup
{
    private readonly bool _isImporting;

    /// <summary>The entered passphrase, or null if the user canceled.</summary>
    public string? EnteredPassphrase { get; private set; }

    public PassphrasePromptPopup(bool isImporting = false)
    {
        InitializeComponent();
        _isImporting = isImporting;

        if (isImporting)
        {
            TitleLabel.Text = "Enter the backup passphrase";
            SubtitleLabel.Text = "This backup includes confidential cards protected by a passphrase. Enter it to restore them.";
            PassphraseEntry.Placeholder = "Passphrase";
            SemanticProperties.SetHint(PassphraseEntry, "Enter the backup passphrase");
            ConfirmButton.Text = "Restore";
        }
        else
        {
            TitleLabel.Text = "Protect with a passphrase";
            SubtitleLabel.Text = "Choose a passphrase to encrypt your confidential cards in this backup. You'll need it to restore them later — we can't recover it for you.";
            PassphraseEntry.Placeholder = "Passphrase (at least 12 characters)";
            SemanticProperties.SetHint(PassphraseEntry, "Enter a passphrase of at least 12 characters");
            ConfirmButton.Text = "Protect Backup";
        }

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            PassphraseEntry.SetSemanticFocus();
        };
    }

    private void OnPassphraseTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;

        if (_isImporting)
        {
            // Import: no minimum length, no strength meter — just require non-empty.
            ConfirmButton.IsEnabled = text.Length > 0;
            ErrorLabel.IsVisible = false;
            StrengthLabel.IsVisible = false;
            return;
        }

        var meetsMinimum = PassphrasePolicy.MeetsMinimumLength(text);

        ConfirmButton.IsEnabled = meetsMinimum;
        ErrorLabel.IsVisible = text.Length > 0 && !meetsMinimum;

        if (text.Length == 0)
        {
            StrengthLabel.IsVisible = false;
            return;
        }

        var strength = PassphrasePolicy.GetStrength(text);
        StrengthLabel.IsVisible = true;
        (StrengthLabel.Text, StrengthLabel.TextColor) = strength switch
        {
            PassphraseStrength.TooShort => ("Keep going…", (Color)Application.Current!.Resources["DangerRed"]),
            PassphraseStrength.Weak => ("Strength: Weak", (Color)Application.Current!.Resources["DangerRed"]),
            PassphraseStrength.Fair => ("Strength: Fair", (Color)Application.Current!.Resources["TagOrange"]),
            _ => ("Strength: Strong", (Color)Application.Current!.Resources["SuccessGreen"])
        };
    }

    private async void OnCancel(object? sender, EventArgs e)
    {
        EnteredPassphrase = null;
        await CloseAsync(CancellationToken.None);
    }

    private async void OnConfirm(object? sender, EventArgs e)
    {
        EnteredPassphrase = PassphraseEntry.Text;
        await CloseAsync(CancellationToken.None);
    }
}
