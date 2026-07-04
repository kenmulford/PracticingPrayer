using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 14: Android-Specific
/// </summary>
[Collection("Appium")]
[Trait("Platform", "Android")]
[Trait("Section", "14-Android")]
public class AndroidTests
{
    private readonly AppiumSetup _setup;
    public AndroidTests(AppiumSetup setup) => _setup = setup;

    /// <summary>
    /// 14.1: Clean hardware-back from a Settings sub-page pops to the hub with no
    /// discard dialog. Restores the dedicated E2E for the CLEAN pop retired during
    /// the #148 cull (original: <c>HardwareBack_NavigatesFromSubPages</c>,
    /// <c>AndroidTests.cs</c>, deleted in #149) — see #224. The DIRTY guard variant
    /// survives as
    /// <see cref="UnsavedChangesTests.UnsavedChanges_EditTitle_BackShowsDiscardDialog"/>,
    /// but that test lands on the New-Prayer page, not a Settings sub-page, so it does
    /// not cover this path. <c>GoBack()</c> == <c>Navigate().Back()</c> == Android
    /// hardware back (see <see cref="AppExtensions.GoBack"/>).
    /// </summary>
    [SkippableFact]
    public void HardwareBack_CleanSettingsSubPage_PopsToHubNoDiscardDialog()
    {
        _setup.Driver.ResetAppUIState(_setup);
        if (TestConfig.IsIOS)
            throw new SkipException("Android-only: hardware back button does not exist on iOS");

        var driver = _setup.Driver;
        driver.EnsureOnTab("Settings", _setup);

        driver.WaitAndTap("Settings_Row_AppSettings");
        driver.WaitForElement("AppSettings_Switch_Notifications", timeoutSeconds: 10);

        // CLEAN case: no edits made on the sub-page, so there is no unsaved state for
        // the back-guard to intercept.
        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.False(driver.IsAlertPresent(),
            "Clean back from a Settings sub-page with no unsaved edits should not show any dialog");
        Assert.False(driver.IsTextDisplayed("Discard", timeoutSeconds: 1),
            "Clean back from a Settings sub-page with no unsaved edits should not show a discard dialog");

        Assert.True(driver.IsDisplayed("Settings_Row_AppSettings", timeoutSeconds: 10),
            "Hardware back should pop from the sub-page straight back to the Settings hub");
    }
}
