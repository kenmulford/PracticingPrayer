using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PrayerApp.Helpers;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 3: Prayer Cards Tab
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "3-PrayerCards")]
public class PrayerCardTests
{
    private readonly AppiumSetup _setup;
    public PrayerCardTests(AppiumSetup setup) => _setup = setup;

    /// <summary>
    /// Bounded number of expand-tap attempts in <see cref="EnsureCardExpanded"/>. Small on
    /// purpose (#207): in the aged shared session an expand tap can fail to register — header
    /// present but the inner subtree never realizes — so up to 3 expand-tap attempts (two
    /// retries) cover the drift while a genuinely stuck card still fails fast rather than spinning.
    /// </summary>
    private const int CardExpandAttempts = 3;

    /// <summary>
    /// Regression: tester reproducibly crashed on Samsung Galaxy Ultra after creating a
    /// new card. Root cause was VM→View C# event firing CollectionView.ScrollTo against
    /// a MauiRecyclerView whose adapter snapshot hadn't committed the BoxSections
    /// rebuild — Java.Lang.IllegalArgumentException "Invalid target position." The fix
    /// replaces the event with a lifecycle-gated PendingSavedIdentifier consumed in
    /// OnAppearing with two dispatcher ticks before ScrollTo. Test asserts (a) the app
    /// survives the post-save lifecycle and (b) the new card row materialized in the
    /// CollectionView. Note: doesn't reproducibly RED on emulator (race window too narrow);
    /// serves as a regression safety net for any future device where the race opens up.
    /// </summary>
    [Fact]
    public void Cards_CreateCard_PostSaveLifecycle_DoesNotCrash_AndCardIsVisible()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        // Timestamped title to dodge UNIQUE-constraint dup alerts (BUG-74).
        var title = $"Race Regression {DateTime.Now:HHmmss}";

        driver.TapToolbarItemById("Add Card");
        driver.WaitForElement("Card_Entry_Title", timeoutSeconds: 10);
        driver.EnterText("Card_Entry_Title", title);
        driver.DismissKeyboardIfPresent();
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // (a) Process survival check — if the post-save scroll-to crashed, this
        //     element wait would fail with a session error, not a NoSuchElement.
        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10),
            "Cards page should still render after save (no crash).");

        // (b) New card row materialized in the virtualized CollectionView.
        driver.EnsureCardVisible(title);
        Assert.True(
            TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed(title, timeoutSeconds: 5)
                : driver.IsTextDisplayed(title, timeoutSeconds: 5),
            $"Newly created card '{title}' should be visible in the list.");

        // Cleanup: delete the disposable card so reruns don't accumulate fixtures.
        if (TestConfig.IsIOS)
            driver.TapByTextContains(title);
        else
            driver.TapByText(title);
        Thread.Sleep(TestConfig.DelayAfterTap);
        if (driver.IsDisplayed("Cards_Btn_Delete", timeoutSeconds: 3))
        {
            driver.Tap("Cards_Btn_Delete");
            driver.DismissAlertIfPresent();
            Thread.Sleep(TestConfig.DelayAfterSave);
        }
    }

    /// <summary>Build-95 fallout (Slice 6c): after deleting an expanded card,
    /// none of its prayer titles should still render anywhere on the page.
    /// Pre-fix the lazy-realized expanded subtree's explicit BindingContext
    /// pinned the inner ContentView.Content to the deleted card's vm, so a
    /// recycled cell rendering a different card still showed the deleted
    /// card's prayer rows under it. The bug class was masked by BUG-79/80's
    /// realize-storm crash; once that crash was closed in build 95 the
    /// staleness became visible.</summary>
    [Fact]
    public void Cards_DeleteExpandedCard_DoesNotLeakPrayersToOtherCards()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Expand Big Card so its inline expanded subtree (shape (i): always
        // inflated, gated by IsVisible) renders with Big Card's BindingContext.
        EnsureCardExpanded(driver, "Recycle Big Card");

        // Sanity: at least one Big Card prayer is rendered before delete.
        Assert.True(
            driver.IsTextDisplayed("Recycle Big Prayer 0", timeoutSeconds: 10),
            "Big Card should expand and show its prayers before delete (sanity).");

        // Delete via the inline Delete button + confirm dialog — the standard
        // card-delete flow; the difference here is that the target card is
        // expanded with a realized subtree at delete time.
        driver.WaitAndTap("Cards_Btn_Delete", timeoutSeconds: 10);
        driver.DismissAlertIfPresent();
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Anchor the viewport on the survivor so any leaked Big Card prayer
        // rows would be near the visible cell, not scrolled off-screen
        // (avoids a false-pass where the assertions miss content that's
        // technically present in the tree but outside the visible region).
        driver.EnsureCardVisible("Recycle Small Card");

        // Post-delete the Loose Cards section's SetCards fires a Reset which
        // re-dequeues cells. Pre-fix the inner ContentView.Content kept its
        // first-realize BindingContext = Big Card vm even after the cell
        // was reassigned — so any of Big Card's prayer titles that remained
        // visible anywhere on the page indicates the bug.
        Assert.False(
            driver.IsTextDisplayed("Recycle Big Prayer 0", timeoutSeconds: 5),
            "After deleting Big Card, none of its prayer titles should still " +
            "render. If this fails, a recycled cell's inner BindingContext is " +
            "still pointing at the deleted card (Slice 6c lazy-realize / " +
            "build-95 fallout).");
        Assert.False(
            driver.IsTextDisplayed("Recycle Big Prayer 2", timeoutSeconds: 3));
        Assert.False(
            driver.IsTextDisplayed("Recycle Big Prayer 4", timeoutSeconds: 3));
    }

    // ── Slice 6c real + 6g — expand realize + post-save overlay continuity ──

    /// <summary>
    /// Idempotently expands the card AND confirms the expand
    /// took, re-tapping a bounded number of times when the first tap didn't register — header
    /// present but the inner subtree never realized (shared-session aging, #207) — so a caller
    /// that immediately asserts on the realized chips/prayers doesn't fail on a dropped tap.
    /// Re-reads the <see cref="IsCardExpanded"/> state proxy per attempt (a fresh lookup, so a
    /// reflow can't hand back a stale ref — mirrors EnsureAllSectionsExpanded). After each tap
    /// it CONFIRMS the expand via
    /// <see cref="WaitForCardExpanded"/>'s bool return and returns immediately on success — so
    /// a tap that DID take is never undone by a premature re-tap: a detection flake on the next
    /// iteration can't fall through and toggle an already-expanded card back to collapsed. It
    /// only re-taps when the wait genuinely reports still-not-expanded (for a user card
    /// IsCardHeaderExpanded and IsCardExpanded agree). No-op cost when already expanded: the
    /// first proxy check returns before any tap.
    /// </summary>
    private static void EnsureCardExpanded(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
    {
        driver.EnsureCardVisible(cardName);
        for (int attempt = 0; attempt < CardExpandAttempts; attempt++)
        {
            if (IsCardExpanded(driver, cardName)) return;
            TapCardHeader(driver, cardName);
            if (WaitForCardExpanded(driver, cardName, timeoutSeconds: 5)) return;
        }
    }

    /// <summary>True if the card is in expanded state, judged by its own ", Expanded" suffix.</summary>
    private static bool IsCardExpanded(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
        => TestConfig.IsIOS
            ? driver.IsTextContainsDisplayed(cardName + ", Expanded", timeoutSeconds: 1)
            : driver.IsTextDisplayed(cardName + ", Expanded", timeoutSeconds: 1);

    /// <summary>
    /// True if the named card's header is currently rendered in its expanded state,
    /// tolerant of the ", System" infix that <c>AccessibleCardHeader</c> inserts for
    /// system cards (PrayerCardViewModel.cs:267) — so "Quick Add, System, Expanded"
    /// counts as expanded just like a user card's "Move Source Card, Expanded". The
    /// shared <see cref="IsCardExpanded"/> uses an EXACT "{name}, Expanded" match, which
    /// can never match a system card; this helper matches a SINGLE header element whose
    /// content-desc/label contains BOTH the card name AND the ", Expanded" suffix, so it
    /// never cross-matches a different card or a collapsed header. Non-throwing.
    /// </summary>
    private static bool IsCardHeaderExpanded(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
    {
        var by = TestConfig.IsIOS
            ? By.XPath($"//*[contains(@label,'{cardName}') and contains(@label,', Expanded')]")
            : By.XPath($"//*[contains(@content-desc,'{cardName}') and contains(@content-desc,', Expanded')]");
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(1);
            return driver.FindElement(by).Displayed;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>
    /// Polls <see cref="IsCardHeaderExpanded"/> until the named card has SETTLED into its
    /// expanded state, up to <paramref name="timeoutSeconds"/>. Returns true once settled,
    /// false on timeout.
    /// <para>
    /// Since issue #42 retired lazy realization the expanded subtree is EAGER, so the
    /// chips/prayers exist the instant the card expands — but they read
    /// <c>Displayed=false</c> while the expand animation is mid-flight. Probing chip
    /// visibility immediately after a fixed 300 ms sleep lands inside that window and
    /// flakes. Waiting for the header to settle first is step one; an expanded card low in
    /// the list ALSO renders its contents below the fold (the CollectionView virtualizes
    /// off-screen rows out of the a11y tree), so the call site then scrolls the target row
    /// into view before asserting.
    /// </para>
    /// </summary>
    private static bool WaitForCardExpanded(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName,
        int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (IsCardHeaderExpanded(driver, cardName)) return true;
            Thread.Sleep(TestConfig.DelayAfterTap);
        }
        return IsCardHeaderExpanded(driver, cardName);
    }

    private static void TapCardHeader(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
    {
        if (TestConfig.IsIOS) driver.TapByTextContains(cardName);
        else driver.TapByText(cardName);
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

}
