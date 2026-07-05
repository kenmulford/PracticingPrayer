using CommunityToolkit.Maui.Views;

namespace PrayerApp.Views.Confidential;

/// <summary>
/// PIN-entry popup used both for first-enable-protection onboarding (setting a new 6-digit
/// PIN) and for the biometric-fallback verify prompt (entering the existing PIN). The two
/// modes only differ in copy — the entered value and cancel/confirm flow are identical.
/// Mirrors the CommunityToolkit.Maui Popup + ShowPopupAsync pattern used by
/// Views/Tags/ColorPickerPopup and Platforms/*/ColorPickerService.
/// </summary>
public partial class PinPromptPopup : Popup
{
    /// <summary>The entered 6-digit PIN, or null if the user canceled.</summary>
    public string? EnteredPin { get; private set; }

    public PinPromptPopup(bool isSettingNewPin)
    {
        InitializeComponent();

        TitleLabel.Text = isSettingNewPin ? "Set a PIN" : "Enter your PIN";
        SubtitleLabel.Text = isSettingNewPin
            ? "This PIN unlocks protected cards when biometrics aren't available."
            : "Biometric authentication isn't available. Enter your PIN to continue.";

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            PinEntry.SetSemanticFocus();
        };
    }

    private void OnPinTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;
        var isValid = text.Length == 6 && text.All(char.IsDigit);
        ConfirmButton.IsEnabled = isValid;
        ErrorLabel.IsVisible = text.Length > 0 && !isValid;
    }

    private async void OnCancel(object? sender, EventArgs e)
    {
        EnteredPin = null;
        await CloseAsync(CancellationToken.None);
    }

    private async void OnConfirm(object? sender, EventArgs e)
    {
        EnteredPin = PinEntry.Text;
        await CloseAsync(CancellationToken.None);
    }
}
