using CommunityToolkit.Maui.Extensions;
using PrayerApp.Views.Confidential;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// Real <see cref="IPinPrompt"/> — shows <see cref="PinPromptPopup"/> via
/// CommunityToolkit.Maui's ShowPopupAsync, mirroring Platforms/*/ColorPickerService's
/// popup-hosting pattern (grabs the current window's page, awaits the popup, reads its result).
/// </summary>
public class MauiPinPrompt : IPinPrompt
{
    public Task<string?> PromptSetPinAsync() => ShowAsync(isSettingNewPin: true);

    public Task<string?> PromptEnterPinAsync() => ShowAsync(isSettingNewPin: false);

    private static async Task<string?> ShowAsync(bool isSettingNewPin)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;

        var popup = new PinPromptPopup(isSettingNewPin);
        await page.ShowPopupAsync(popup, null, CancellationToken.None);

        return popup.EnteredPin;
    }
}
