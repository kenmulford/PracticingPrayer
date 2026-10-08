using NSubstitute;
using PrayerApp.Services;
using Xunit;

namespace PrayerApp.Tests.Services;

public class ShareHandoffServiceTests
{
    private const string SharePrefix = "https://practicingprayerapp.com/share";
    private const string ShareUrl = SharePrefix + "/r?d=abc";

    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IClipboardLinkDetector _detector = Substitute.For<IClipboardLinkDetector>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDeepLinkService _deepLinks = Substitute.For<IDeepLinkService>();
    private readonly IOnboardingService _onboarding = Substitute.For<IOnboardingService>();

    private int _clipboardReads;
    private string? _clipboardText;

    private ShareHandoffService CreateSut() => new(
        _settings, _detector, _navigation, _deepLinks, _onboarding,
        () =>
        {
            _clipboardReads++;
            return Task.FromResult(_clipboardText);
        });

    private void GivenDetectedLinkAndImportTapped(string? clipboardText)
    {
        _detector.HasProbableLinkAsync().Returns(true);
        _navigation.DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _clipboardText = clipboardText;
    }

    [Fact]
    public async Task FirstLaunchWithDetection_ShowsAlertAndSetsFlag()
    {
        _detector.HasProbableLinkAsync().Returns(true);

        await CreateSut().RunAsync();

        Assert.True(_settings.ShareHandoffPrompted);
        await _navigation.Received(1).DisplayConfirmAsync(
            "Did someone share a prayer with you?",
            "Import it from your clipboard.",
            "Import",
            "Not now");
    }

    [Fact]
    public async Task FlagSet_ReturnsFalseWithoutDetection()
    {
        _settings.ShareHandoffPrompted = true;

        var handled = await CreateSut().RunAsync();

        Assert.False(handled);
        await _detector.DidNotReceive().HasProbableLinkAsync();
    }

    [Fact]
    public async Task NothingDetected_ReturnsFalseWithoutAlert()
    {
        _detector.HasProbableLinkAsync().Returns(false);

        var handled = await CreateSut().RunAsync();

        Assert.False(handled);
        Assert.False(_settings.ShareHandoffPrompted);
        await _navigation.DidNotReceive().DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ImportWithTrailingText_HandsOffStrippedUrl()
    {
        GivenDetectedLinkAndImportTapped(ShareUrl + "\nPray");

        var handled = await CreateSut().RunAsync();

        Assert.True(handled);
        Received.InOrder(() =>
        {
            _onboarding.MarkDeepLinkSession();
            _deepLinks.HandleAsync(ShareUrl);
        });
    }

    [Theory]
    [InlineData("hello")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(SharePrefix + "/x?d=abc")]
    [InlineData(SharePrefix + "/r")]
    public async Task ImportWithoutImportableUrl_ShowsFallback(string? clipboardText)
    {
        GivenDetectedLinkAndImportTapped(clipboardText);

        var handled = await CreateSut().RunAsync();

        Assert.False(handled);
        await _deepLinks.DidNotReceive().HandleAsync(Arg.Any<string>());
        await _navigation.Received(1).DisplayAlertAsync(
            "No shared prayer found",
            "Tap the link you received to open it.",
            "OK");
    }

    [Fact]
    public async Task NotNow_DoesNotReadClipboard()
    {
        _detector.HasProbableLinkAsync().Returns(true);
        _navigation.DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var handled = await CreateSut().RunAsync();

        Assert.False(handled);
        Assert.Equal(0, _clipboardReads);
    }
}
