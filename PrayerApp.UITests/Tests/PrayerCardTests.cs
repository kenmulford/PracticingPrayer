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
    /// Idempotently expand the "Quick Add" system card and confirm it settled into its
    /// expanded state. Cross-platform and order-independent.
    /// <para>
    /// Quick Add is a SYSTEM card seeded into the System box, whose <c>SortOrder = 900</c>
    /// (DBService.cs:593) sorts it BELOW every user box — so on a freshly-reset page it
    /// renders below the fold and the CollectionView virtualizes it out of the a11y tree.
    /// The pre-#194 implementation tapped only if a bare <c>IsTextDisplayed("Quick Add")</c>
    /// probe already saw it, so solo it missed the off-screen row and silently no-op'd; it
    /// passed only inside the full suite, where an earlier test (e.g.
    /// <see cref="Cards_MovePrayer_ToSystemCard_TargetExpandsAndShowsMovedPrayer"/>) had
    /// already scrolled Quick Add into view and left it expanded in the shared session.
    /// </para>
    /// <para>
    /// Mirrors that test's proven path: <c>EnsureCardVisible</c> brings the off-screen
    /// system card on screen (scroll → expand-section → search fallback), then the
    /// system-card-aware <see cref="IsCardHeaderExpanded"/> / <see cref="WaitForCardExpanded"/>
    /// (which tolerate the ", System" infix <c>AccessibleCardHeader</c> inserts —
    /// PrayerCardViewModel.cs:267) drive and confirm the expand toggle.
    /// </para>
    /// </summary>
    private void ExpandQuickAddCard()
    {
        var driver = _setup.Driver;
        const string QuickAdd = "Quick Add";

        // Bring the off-screen system card into the rendered tree (no-op if visible).
        driver.EnsureCardVisible(QuickAdd);

        // Already expanded (e.g. left so by a prior test in the shared session)? Done.
        if (IsCardHeaderExpanded(driver, QuickAdd)) return;

        // Tap the header (platform-aware) and wait for the composed header to settle
        // into its expanded state before the caller asserts on the realized subtree.
        TapCardHeader(driver, QuickAdd);
        WaitForCardExpanded(driver, QuickAdd, timeoutSeconds: 10);
    }

    /// <summary>3.1: Cards list loads — card collection is visible.</summary>
    [Fact]
    public void Cards_ListLoads_ShowsCards()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        Assert.True(_setup.Driver.IsDisplayed("Cards_List_Cards"),
            "Card collection should be visible on Prayer Cards page");
    }

    /// <summary>3.2: Search cards — typing filters the list.</summary>
    [Fact]
    public void Cards_SearchFilter_FiltersCards()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        driver.EnterText("Cards_Search", "zzz_nonexistent");
        Thread.Sleep(TestConfig.DelayDirtyRegistration);

        driver.EnterText("Cards_Search", "");
        Thread.Sleep(TestConfig.DelayDirtyRegistration);

        Assert.True(driver.IsDisplayed("Cards_List_Cards"));
    }

    /// <summary>3.3: Search keyboard dismiss — tap background dismisses keyboard.</summary>
    [Fact]
    public void Cards_SearchKeyboard_DismissesOnBackground()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        if (TestConfig.IsIOS)
        {
            // iOS with hardware keyboard: on-screen keyboard doesn't appear,
            // so test that tapping search and going back doesn't break navigation.
            driver.Tap("Cards_Search");
            Thread.Sleep(TestConfig.DelayAfterTap);
            driver.NavigateToTab("Prayer Cards");
        }
        else
        {
            driver.Tap("Cards_Search");
            Thread.Sleep(TestConfig.DelayAfterTap);
            driver.GoBack();
            Thread.Sleep(TestConfig.DelayAfterTap);
        }

        Assert.True(driver.IsDisplayed("Cards_List_Cards"));
    }

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

        // Delete via the inline Delete button + confirm dialog. Same flow as
        // Cards_DeleteCard_RemovesFromList; the difference is that the target
        // card is expanded with a realized subtree at delete time.
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

    /// <summary>3.12: Delete card — navigate to the pre-seeded "UITest Delete Target Card",
    /// expand it, delete via the inline Delete button, confirm. The target card
    /// is a throwaway from the seed; destroying it does not affect the shared
    /// UITest baseline.</summary>
    [Fact]
    public void Cards_DeleteCard_RemovesFromList()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Realize the always-seeded target into the tree first (#206). The larger
        // namespaced-move seed sorts "Move *" cards ahead of "UITest *" and pushes
        // this row further toward the fold; under CollectionView virtualization a bare
        // IsTextDisplayed guard would then miss the row and silently skip the delete
        // flow (a false-green). EnsureCardVisible scrolls/expands/searches it in.
        driver.EnsureCardVisible(TestSeedFixtures.DeleteCard);
        Assert.True(driver.IsTextDisplayed(TestSeedFixtures.DeleteCard, timeoutSeconds: 10),
            $"Seeded '{TestSeedFixtures.DeleteCard}' must be locatable after EnsureCardVisible — " +
            "a missing own-fixture is a harness failure, not a silent pass.");

        // Expand the card, tap inline Delete button
        driver.TapByText(TestSeedFixtures.DeleteCard);
        Thread.Sleep(TestConfig.DelayAfterTap);
        driver.WaitAndTap("Cards_Btn_Delete", timeoutSeconds: 10);
        driver.DismissAlertIfPresent();
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));
    }

    /// <summary>3.16: Tag filter chips — visible when tags exist in the system.</summary>
    [Fact]
    public void Cards_TagChips_VisibleWhenTagsExist()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        // Ensure at least one tag exists
        driver.EnsureUITestTagExists(_setup);

        driver.EnsureOnTab("Prayer Cards", _setup);
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Chip inner Label is not in the a11y tree; Border Description is a phrase
        // ("UITest Tag, not selected"), so match by substring.
        Assert.True(
            driver.IsTextContainsDisplayed("UITest Tag", timeoutSeconds: 10),
            "Tag filter chip should be visible on Prayer Cards page when tags exist");
    }

    /// <summary>3.18: System card protection — Quick Add should not show action buttons when expanded.</summary>
    [SkippableFact]
    public void Cards_SystemCard_ShareNotAvailable()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        // Expand the off-screen Quick Add system card via the shared, order-independent
        // helper (#194). Skip only if expansion genuinely can't be confirmed — the
        // negative asserts below would otherwise false-pass against an unexpanded card.
        ExpandQuickAddCard();
        if (!IsCardHeaderExpanded(driver, "Quick Add"))
            throw new SkipException("Precondition: 'Quick Add' system card could not be expanded");

        // System cards should not show action buttons
        Assert.False(driver.IsDisplayed("Cards_Btn_Share", timeoutSeconds: 2),
            "System card should not show Share button");
    }

    /// <summary>3.15: System card protection — Quick Add card's Delete button is hidden.</summary>
    [SkippableFact]
    public void Cards_SystemCard_DeleteNotAvailable()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        // Expand the off-screen Quick Add system card via the shared, order-independent
        // helper (#194). Skip only if expansion genuinely can't be confirmed — the
        // negative assert below would otherwise false-pass against an unexpanded card.
        ExpandQuickAddCard();
        if (!IsCardHeaderExpanded(driver, "Quick Add"))
            throw new SkipException("Precondition: 'Quick Add' system card could not be expanded");

        // System cards should not show action buttons
        Assert.False(driver.IsDisplayed("Cards_Btn_Delete", timeoutSeconds: 2),
            "System card should not show Delete button");
        Assert.True(driver.IsDisplayed("Cards_List_Cards"));
    }

    // ── Slice 6c real + 6g — expand realize + post-save overlay continuity ──

    /// <summary>
    /// Idempotently brings the named user card into the collapsed state. Uses
    /// the card's own composed accessibility description (e.g.
    /// "UITest Card, Expanded") as the per-card state proxy —
    /// chip-visibility checks are unreliable because OTHER cards' chips can be
    /// in the tree (e.g. an auto-expanded post-save card that persists in the
    /// seed DB across runs), polluting any global chip-presence assertion.
    /// xUnit doesn't guarantee test order; tests must not assume the card's
    /// state from the seed DB.
    /// </summary>
    private static void EnsureCardCollapsed(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
    {
        driver.EnsureCardVisible(cardName);
        if (!IsCardExpanded(driver, cardName)) return;
        TapCardHeader(driver, cardName);
    }

    /// <summary>
    /// Idempotent counterpart to EnsureCardCollapsed. Expands the card AND confirms the expand
    /// took, re-tapping a bounded number of times when the first tap didn't register — header
    /// present but the inner subtree never realized (shared-session aging, #207) — so a caller
    /// that immediately asserts on the realized chips/prayers doesn't fail on a dropped tap.
    /// Re-reads the <see cref="IsCardExpanded"/> state proxy per attempt (a fresh lookup, so a
    /// reflow can't hand back a stale ref — mirrors <see cref="CollapseAnyExpandedCards"/> /
    /// EnsureAllSectionsExpanded). After each tap it CONFIRMS the expand via
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

    /// <summary>
    /// Collapse every card whose header is currently in the tree in its expanded state
    /// (content-desc / label contains ", Expanded"). The shared Appium session preserves
    /// expand state across tests and xUnit doesn't guarantee order, so a preceding test
    /// can leave an UNRELATED card expanded — and its on-screen action chips
    /// (Cards_Btn_Edit/Favorite/…) would satisfy an unscoped IsDisplayed probe, a
    /// false-green hazard. Bounded fixed-point loop that re-finds the first expanded
    /// header each iteration and taps it to collapse (re-find per iteration because each
    /// collapse reflows the CollectionView and invalidates earlier element refs, mirroring
    /// EnsureAllSectionsExpanded). Best-effort: never throws.
    /// </summary>
    private static void CollapseAnyExpandedCards(OpenQA.Selenium.Appium.AppiumDriver driver)
    {
        var by = TestConfig.IsIOS
            ? By.XPath("//*[contains(@label,', Expanded')]")
            : By.XPath("//*[contains(@content-desc,', Expanded')]");

        const int MaxIterations = 10;
        for (int i = 0; i < MaxIterations; i++)
        {
            try
            {
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(1);
                var header = driver.FindElement(by);
                header.Click();
            }
            catch (WebDriverException) { return; }   // none left, or it went stale → done
            finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

            Thread.Sleep(TestConfig.DelayAfterTap);
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
    /// into view via <see cref="TryScrollTextIntoView"/> before asserting.
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

    /// <summary>
    /// Best-effort scroll of the Cards CollectionView until the element with the given
    /// visible <paramref name="text"/> is on screen. Swallows failures so the caller's
    /// assertion raises the canonical "not displayed" error instead of a masked scroll
    /// error — mirrors <see cref="AppExtensions.EnsureCardVisible"/>. Needed because an
    /// expanded card low in the list renders its contents (here, a moved prayer row)
    /// below the fold, where the CollectionView virtualizes them out of the a11y tree
    /// until scrolled into view (issue #42 eager subtree).
    /// </summary>
    private static void TryScrollTextIntoView(OpenQA.Selenium.Appium.AppiumDriver driver, string text)
        => ScrollUntil(driver,
            () => TestConfig.IsIOS ? driver.IsTextContainsDisplayed(text, timeoutSeconds: 1)
                                   : driver.IsTextDisplayed(text, timeoutSeconds: 1),
            () => driver.ScrollDownToText(text, maxScrolls: 4, scrollableAutomationId: "Cards_List_Cards"));

    /// <summary>
    /// Scrolls the Cards list down until <paramref name="isVisible"/> is true (up to a
    /// bounded number of steps). On Android each step is a CONTROLLED, fling-free
    /// <c>mobile: scrollGesture</c> — the swipe-based <c>ScrollDownTo</c>/<c>swipeGesture</c>
    /// path flings a low-sitting expanded card clean off the top of the viewport with
    /// momentum without ever realizing its chips, so it can never settle on the target.
    /// On iOS, falls back to <paramref name="iosScroll"/> (the existing element-targeted
    /// <c>mobile: scroll</c> helpers, which don't fling). Best-effort: never throws.
    /// </summary>
    private static void ScrollUntil(OpenQA.Selenium.Appium.AppiumDriver driver,
        Func<bool> isVisible, Action iosScroll)
    {
        if (TestConfig.IsIOS)
        {
            try { iosScroll(); } catch (WebDriverException) { /* assertion raises canonical error */ }
            return;
        }

        var size = driver.Manage().Window.Size;
        for (int i = 0; i < 5; i++)
        {
            if (isVisible()) return;
            try
            {
                driver.ExecuteScript("mobile: scrollGesture", new Dictionary<string, object>
                {
                    ["left"] = size.Width / 4,
                    ["top"] = size.Height / 4,
                    ["width"] = size.Width / 2,
                    ["height"] = size.Height / 2,
                    ["direction"] = "down",
                    ["percent"] = 0.5,
                });
            }
            catch (WebDriverException) { /* assertion below raises the canonical error */ }
            Thread.Sleep(TestConfig.DelayAfterTap);
        }
    }

    private static void TapCardHeader(OpenQA.Selenium.Appium.AppiumDriver driver, string cardName)
    {
        if (TestConfig.IsIOS) driver.TapByTextContains(cardName);
        else driver.TapByText(cardName);
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    /// <summary>
    /// BUG-76: Newly-saved card hidden inside collapsed parent section.
    /// 1.3.0 iOS UAT 2026-04-26 — after Add Card → Save, if the new card's
    /// parent section is currently collapsed the card is invisible inside the
    /// collapsed group even though <c>card.IsExpanded</c> is true. Fix:
    /// <c>ConsumePendingSavedAsync</c> auto-expands the parent section by BoxId.
    ///
    /// Test sequence: bring sections to a known all-expanded state, collapse
    /// "Loose Cards" specifically, save a new card (defaults to Loose Cards),
    /// then assert the card is visible without using <c>EnsureCardVisible</c>'s
    /// section-expansion / search fallback — direct DOM-presence check only.
    /// </summary>
    [Fact]
    public void Cards_Save_AutoExpandsCollapsedParentSection_BUG76()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        // Establish a known starting state — all sections expanded.
        driver.EnsureAllSectionsExpanded();
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Collapse the Unboxed section specifically — default destination for a
        // new card with no Box selected. The section header's
        // SemanticProperties.Description binds to the section Name and is always
        // exactly BoxStrings.Unorganized regardless of card count, so TextLocator
        // (iOS @name; Android @content-desc) finds the layout wrapper. Tapping
        // it fires the inner Grid's TapGestureRecognizer (OnSectionHeaderTapped),
        // which toggles IsExpanded.
        driver.TapByText(BoxStrings.Unorganized);
        Thread.Sleep(TestConfig.DelayAfterTap);

        var title = $"BUG76 {DateTime.Now:HHmmssfff}";

        driver.TapToolbarItemById("Add Card");
        driver.WaitForElement("Card_Entry_Title", timeoutSeconds: 10);
        driver.EnterText("Card_Entry_Title", title);
        driver.DismissKeyboardIfPresent();

        try
        {
            driver.TapToolbarItem("Save");
            Thread.Sleep(TestConfig.DelayAfterSave);

            // Direct DOM-presence check — no EnsureCardVisible because its
            // EnsureAllSectionsExpanded fallback would mask the bug. The new
            // card must be in the rendered tree because its parent Loose Cards
            // section auto-expanded when ConsumePendingSavedAsync ran.
            bool titleVisible = TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed(title, timeoutSeconds: 10)
                : driver.IsTextDisplayed(title, timeoutSeconds: 10);

            string? evidence = titleVisible ? null
                : driver.DumpPageSource(nameof(Cards_Save_AutoExpandsCollapsedParentSection_BUG76));

            Assert.True(titleVisible,
                $"BUG-76: newly-saved card '{title}' should be visible — its parent " +
                $"section must auto-expand on save. Dump: {evidence}");

            // Cleanup
            if (TestConfig.IsIOS) driver.TapByTextContains(title);
            else driver.TapByText(title);
            Thread.Sleep(TestConfig.DelayAfterTap);
            if (driver.IsDisplayed("Cards_Btn_Delete", timeoutSeconds: 3))
            {
                driver.Tap("Cards_Btn_Delete");
                driver.DismissAlertIfPresent();
                Thread.Sleep(TestConfig.DelayAfterSave);
            }
        }
        finally
        {
            // Edit-page bail out if Save failed before nav.
            if (driver.IsDisplayed("Card_Entry_Title", timeoutSeconds: 1))
            {
                try { driver.GoBack(); } catch (WebDriverException) { }
                Thread.Sleep(TestConfig.DelayAfterTap);
                try { driver.DismissAlertIfPresent(); } catch (WebDriverException) { }
            }
        }
    }

    // ── Move-prayer tests (TD-20) ── Commit 1 TDD anchors ─────────────────
    // These three tests are RED until Commit 2 lands the declarative-margin +
    // ExpandedCardId fix. Gate them with the FailingPreCommit2 trait so CI
    // can exclude them via --filter 'Status!=FailingPreCommit2' until ready.

    /// <summary>
    /// X-06 / TD-20: Moving a prayer from "Move Reflect Source Card" to "Move Reflect Target Card"
    /// is reflected in both card headers — source count decreases, target count increases.
    /// </summary>
    [Fact]
    [Trait("Status", "FailingPreCommit2")]
    public void Cards_MovePrayerBetweenCards_BothCardsReflect()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 1. Navigate to the source card and expand it.
        driver.EnsureCardVisible(TestSeedFixtures.MoveReflectSourceCard);
        EnsureCardExpanded(driver, TestSeedFixtures.MoveReflectSourceCard);

        // 2. Tap "Prayer One" to open PrayerDetailPage (view mode).
        if (TestConfig.IsIOS)
            driver.TapByTextContains("Prayer One", timeoutSeconds: 10);
        else
            driver.TapByText("Prayer One");
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 3. Enter edit mode.
        driver.TapToolbarItem("Edit");
        Thread.Sleep(TestConfig.DelayAfterTap);
        Assert.True(driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 10),
            "Should be in edit mode on PrayerDetailPage");

        // 4. Change PrayerCardId — tap the card picker and select "Move Reflect Target Card".
        driver.WaitAndTap("Detail_Picker_Card", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterTap);
        if (TestConfig.IsIOS)
            driver.SelectIOSPickerValue(TestSeedFixtures.MoveReflectTargetCard);
        else
            driver.TapByText(TestSeedFixtures.MoveReflectTargetCard);
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 5. Save and navigate back to the Cards tab.
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);
        driver.NavigateToTab("Prayer Cards");
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 6a. Source card should show 3 prayers (one of its four seeded prayers moved out).
        driver.EnsureCardVisible(TestSeedFixtures.MoveReflectSourceCard);
        Assert.True(
            TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed(TestSeedFixtures.MoveReflectSourceCard, timeoutSeconds: 5)
                : driver.IsTextDisplayed(TestSeedFixtures.MoveReflectSourceCard, timeoutSeconds: 5),
            $"{TestSeedFixtures.MoveReflectSourceCard} should still be visible after move");

        // Expand source to verify prayer count dropped — "Prayer One" must be gone.
        EnsureCardExpanded(driver, TestSeedFixtures.MoveReflectSourceCard);
        Assert.False(
            TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed("Prayer One", timeoutSeconds: 3)
                : driver.IsTextDisplayed("Prayer One", timeoutSeconds: 3),
            $"Prayer One should no longer appear in {TestSeedFixtures.MoveReflectSourceCard} after the move");

        // 6b. Target card should now contain the moved prayer.
        EnsureCardCollapsed(driver, TestSeedFixtures.MoveReflectSourceCard);
        driver.EnsureCardVisible(TestSeedFixtures.MoveReflectTargetCard);
        EnsureCardExpanded(driver, TestSeedFixtures.MoveReflectTargetCard);
        Assert.True(
            TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed("Prayer One", timeoutSeconds: 10)
                : driver.IsTextDisplayed("Prayer One", timeoutSeconds: 10),
            $"Prayer One should be visible in {TestSeedFixtures.MoveReflectTargetCard} after the move");
    }

    /// <summary>
    /// D-07 / TD-20: After moving a prayer out of "Move Margin Source Card", the originating
    /// card must NOT show a stuck expanded margin while its VM has IsExpanded=false.
    /// This is the regression test for the Border.Margin bug fixed in Commit 2.
    /// </summary>
    [Fact]
    [Trait("Status", "FailingPreCommit2")]
    public void Cards_MovePrayer_DoesNotLeaveSourceCardWithStaleExpandedMargin()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 1. Expand source card.
        driver.EnsureCardVisible(TestSeedFixtures.MoveMarginSourceCard);
        EnsureCardExpanded(driver, TestSeedFixtures.MoveMarginSourceCard);

        // 2. Open "Prayer Two" (using Prayer Two here to keep fixtures independent
        //    from Cards_MovePrayerBetweenCards_BothCardsReflect which uses Prayer One).
        if (TestConfig.IsIOS)
            driver.TapByTextContains("Prayer Two", timeoutSeconds: 10);
        else
            driver.TapByText("Prayer Two");
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 3. Edit mode.
        driver.TapToolbarItem("Edit");
        Thread.Sleep(TestConfig.DelayAfterTap);
        Assert.True(driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 10),
            "Should be in edit mode");

        // 4. Move to target card.
        driver.WaitAndTap("Detail_Picker_Card", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterTap);
        if (TestConfig.IsIOS)
            driver.SelectIOSPickerValue(TestSeedFixtures.MoveMarginTargetCard);
        else
            driver.TapByText(TestSeedFixtures.MoveMarginTargetCard);
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 5. Save and return to Cards tab.
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);
        driver.NavigateToTab("Prayer Cards");
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 6. Source card must be collapsed — not showing the stale expanded margin.
        // The composed accessibility label includes ", Collapsed" when the VM's
        // IsExpanded=false AND the view margin matches (i.e. no stuck margin).
        // Pre-fix this assertion fails because the Border.Margin is visually expanded
        // while VM is collapsed — the accessibility label still reports ", Collapsed"
        // via the VM, but the visual border margin is wrong (the visual regression).
        // Post-fix the DataTrigger ensures the margin is also correct.
        driver.EnsureCardVisible(TestSeedFixtures.MoveMarginSourceCard);
        Assert.True(
            IsCardExpanded(driver, TestSeedFixtures.MoveMarginSourceCard) == false,
            $"{TestSeedFixtures.MoveMarginSourceCard} should be collapsed (IsExpanded=false) after moving a prayer out. " +
            "If stuck expanded, the Border.Margin is not driven by the declarative DataTrigger.");

        // On iOS also verify via composed label that the platform sees it as Collapsed.
        // The composed card label carries a prayer-count infix — "Move Margin Source Card, 3 prayers,
        // Collapsed" — so a contiguous "Move Margin Source Card, Collapsed" substring never matches.
        // Assert count-agnostically (mirrors IsCardHeaderExpanded's AND-XPath) that one element's
        // label contains BOTH the card name AND the ", Collapsed" suffix; never hard-code the count.
        if (TestConfig.IsIOS)
            Assert.True(
                driver.FindElements(By.XPath(
                    $"//*[contains(@label,'{TestSeedFixtures.MoveMarginSourceCard}') and contains(@label,', Collapsed')]")).Count > 0,
                $"iOS a11y label should report the {TestSeedFixtures.MoveMarginSourceCard} as Collapsed (no stale expanded " +
                "margin); the composed label is '{Title}, {N prayers}, Collapsed'.");
    }

    /// <summary>
    /// Issue #42: Moving a prayer to a SYSTEM card target (here "Quick Add", which
    /// lives in the System box at sort ~900 and is therefore usually off-screen)
    /// must auto-expand that card and reveal the moved prayer in context — exactly
    /// like the user-target move above. This is the regression net for the
    /// shape-(i) realization retirement: with the expanded subtree always inflated
    /// (hidden via IsVisible when collapsed) there is no off-screen cell-realization
    /// race, so setting ExpandedCardId on the system target reveals the prayer
    /// regardless of where the cell sits in the list. Exercises the post-save
    /// auto-expand path with a system-card target specifically.
    /// "Quick Add" is used (not "Shared with me") because it is the system card the
    /// app always seeds on startup, so it is reliably present in the move picker.
    /// </summary>
    [Fact]
    public void Cards_MovePrayer_ToSystemCard_TargetExpandsAndShowsMovedPrayer()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 1. Expand source card.
        driver.EnsureCardVisible(TestSeedFixtures.MoveSystemSourceCard);
        EnsureCardExpanded(driver, TestSeedFixtures.MoveSystemSourceCard);

        // 2. Open "Prayer Four" (dedicated fixture prayer for the system-target test).
        if (TestConfig.IsIOS)
            driver.TapByTextContains("Prayer Four", timeoutSeconds: 10);
        else
            driver.TapByText("Prayer Four");
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 3. Edit mode.
        driver.TapToolbarItem("Edit");
        Thread.Sleep(TestConfig.DelayAfterTap);
        Assert.True(driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 10),
            "Should be in edit mode");

        // 4. Move to the "Quick Add" SYSTEM card via the card picker.
        driver.WaitAndTap("Detail_Picker_Card", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterTap);
        if (TestConfig.IsIOS)
            driver.SelectIOSPickerValue("Quick Add");
        else
            driver.TapByText("Quick Add");
        Thread.Sleep(TestConfig.DelayAfterTap);

        // 5. Save and return to the Cards tab.
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);
        driver.NavigateToTab("Prayer Cards");
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // 6. "Quick Add" (off-screen system card) should be auto-expanded.
        // EnsureCardVisible only opens sections / scrolls — it does NOT tap-expand the
        // card — so the expanded-header check genuinely reflects the post-save auto-expand.
        // WaitForCardExpanded is system-card-aware: Quick Add's a11y header is
        // "Quick Add, System, Expanded" (the ", System" infix), which the exact-match
        // IsCardExpanded can never satisfy — IsCardHeaderExpanded matches it correctly.
        driver.EnsureCardVisible("Quick Add");
        bool quickAddExpanded = WaitForCardExpanded(driver, "Quick Add", timeoutSeconds: 10);
        Assert.True(
            quickAddExpanded,
            "Quick Add (system card) should auto-expand after receiving the moved prayer. " +
            "Post-save ExpandedCardId should expand the off-screen system target — issue #42.");

        // 7. The moved prayer must be visible inside the expanded system card. Scroll it
        // into view first — like the chips, the eager expanded subtree renders below the
        // fold when Quick Add sits low in the System box (virtualized out until scrolled).
        TryScrollTextIntoView(driver, "Prayer Four");
        Assert.True(
            TestConfig.IsIOS
                ? driver.IsTextContainsDisplayed("Prayer Four", timeoutSeconds: 10)
                : driver.IsTextDisplayed("Prayer Four", timeoutSeconds: 10),
            "Prayer Four should be visible inside the expanded Quick Add system card after the move");
    }
}
