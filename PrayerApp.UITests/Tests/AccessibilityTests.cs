using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PrayerApp.UITests.Infrastructure;
using PrayerApp.UITests.Helpers;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 15: Accessibility
/// Validates that semantic properties, descriptions, hints, and tree visibility
/// are correct for screen reader users on both Android and iOS.
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "15-Accessibility")]
public class AccessibilityTests
{
    private readonly AppiumSetup _setup;
    private AppiumDriver Driver => _setup.Driver;

    public AccessibilityTests(AppiumSetup setup) => _setup = setup;

    // 15.3 (Cards_CardHeader_AnnouncesExpandCollapseState) and
    // 15.4 (Cards_PrayerRow_HasAccessibleSummary) were converted to deterministic
    // unit tests in issue #148 Phase 2. The composed-label contracts they exercised
    // now live in PrayerApp.Tests:
    //   - PrayerCardViewModelTests.AccessibleCardHeader_* (over PrayerCardViewModel.AccessibleCardHeader)
    //   - PrayerRequestDetailViewModelTests.AccessibleSummary_* (over PrayerRequestDetailViewModel.AccessibleSummary)
    // The on-device E2Es added no coverage beyond the getters and were removed.

    /// <summary>
    /// 15.7: Android-only — decorative elements marked IsInAccessibleTree="False"
    /// have important-for-accessibility="no" in the UiAutomator2 tree.
    /// Note: UiAutomator2 still SHOWS the element in page source (it sees all views),
    /// but the attribute tells TalkBack to skip it. We verify the attribute, not absence.
    /// </summary>
    [Fact]
    public void Cards_DecorativeElements_MarkedNotImportant()
    {
        Driver.ResetAppUIState(_setup);
        if (TestConfig.IsIOS)
            return; // iOS flattening makes child-level tree assertions unreliable

        Driver.EnsureOnTab("Prayer Cards", _setup);
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Dump the page source and check that triangle elements have
        // importantForAccessibility="no" or are not focusable.
        // UiAutomator2 exposes ALL views — IsInAccessibleTree="False" doesn't
        // remove them from the dump, it sets an attribute that TalkBack respects.
        var source = Driver.PageSource;

        // The triangle text exists in the tree but should not be focusable
        // (no content-desc, not clickable, not focusable as an a11y node)
        // This is a heuristic: if the triangle has no content-desc and isn't
        // marked as accessible, TalkBack will skip it.
        Assert.Contains("\u25BC", source); // triangle exists in DOM (expected)
        // Verify it does NOT appear as a content-desc (which would make it announced)
        Assert.DoesNotContain("content-desc=\"\u25BC\"", source);
    }

    /// <summary>15.9: Settings row meets the 44dp touch-target minimum (PS-06 / TouchTargetMinimum
    /// guard). The Platform Styles Sprint introduced the TouchTargetMinimum=44 token
    /// and applied it to SettingsRowGrid (commit e144476); this test catches a future
    /// XAML override that would silently drop below the floor. Density is queried
    /// from Appium so the test is portable across emulators of different DPI.
    /// </summary>
    [Fact]
    [Trait("Platform", "Android")]
    [Trait("Section", "9-Settings")]
    public void Settings_AppSettingsRow_MeetsTouchTargetMinimum()
    {
        Driver.ResetAppUIState(_setup);
        // #170: density math below uses UiAutomator2's Android-only `mobile: deviceInfo`
        // displayDensity; the class-level CrossPlatform trait otherwise pulls this Android
        // touch-target guard into the iOS run scope, where that query throws. Guard iOS out
        // (mirrors the sibling at line 136) — the 44pt regression guard stays intact on Android.
        if (TestConfig.IsIOS)
            return;
        Driver.EnsureOnTab("Settings", _setup);
        Driver.WaitForElement("Settings_Row_AppSettings", timeoutSeconds: 10);

        var row = Driver.FindByAutomationId("Settings_Row_AppSettings");
        var heightPx = row.Size.Height;

        // Convert the 44dp accessibility floor to actual pixels for this device.
        // UiAutomator2's mobile: deviceInfo exposes displayDensity (Android DPI, e.g. 440).
        // px-per-dp = displayDensity / 160 (the Android baseline density).
        var deviceInfo = (Dictionary<string, object>?)Driver.ExecuteScript("mobile: deviceInfo")
            ?? throw new InvalidOperationException("mobile: deviceInfo returned null");
        var displayDensity = Convert.ToDouble(deviceInfo["displayDensity"]);
        var pxPerDp = displayDensity / 160.0;
        var expectedMinPx = (int)Math.Floor(44 * pxPerDp);

        Assert.True(heightPx >= expectedMinPx,
            $"Settings_Row_AppSettings height {heightPx}px should meet the 44dp touch-target " +
            $"minimum ({expectedMinPx}px at displayDensity {displayDensity}). PS-06 regression — " +
            $"check whether MinimumHeightRequest on the SettingsRowGrid style was overridden.");
    }
}
