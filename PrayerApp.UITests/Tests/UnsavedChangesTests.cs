using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 5: Prayer Detail — Unsaved Changes Guard
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "5-UnsavedChanges")]
public class UnsavedChangesTests
{
    private readonly AppiumSetup _setup;
    public UnsavedChangesTests(AppiumSetup setup) => _setup = setup;

    /// <summary>5.1: Edit title → back button → discard dialog appears.</summary>
    [SkippableFact]
    public void UnsavedChanges_EditTitle_BackShowsDiscardDialog()
    {
        _setup.Driver.ResetAppUIState(_setup);
        // iOS back button tap does not fire Shell.Navigating (dotnet/maui#15813, #7351).
        // SwipeBackHelper disables the swipe-back gesture on edit pages, but the native
        // back button in the nav bar is a separate code path that MAUI doesn't intercept.
        // Skip until MAUI fixes Shell.Navigating for iOS back button, or we find a
        // non-BackButtonBehavior workaround (BBB corrupts Shell nav stack — see run history).
        if (TestConfig.IsIOS)
            throw new SkipException(
                "iOS back button does not fire Shell.Navigating — swipe-back is disabled but back button still bypasses guard (MAUI #15813)");

        _setup.Driver.NavigateToNewPrayer(_setup);
        var driver = _setup.Driver;

        driver.EnterText("Detail_Entry_Title", "Dirty Prayer");
        Thread.Sleep(TestConfig.DelayDirtyRegistration);

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterSave);

        var hasAlert = driver.IsAlertPresent();
        var hasDiscardText = driver.IsTextDisplayed("Discard", timeoutSeconds: 2)
                          || driver.IsTextDisplayed("Unsaved", timeoutSeconds: 1);
        var stillOnDetail = driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 2);

        Assert.True(hasAlert || hasDiscardText || stillOnDetail,
            "Discard changes dialog should appear when navigating away with unsaved changes");

        // Clean up: ensure we leave the detail page
        if (hasAlert || hasDiscardText)
        {
            try { driver.TapAlertButton("Discard"); }
            catch { driver.DismissAlertIfPresent(); }
        }
        driver.DismissAlertIfPresent();
        Thread.Sleep(TestConfig.DelayAfterDismiss);

        // Make sure we're back on the Prayers list (not stuck on detail)
        if (driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 2))
        {
            driver.GoBack();
            driver.DismissAlertIfPresent();
            Thread.Sleep(TestConfig.DelayAfterNavigation);
        }
    }
}
