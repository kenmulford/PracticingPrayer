namespace PrayerApp.Services;

/// <summary>
/// First-launch handoff for a user who installed the app from a shared link: when the
/// clipboard probably holds a link, offers to import it, then hands the pasted share
/// URL to <see cref="IDeepLinkService"/>. The clipboard text is read only after the
/// user taps Import, so no OS paste notice appears before they opt in.
/// </summary>
public sealed class ShareHandoffService
{
    private const string SharePrefix = "https://practicingprayerapp.com/share";

    private readonly ISettings _settings;
    private readonly IClipboardLinkDetector _detector;
    private readonly INavigationService _navigation;
    private readonly IDeepLinkService _deepLinks;
    private readonly IOnboardingService _onboarding;
    private readonly Func<Task<string?>> _readClipboardText;

    public ShareHandoffService(
        ISettings settings,
        IClipboardLinkDetector detector,
        INavigationService navigation,
        IDeepLinkService deepLinks,
        IOnboardingService onboarding,
        Func<Task<string?>> readClipboardText)
    {
        _settings = settings;
        _detector = detector;
        _navigation = navigation;
        _deepLinks = deepLinks;
        _onboarding = onboarding;
        _readClipboardText = readClipboardText;
    }

    /// <summary>
    /// Returns true when a share URL was handed to the deep-link service, so the caller
    /// skips the welcome popup. The prompted flag is set before the alert appears, so an
    /// app kill or a "Not now" never re-prompts.
    /// </summary>
    public async Task<bool> RunAsync()
    {
        if (_settings.ShareHandoffPrompted || !await _detector.HasProbableLinkAsync())
            return false;

        _settings.ShareHandoffPrompted = true;

        var import = await _navigation.DisplayConfirmAsync(
            "Did someone share a prayer with you?",
            "Import it from your clipboard.",
            "Import",
            "Not now");
        if (!import)
            return false;

        var url = ExtractShareUrl(await _readClipboardText());
        if (url is null)
        {
            await _navigation.DisplayAlertAsync(
                "No shared prayer found",
                "Tap the link you received to open it.",
                "OK");
            return false;
        }

        _onboarding.MarkDeepLinkSession();
        await _deepLinks.HandleAsync(url);
        return true;
    }

    // Mirrors MauiProgram.HandleDeepLink: the share message appends human-readable text
    // after the URL, so the URL ends at the first whitespace.
    private static string? ExtractShareUrl(string? text)
    {
        var trimmed = text?.TrimStart();
        if (trimmed is null || !trimmed.StartsWith(SharePrefix, StringComparison.Ordinal))
            return null;

        var end = trimmed.IndexOfAny(new[] { '\n', '\r', ' ' });
        var url = end >= 0 ? trimmed[..end] : trimmed;
        return DeepLinkService.IsImportableShareUri(url) ? url : null;
    }
}
