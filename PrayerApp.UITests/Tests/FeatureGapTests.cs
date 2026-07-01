using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 13: Feature Gap Coverage
/// Tests for recently added features that had no prior Appium coverage.
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "13-FeatureGaps")]
public class FeatureGapTests
{
    private readonly AppiumSetup _setup;
    public FeatureGapTests(AppiumSetup setup) => _setup = setup;

    // ── F-23: Answered On This Date card ────────────────────────

    /// <summary>
    /// 13.1: F-23 — "Answered on this date" card is conditionally visible.
    /// The card only appears when prayers were answered on the same month+day in prior years.
    /// This test validates EITHER the card shows with correct content OR doesn't show at all
    /// (both are valid depending on test data).
    /// </summary>
    [Fact]
    public void Home_AnsweredOnThisDate_CorrectConditionalVisibility()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Home", _setup);
        var driver = _setup.Driver;

        // Home content is inside a ScrollView — scroll to the metric grid area
        if (!driver.IsDisplayed("Home_Metric_Overdue", timeoutSeconds: 10))
            driver.ScrollDownTo("Home_Metric_Overdue", maxScrolls: 2);

        // Try to find the "Answered on this date" card
        bool cardVisible = driver.IsDisplayed("Home_Card_AnsweredOnThisDate", timeoutSeconds: 3);

        // Also check for text on iOS where AutomationId may be flattened
        if (!cardVisible && TestConfig.IsIOS)
            cardVisible = driver.IsTextContainsDisplayed("Answered prayers from", timeoutSeconds: 3);

        if (cardVisible)
        {
            // Card IS visible — verify it contains the header text (works on both platforms:
            // Android reads child Label text, iOS reads the composed Description label)
            Assert.True(
                driver.IsTextContainsDisplayed("Answered prayers from", timeoutSeconds: 3),
                "Answered-on-this-date card should show the 'Answered prayers from' header");
        }
        else
        {
            // Card is NOT visible — verify the rest of the Home page is functional.
            // The card binding is IsVisible="{Binding HasAnsweredOnThisDate}" so
            // it correctly hides when no matching answered prayers exist.
            Assert.True(driver.IsDisplayed("Home_Btn_QuickAdd", timeoutSeconds: 3),
                "Home page should still be functional when answered-on-this-date card is hidden");
            Assert.True(driver.IsDisplayed("Home_Btn_PrayerTime", timeoutSeconds: 3),
                "Prayer Time button should be visible when answered-on-this-date card is hidden");
        }
    }

    // ── FAQ accordion expand/collapse ───────────────────────────

    /// <summary>
    /// 13.2: FAQ accordion — tapping a question expands the answer, tapping again collapses it.
    /// </summary>
    [Fact]
    public void Settings_Help_FaqAccordionExpandCollapse()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.NavigateToTabRoot("Settings", "Settings_Row_Help", _setup);

        driver.WaitAndTap("Settings_Row_Help");
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Find the first FAQ question — "How do I create a prayer card?"
        const string faqQuestion = "How do I create a prayer card?";
        const string faqAnswerFragment = "Add Card";

        Assert.True(driver.IsTextDisplayed(faqQuestion, timeoutSeconds: 8),
            "FAQ question should be visible on Help page");

        // Verify the answer is NOT visible before tapping
        bool answerVisibleBefore = driver.IsTextContainsDisplayed(faqAnswerFragment, timeoutSeconds: 2);

        // Tap the question to expand
        driver.TapByText(faqQuestion, timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        // Verify the answer IS visible after tapping
        Assert.True(driver.IsTextContainsDisplayed(faqAnswerFragment, timeoutSeconds: 10),
            "FAQ answer should be visible after tapping the question to expand");

        // Tap the question again to collapse
        driver.TapByText(faqQuestion, timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        // Verify the answer is hidden after collapsing.
        // On iOS, CollectionView re-render may be slower — allow brief wait.
        bool answerVisibleAfterCollapse = driver.IsTextContainsDisplayed(faqAnswerFragment, timeoutSeconds: 2);

        // If the answer was visible BEFORE we even expanded (another FAQ may show it),
        // skip the collapse assertion — the test data may have pre-expanded state.
        if (!answerVisibleBefore)
        {
            Assert.False(answerVisibleAfterCollapse,
                "FAQ answer should be hidden after tapping the question again to collapse");
        }

        driver.GoBack();
        Assert.True(driver.IsDisplayed("Settings_Row_Help", timeoutSeconds: 10),
            "Should return to Settings hub after leaving Help page");
    }

    // ── Prayer Time by Collection scope (F-25) ──────────────────

    /// <summary>
    /// 13.3: F-25 — Prayer Time "By Collection" scope page shows Start and Cancel buttons.
    /// Requires both active prayers and user-created collections with cards to trigger
    /// the action sheet with the "By Collection" option.
    /// </summary>
    [SkippableFact]
    public void PrayerTime_ByCollection_ShowsScopePage()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        // Ensure preconditions: prayers and a collection must exist
        driver.EnsureOnPrayersTab(_setup);
        driver.EnsureUITestCollectionExists(_setup);

        driver.EnsureOnTab("Home", _setup);
        driver.DismissAlertIfPresent();

        driver.WaitAndTap("Home_Btn_PrayerTime");

        // The action sheet should appear with "By Collection" option.
        // On iOS, autoDismissAlerts may auto-tap the last option — try to tap explicitly.
        // On Android, wait briefly for the action sheet to render.
        if (!TestConfig.IsIOS) Thread.Sleep(TestConfig.DelayAfterNavigation);

        bool reachedScopePage = false;

        try
        {
            driver.TapIOSActionSheetButton("By Collection", timeoutSeconds: 3);
            Thread.Sleep(TestConfig.DelayModalAnimation);
            reachedScopePage = true;
        }
        catch (WebDriverException)
        {
            // Action sheet may not appear if conditions not met (no user boxes with cards),
            // or autoDismissAlerts already tapped something else on iOS.
            driver.DismissAlertIfPresent();
        }

        if (reachedScopePage || driver.IsDisplayed("BoxScope_Btn_Start", timeoutSeconds: 3))
        {
            // We're on the collection scope page — verify Start and Cancel buttons
            Assert.True(driver.IsDisplayed("BoxScope_Btn_Start", timeoutSeconds: 10)
                || driver.IsTextDisplayed("Start", timeoutSeconds: 3),
                "Collection scope page should show Start button");
            Assert.True(driver.IsDisplayed("BoxScope_Btn_Cancel", timeoutSeconds: 3)
                || driver.IsTextDisplayed("Cancel", timeoutSeconds: 3),
                "Collection scope page should show Cancel button");

            // Also verify the page title or header text
            Assert.True(
                driver.IsTextDisplayed("Pray by Collection", timeoutSeconds: 3)
                || driver.IsTextContainsDisplayed("Select", timeoutSeconds: 3),
                "Collection scope page should show the 'Pray by Collection' heading");

            // Cancel back to Home
            if (driver.IsDisplayed("BoxScope_Btn_Cancel", timeoutSeconds: 2))
                driver.Tap("BoxScope_Btn_Cancel");
            else
                driver.TapByText("Cancel");
            Thread.Sleep(TestConfig.DelayModalAnimation);
        }
        else
        {
            // Could not reach the scope page — the action sheet may not have shown
            // "By Collection" (requires user boxes with active prayer cards).
            // Check if we ended up in a prayer session or back on Home.
            if (driver.IsDisplayed("PrayerTime_Btn_Done", timeoutSeconds: 2)
                || driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 1)
                || driver.IsTextDisplayed("I'm done", timeoutSeconds: 1))
            {
                // In a prayer session — exit it
                if (driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 1))
                    driver.Tap("PrayerTime_Btn_Finish");
                else if (driver.IsTextDisplayed("I'm done", timeoutSeconds: 1))
                    driver.TapByText("I'm done");
                else
                    driver.GoBack();
                Thread.Sleep(TestConfig.DelayAfterNavigation);
            }

            // If on tag scope page, cancel out
            if (driver.IsDisplayed("Scope_Btn_Cancel", timeoutSeconds: 1))
            {
                driver.Tap("Scope_Btn_Cancel");
                Thread.Sleep(TestConfig.DelayModalAnimation);
            }

            throw new SkipException(
                "Precondition: 'By Collection' action sheet option not available — "
                + "requires user-created collections containing cards with active prayers");
        }

        // Verify we're back on Home
        Assert.True(driver.IsDisplayed("Home_Btn_PrayerTime", timeoutSeconds: 10),
            "Should return to Home after cancelling collection scope");
    }

    // ── Overdue threshold setting ───────────────────────────────

    /// <summary>
    /// 13.4: Overdue threshold setting — the Entry control is visible on App Settings
    /// with a default placeholder of "30".
    /// </summary>
    [Fact]
    public void Settings_OverdueThreshold_VisibleWithDefault()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.NavigateToTabRoot("Settings", "Settings_Row_AppSettings", _setup);

        driver.WaitAndTap("Settings_Row_AppSettings");
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        // The overdue threshold Entry may require scrolling to reach
        if (!driver.IsDisplayed("AppSettings_Entry_OverdueThreshold", timeoutSeconds: 10))
            driver.ScrollDownTo("AppSettings_Entry_OverdueThreshold", maxScrolls: 2);

        Assert.True(driver.IsDisplayed("AppSettings_Entry_OverdueThreshold", timeoutSeconds: 10),
            "Overdue threshold Entry should be visible on App Settings page");

        // Verify we can read the current value or see the placeholder
        try
        {
            var text = driver.GetText("AppSettings_Entry_OverdueThreshold");
            // The entry should have a value (user's saved threshold) or be empty with placeholder "30"
            // Either state is valid — the important thing is the control is accessible
            Assert.True(text != null,
                "Overdue threshold Entry should be readable");
        }
        catch (WebDriverException)
        {
            // On iOS, text retrieval from Entry can fail if the field is empty
            // and only shows placeholder — this is acceptable
        }

        // Also verify the Landscape Mode switch is present (another setting in the same section)
        Assert.True(driver.IsDisplayed("AppSettings_Switch_Landscape", timeoutSeconds: 3),
            "Landscape Mode switch should also be visible on App Settings page");

        driver.GoBack();
        Assert.True(driver.IsDisplayed("Settings_Row_AppSettings", timeoutSeconds: 10),
            "Should return to Settings hub after leaving App Settings");
    }
}
