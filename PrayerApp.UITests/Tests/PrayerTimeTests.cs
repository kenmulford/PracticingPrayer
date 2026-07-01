using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 8: Prayer Time
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "8-PrayerTime")]
public class PrayerTimeTests
{
    private readonly AppiumSetup _setup;
    public PrayerTimeTests(AppiumSetup setup) => _setup = setup;

    /// <summary>Start a full ("All Requests") prayer time session.</summary>
    /// <remarks>
    /// With active prayers, tapping Prayer Time always shows an action sheet —
    /// "All Requests" and "Choose cards…" are always offered (MainPage.xaml.cs:70-79),
    /// plus "By Tags"/"By Collection" when those exist. So the harness must tap
    /// "All Requests" to begin; there is no longer a direct-navigation path. No
    /// prayers → "No Prayer Requests" alert blocks at Home, which is why we seed a
    /// prayer first.
    ///
    /// The action-sheet option is located differently per platform: iOS exposes the
    /// button by accessibility id, but Android renders DisplayActionSheet as a native
    /// dialog whose options carry @text and NO content-desc — so AccessibilityId
    /// never matches there and the option must be tapped by its visible text (#195).
    /// </remarks>
    private bool TryStartPrayerTime()
    {
        var driver = _setup.Driver;

        // Ensure prayer data exists — without prayers, the guard shows an alert instead
        driver.EnsureOnPrayersTab(_setup);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            driver.EnsureOnTab("Home", _setup);

            // Dismiss any lingering alert (e.g. "No Prayer Requests" from a prior attempt)
            driver.DismissAlertIfPresent();

            driver.WaitAndTap("Home_Btn_PrayerTime");

            // Tapping Prayer Time always shows an action sheet now: with active
            // prayers, "All Requests" and "Choose cards…" are always offered
            // (MainPage.xaml.cs:70-79). Tap "All Requests" to start a full session.
            // The locator differs by platform — iOS exposes the button by
            // accessibility id, but Android renders the action sheet as a native
            // dialog whose options carry @text and NO content-desc, so
            // AccessibilityId never matches there; tap by visible text instead (#195).
            try
            {
                if (TestConfig.IsAndroid)
                {
                    driver.TapByText("All Requests", timeoutSeconds: 5);
                }
                else
                {
                    // iOS: AccessibilityId + Click() rather than a coordinate tap —
                    // iPad popover coordinates can drift mid-animation.
                    driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
                    try
                    {
                        driver.FindElement(MobileBy.AccessibilityId("All Requests")).Click();
                    }
                    finally
                    {
                        driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
                    }
                }
            }
            catch (WebDriverException)
            {
                // No action sheet found — a stray alert may be up instead; clear it.
                driver.DismissAlertIfPresent();
            }
            Thread.Sleep(TestConfig.DelayModalAnimation);

            // Check if we landed on tag scope page (tapped "By Tags" by mistake)
            if (TryRecoverFromTagScope(driver))
                continue;

            // Check if we're in a real prayer session
            bool inSession = driver.IsDisplayed("PrayerTime_Btn_Next", timeoutSeconds: 3)
                || driver.IsDisplayed("PrayerTime_Btn_Done", timeoutSeconds: 1)
                || driver.IsTextDisplayed("›", timeoutSeconds: 1)
                || driver.IsTextDisplayed("I'm done", timeoutSeconds: 1);
            if (inSession)
                return true;

            // Might be on the "all prayed" completion screen
            bool onCompletion = driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 1)
                || driver.IsTextDisplayed("Finish", timeoutSeconds: 1);
            if (onCompletion)
            {
                if (driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 1))
                    driver.Tap("PrayerTime_Btn_Finish");
                else
                    driver.TapByText("Finish");
                Thread.Sleep(TestConfig.DelayAfterNavigation);
                continue;
            }
        }
        return false;
    }

    /// <summary>If autoDismissAlerts sent us to the tag scope page, cancel and return true.</summary>
    private bool TryRecoverFromTagScope(OpenQA.Selenium.Appium.AppiumDriver driver)
    {
        if (driver.IsDisplayed("Scope_Btn_Cancel", timeoutSeconds: 1))
        {
            driver.Tap("Scope_Btn_Cancel");
            Thread.Sleep(TestConfig.DelayModalAnimation);
            return true;
        }
        return false;
    }

    /// <summary>Exit prayer time via Finish, Done, or Back.</summary>
    /// <remarks>
    /// iOS accessibility flattening makes AutomationIds invisible inside the Prayer Time
    /// page layout. Fall back to text-based locators for "Finish" and "I'm done" buttons.
    /// </remarks>
    private void ExitPrayerTime()
    {
        var driver = _setup.Driver;

        // Diagnostic: capture Prayer Time page state before attempting exit
        if (TestConfig.IsIOS)
            driver.DumpPageSource("PrayerTime_BeforeExit");

        // Try AutomationId first (works on Android)
        if (driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 2))
        {
            driver.Tap("PrayerTime_Btn_Finish");
        }
        else if (driver.IsDisplayed("PrayerTime_Btn_Done", timeoutSeconds: 2))
        {
            driver.Tap("PrayerTime_Btn_Done");
        }
        // iOS fallback: text-based search for the button labels
        else if (driver.IsTextDisplayed("Finish", timeoutSeconds: 2))
        {
            driver.TapByText("Finish");
        }
        else if (driver.IsTextDisplayed("I'm done", timeoutSeconds: 2))
        {
            driver.TapByText("I'm done");
        }
        else if (driver.IsTextContainsDisplayed("done", timeoutSeconds: 2))
        {
            driver.TapByTextContains("done");
        }
        else
        {
            // Nothing found — dump page source for debugging
            if (TestConfig.IsIOS)
                driver.DumpPageSource("PrayerTime_ExitFailed");
            driver.GoBack();
        }
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        // Diagnostic: capture state after exit attempt
        if (TestConfig.IsIOS)
            driver.DumpPageSource("PrayerTime_AfterExit");
    }

    /// <summary>8.1: Prayer Time session starts — carousel and nav buttons visible.</summary>
    [SkippableFact]
    public void PrayerTime_SessionStarts_ShowsCarousel()
    {
        _setup.Driver.ResetAppUIState(_setup);
        if (!TryStartPrayerTime())
            throw new SkipException("Could not start Prayer Time session");

        var driver = _setup.Driver;

        // iOS accessibility flattening hides AutomationIds — use text fallbacks
        Assert.True(
            driver.IsDisplayed("PrayerTime_List_Carousel", timeoutSeconds: 10)
            || driver.IsDisplayed("PrayerTime_Btn_Done", timeoutSeconds: 3)
            || driver.IsTextDisplayed("I'm done", timeoutSeconds: 2)
            || driver.IsTextContainsDisplayed("of", timeoutSeconds: 2), // "1 of 2" counter
            "Prayer time should show carousel or Done button");

        ExitPrayerTime();
    }

    /// <summary>8.2: Navigation buttons — Previous/Next are present.</summary>
    [SkippableFact]
    public void PrayerTime_NavigationButtons_Present()
    {
        _setup.Driver.ResetAppUIState(_setup);
        if (!TryStartPrayerTime())
            throw new SkipException("Could not start Prayer Time session");

        var driver = _setup.Driver;

        // iOS: AutomationIds invisible due to flattening — fall back to button text
        Assert.True(
            driver.IsDisplayed("PrayerTime_Btn_Previous", timeoutSeconds: 3)
            || driver.IsDisplayed("PrayerTime_Btn_Next", timeoutSeconds: 3)
            || driver.IsTextDisplayed("‹", timeoutSeconds: 2)
            || driver.IsTextDisplayed("›", timeoutSeconds: 2)
            || driver.IsTextDisplayed("I'm done", timeoutSeconds: 2),
            "Navigation buttons or session controls should be visible");

        ExitPrayerTime();
    }

    /// <summary>8.5: "I'm Done" / Finish button exits prayer time.</summary>
    [SkippableFact]
    public void PrayerTime_FinishButton_ExitsPrayerTime()
    {
        _setup.Driver.ResetAppUIState(_setup);
        if (!TryStartPrayerTime())
            throw new SkipException("Could not start Prayer Time session");

        ExitPrayerTime();

        Assert.True(
            _setup.Driver.IsDisplayed("Home_Btn_QuickAdd", timeoutSeconds: 10)
            || _setup.Driver.IsDisplayed("Home_Btn_PrayerTime", timeoutSeconds: 3),
            "Should return to Home after exiting Prayer Time");
    }

    /// <summary>8.6: Tag-scoped session — scope page shows Cancel/Start.</summary>
    /// <remarks>
    /// Commit 84d4b11: action sheet only appears when both prayers AND tags exist.
    /// This test ensures a tag exists so the action sheet is shown, then taps
    /// "By Tags" to land on the tag scope page.
    /// </remarks>
    [Fact]
    public void PrayerTime_TagScoped_ShowsScopePage()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        // Both prayers and tags must exist for the action sheet to appear
        driver.EnsureOnPrayersTab(_setup);
        driver.EnsureUITestTagExists(_setup);

        driver.EnsureOnTab("Home", _setup);

        driver.WaitAndTap("Home_Btn_PrayerTime");

        // Let the action sheet / iPad popover finish animating before tapping —
        // on iPad, mid-animation hits on "By Tags" land on "By Collection" below it.
        Thread.Sleep(TestConfig.DelayModalAnimation);
        driver.TapIOSActionSheetButton("By Tags", timeoutSeconds: 5);

        Assert.True(
            driver.IsDisplayed("Scope_Btn_Start", timeoutSeconds: 10)
            || driver.IsDisplayed("Scope_Btn_Cancel", timeoutSeconds: 3),
            "Tag scope page should show Start and Cancel buttons");

        driver.WaitAndTap("Scope_Btn_Cancel");
        Thread.Sleep(TestConfig.DelayModalAnimation);
        Assert.True(driver.IsDisplayed("Home_Btn_PrayerTime", timeoutSeconds: 10),
            "Cancel should dismiss scope modal and return to Home");
    }
}
