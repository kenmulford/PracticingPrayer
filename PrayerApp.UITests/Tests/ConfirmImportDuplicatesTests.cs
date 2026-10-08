using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// #313: importing into an existing card skips rows already on that card.
/// Cancels out at the end, so the seeded card is never mutated.
/// </summary>
[Collection("Appium")]
[Trait("Platform", "Android")]
[Trait("Section", "14-Android")]
public class ConfirmImportDuplicatesTests
{
    private readonly AppiumSetup _setup;
    public ConfirmImportDuplicatesTests(AppiumSetup setup) => _setup = setup;

    [SkippableFact]
    public void ImportExisting_SkipsDuplicates()
    {
        if (TestConfig.IsIOS)
            throw new SkipException("Android-only: PROCESS_TEXT is the Android selection-toolbar entry point");

        _setup.Driver.ForceStopApp();
        _setup.Driver.LaunchProcessTextIntent(_setup, TestSeedFixtures.ImportSkipsDuplicatesPrayer);

        var stagedTitle = _setup.Driver.WaitForElement("ConfirmImport_Entry_PrayerTitle");
        // Android prepends the accessibility description ("Prayer title, item 1 of 1, ...") to the Entry text.
        Assert.True(stagedTitle.Text.EndsWith(TestSeedFixtures.ImportSkipsDuplicatesPrayer, StringComparison.Ordinal),
            $"The intent should stage the full shared title, but the first row reads '{stagedTitle.Text}'");

        _setup.Driver.WaitAndTap("ConfirmImport_Seg_ExistingCard");
        _setup.Driver.ScrollDownToText(TestSeedFixtures.ImportSkipsDuplicatesCard);
        _setup.Driver.TapByText(TestSeedFixtures.ImportSkipsDuplicatesCard);

        Assert.True(_setup.Driver.IsDisplayed("ConfirmImport_Lbl_DuplicatesHeader", timeoutSeconds: 5),
            "The matching row should move to Already on this card");
        var allDuplicates = _setup.Driver.WaitForElement("ConfirmImport_Lbl_AllDuplicates", timeoutSeconds: 5);
        Assert.NotNull(allDuplicates);
        Assert.Equal(
            $"Everything in this share is already on {TestSeedFixtures.ImportSkipsDuplicatesCard}.",
            allDuplicates.Text);

        _setup.Driver.WaitAndTap("ConfirmImport_Btn_AddDuplicate");

        _setup.Driver.WaitForElementGone("ConfirmImport_Lbl_AllDuplicates");
        Assert.True(_setup.Driver.IsDisplayed("ConfirmImport_Lbl_DuplicateTag", timeoutSeconds: 5),
            "The moved row should carry the Already on this card tag");

        _setup.Driver.WaitAndTap("ConfirmImport_Btn_Cancel");
        Thread.Sleep(TestConfig.DelayAfterDismiss);
    }
}
