using CommunityToolkit.Maui.Extensions;
using PrayerApp.Views.Confidential;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// Real <see cref="IPassphrasePrompt"/> — shows <see cref="PassphrasePromptPopup"/> via
/// CommunityToolkit.Maui's ShowPopupAsync, mirroring <see cref="MauiPinPrompt"/>'s
/// popup-hosting pattern (grabs the current window's page, awaits the popup, reads its result).
/// </summary>
public class MauiPassphrasePrompt : IPassphrasePrompt
{
    public async Task<string?> PromptForExportPassphraseAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;

        var popup = new PassphrasePromptPopup();
        await page.ShowPopupAsync(popup, null, CancellationToken.None);

        return popup.EnteredPassphrase;
    }
}
