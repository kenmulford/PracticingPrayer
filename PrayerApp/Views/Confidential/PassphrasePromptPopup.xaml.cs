using CommunityToolkit.Maui.Views;
using PrayerApp.Services.Confidential;

namespace PrayerApp.Views.Confidential;

/// <summary>
/// Passphrase-entry popup for confidential-aware backup export (issue #256): minimum 12
/// characters, live strength meter, no rigid character-class rule. Mirrors the
/// CommunityToolkit.Maui Popup + ShowPopupAsync pattern used by PinPromptPopup and
/// Views/Tags/ColorPickerPopup.
/// </summary>
public partial class PassphrasePromptPopup : Popup
{
    /// <summary>The entered passphrase, or null if the user canceled.</summary>
    public string? EnteredPassphrase { get; private set; }

    public PassphrasePromptPopup()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            PassphraseEntry.SetSemanticFocus();
        };
    }

    private void OnPassphraseTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;
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
