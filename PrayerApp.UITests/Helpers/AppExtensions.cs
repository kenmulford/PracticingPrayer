using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;
using PrayerApp.UITests.Infrastructure;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace PrayerApp.UITests.Helpers;

/// <summary>
/// Convenience extensions for Appium driver interactions.
/// On Android, MAUI AutomationId maps to resource-id (By.Id with package prefix).
/// On iOS, AutomationId maps to accessibilityIdentifier (MobileBy.AccessibilityId).
/// </summary>
public static class AppExtensions
{
    private const string PackagePrefix = "com.multithreadedllc.prayercards:id/";

    /// <summary>iOS swipe velocity in pixels/sec. Moderate speed to reliably trigger SwipeView.</summary>
    private const int IOSSwipeVelocity = 1500;

    /// <summary>Dismiss the software keyboard if showing on iOS. No-op on Android.</summary>
    /// <remarks>
    /// Catches every exception: Appium 2 returns "resource not found" for HideKeyboard
    /// which the .NET client surfaces as <see cref="NotImplementedException"/>, not
    /// <see cref="WebDriverException"/>.
    /// </remarks>
    public static void DismissKeyboardIfPresent(this AppiumDriver driver)
    {
        if (!TestConfig.IsIOS) return;
        try { driver.HideKeyboard(); } catch { /* keyboard not shown, endpoint unavailable, etc. */ }
    }

    /// <summary>Build the correct locator for an AutomationId on the current platform.</summary>
    private static By AutomationIdLocator(string automationId)
    {
        if (TestConfig.IsAndroid)
        {
            // MAUI AutomationId maps to resource-id on interactive elements,
            // but may map to content-desc on layout containers (Grid, etc.).
            // Use XPath to check both.
            return By.XPath(
                $"//*[@resource-id='{PackagePrefix}{automationId}' or @content-desc='{automationId}']");
        }
        return MobileBy.AccessibilityId(automationId);
    }

    /// <summary>Build a platform-correct XPath to find an element by its visible text.</summary>
    private static By TextLocator(string text)
    {
        if (TestConfig.IsIOS)
            return By.XPath($"//*[@name='{text}' or @label='{text}']");
        return By.XPath($"//*[@text='{text}' or @content-desc='{text}']");
    }

    /// <summary>Build a platform-correct XPath to find an element containing text.</summary>
    private static By TextContainsLocator(string text)
    {
        if (TestConfig.IsIOS)
            return By.XPath($"//*[contains(@name,'{text}') or contains(@label,'{text}')]");
        return By.XPath($"//*[contains(@text,'{text}') or contains(@content-desc,'{text}')]");
    }

    /// <summary>iOS XPath to find a button by its exact name or label.</summary>
    private static By IOSButtonByNameOrLabel(string value)
        => By.XPath($"//XCUIElementTypeButton[@name='{value}' or @label='{value}']");

    // ── Element Finders ──────────────────────────────────────────

    /// <summary>Find an element by its AutomationId.</summary>
    public static AppiumElement FindByAutomationId(this AppiumDriver driver, string automationId)
        => (AppiumElement)driver.FindElement(AutomationIdLocator(automationId));

    /// <summary>Wait for an element with the given AutomationId to appear.</summary>
    public static AppiumElement WaitForElement(this AppiumDriver driver, string automationId,
        int timeoutSeconds = 15)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        var locator = AutomationIdLocator(automationId);
        return (AppiumElement)wait.Until(d => d.FindElement(locator));
    }

    /// <summary>Wait for an element to disappear from the screen.</summary>
    public static void WaitForElementGone(this AppiumDriver driver, string automationId,
        int timeoutSeconds = 10)
    {
        var locator = AutomationIdLocator(automationId);
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            wait.Until(d =>
            {
                try { d.FindElement(locator); return false; }
                catch (NoSuchElementException) { return true; }
            });
        }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }
    }

    // ── Actions ──────────────────────────────────────────────────

    /// <summary>Tap an element by AutomationId.</summary>
    public static void Tap(this AppiumDriver driver, string automationId)
        => driver.FindByAutomationId(automationId).Click();

    /// <summary>Tap an element by AutomationId, waiting for it first.</summary>
    public static void WaitAndTap(this AppiumDriver driver, string automationId, int timeoutSeconds = 15)
        => driver.WaitForElement(automationId, timeoutSeconds).Click();

    /// <summary>Clear an input and type new text.</summary>
    public static void EnterText(this AppiumDriver driver, string automationId, string text)
    {
        var element = driver.FindByAutomationId(automationId);

        if (TestConfig.IsIOS)
        {
            // iOS: use "Clear text" button to clear (element.Clear() is broken on
            // search field wrappers), then standard SendKeys to type.
            // autoDismissAlerts handles any dictation prompts from the keyboard.
            try
            {
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(1);
                driver.FindElement(MobileBy.AccessibilityId("Clear text")).Click();
                Thread.Sleep(TestConfig.DelayShortSettle);
            }
            catch (WebDriverException) { /* field empty or unfocused */ }
            finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

            if (!string.IsNullOrEmpty(text))
            {
                element.SendKeys(text);
                driver.DismissKeyboardIfPresent();
            }
        }
        else
        {
            element.Clear();
            if (!string.IsNullOrEmpty(text))
                element.SendKeys(text);
        }
    }

    /// <summary>Get the text content of an element.</summary>
    public static string GetText(this AppiumDriver driver, string automationId)
        => driver.FindByAutomationId(automationId).Text;

    /// <summary>Check if an element is currently displayed (does not throw).</summary>
    public static bool IsDisplayed(this AppiumDriver driver, string automationId,
        int timeoutSeconds = 3)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
            var element = driver.FindElement(AutomationIdLocator(automationId));
            return element.Displayed;
        }
        catch (WebDriverException)
        {
            return false;
        }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }
    }

    // ── Scrolling ────────────────────────────────────────────────

    /// <summary>Scroll down until an element with the given AutomationId is found.</summary>
    /// <param name="scrollableAutomationId">Optional: AutomationId of a scrollable container
    /// (e.g. CollectionView) to target on iOS. When provided, iOS uses <c>mobile: scroll</c>
    /// on that element instead of a full-screen swipe.</param>
    public static AppiumElement ScrollDownTo(this AppiumDriver driver, string automationId,
        int maxScrolls = 5, string? scrollableAutomationId = null)
        => ScrollDownUntil(driver, AutomationIdLocator(automationId),
            $"Element '{automationId}'", maxScrolls, scrollableAutomationId);

    /// <summary>Scroll down until an element with the given visible text is found.</summary>
    public static AppiumElement ScrollDownToText(this AppiumDriver driver, string text,
        int maxScrolls = 5, string? scrollableAutomationId = null)
        => ScrollDownUntil(driver, TextLocator(text),
            $"Text '{text}'", maxScrolls, scrollableAutomationId);

    /// <summary>
    /// Scroll down until an element whose visible text <em>contains</em> the given
    /// substring is found. Use when the target text varies (e.g. section header
    /// "Loose Cards · 11 cards" — anchor on "Loose Cards" instead of the full string).
    /// </summary>
    public static AppiumElement ScrollDownToTextContains(this AppiumDriver driver, string text,
        int maxScrolls = 5, string? scrollableAutomationId = null)
        => ScrollDownUntil(driver, TextContainsLocator(text),
            $"Text containing '{text}'", maxScrolls, scrollableAutomationId);

    /// <summary>
    /// Scrolls the prayer list to <paramref name="text"/> and taps it, failing the test
    /// loudly (with a page-source dump) if the row can't be found after scrolling.
    /// Use instead of an <c>if (IsTextDisplayed(...)) { ... }</c> guard, which silently
    /// skips the enclosed assertions when a row is off-screen or missing (a false green).
    /// </summary>
    public static void ScrollToPrayerAndTap(this AppiumDriver driver, string text,
        string scrollableAutomationId = "List_List_Prayers")
    {
        bool scrolledOnIOS = false;
        if (TestConfig.IsIOS)
        {
            // A freshly-created prayer is card-less (PrayerCardId 0 → CardTitle "") and the
            // list sorts by CardTitle then Title (PrayerListViewModel.cs:425-426), so an empty
            // CardTitle sorts it to the TOP. Every scroll path below moves DOWN only — and the
            // iOS predicate-scroll moves further down — so a top-anchored row can never be
            // realized from a list a prior navigation left mid-scrolled; the down-scroll just
            // moves away from it (#183/#184 fail; the #183 dump caught the list at 67% with the
            // target above the realized window). Reset to the top first so the target is at or
            // below the current position: a top row is then on screen (tapped directly below),
            // and a below-fold seeded row (e.g. "UI Test Prayer" under "UITest Card", #182) is
            // still reachable by the down-scroll that follows.
            ResetIOSListScrollToTop(driver, scrollableAutomationId);

            // Top-anchored target is now on screen — tap it directly, before the down-only
            // predicate scroll can move the list past it. Returns before ScrollDownUntil, so
            // the #196 no-progress guard is never involved for this case.
            if (driver.IsTextContainsDisplayed(text, timeoutSeconds: 2))
            {
                driver.TapByTextContains(text, timeoutSeconds: 10);
                return;
            }

            scrolledOnIOS = driver.IOSScrollToPredicateInContainer(
                scrollableAutomationId, $"label CONTAINS '{text}'");
        }

        try
        {
            if (scrolledOnIOS)
            {
                driver.TapByTextContains(text, timeoutSeconds: 10);
            }
            else
            {
                driver.ScrollDownToText(text, maxScrolls: 3,
                    scrollableAutomationId: scrollableAutomationId).Click();
                // Parity with TapByText: a raw .Click() has no post-tap settle, so the
                // caller's next TapToolbarItem would race the navigation animation.
                Thread.Sleep(TestConfig.DelayAfterTap);
            }
        }
        catch (Exception ex) when (ex is NoSuchElementException or WebDriverException)
        {
            var dumpPath = driver.DumpPageSource($"ScrollToPrayerAndTap_{text.Replace(" ", "")}");
            Assert.Fail($"Expected prayer '{text}' on the list but couldn't find it after " +
                $"scrolling (silently-skipped if-guard before #72a). " +
                $"{ex.GetType().Name}: {ex.Message}. Page source: {dumpPath}");
        }
    }

    /// <summary>
    /// Ensure a card on the Prayer Cards page is scrolled into view. No-op if the card
    /// text is already visible. Safe to call before any <c>TapByText</c> / <c>TapByTextContains</c>
    /// on a card name — protects tests from position-in-list variance as the Loose Cards
    /// section accumulates disposable fixtures. Swallows NotFound so the subsequent tap
    /// raises the canonical "could not locate" error instead of a masked scroll error.
    /// </summary>
    public static void EnsureCardVisible(this AppiumDriver driver, string cardName)
    {
        bool IsVisible() => TestConfig.IsIOS
            ? driver.IsTextContainsDisplayed(cardName, timeoutSeconds: 2)
            : driver.IsTextDisplayed(cardName, timeoutSeconds: 2);

        if (IsVisible()) return;

        bool TryScroll()
        {
            try
            {
                driver.ScrollDownToText(cardName, maxScrolls: 3, scrollableAutomationId: "Cards_List_Cards");
                return true;
            }
            catch (WebDriverException) { return false; }
        }

        if (TryScroll()) return;

        // Row missing from the rendered CollectionView — the parent section may
        // be collapsed. Expand all and retry.
        driver.EnsureAllSectionsExpanded();
        if (TryScroll()) return;

        // Last resort (lesson: uitest-visibility-not-existence-under-virtualization.md):
        // type the card name into the in-page search to force MAUI to materialize the
        // matching row. TD-19 validated this pattern for freshly-created cards on iOS.
        // ResetAppUIState clears Cards_Search at the start of the next test.
        try
        {
            driver.EnterText("Cards_Search", cardName);
            Thread.Sleep(TestConfig.DelayCollectionRender);
        }
        catch (WebDriverException)
        {
            // Search bar unavailable (off-page, multi-select mode, etc.) — caller's
            // tap raises the canonical NotFound error.
        }
    }

    /// <summary>Scroll down until a locator matches a visible element.</summary>
    private static AppiumElement ScrollDownUntil(AppiumDriver driver, By locator,
        string description, int maxScrolls, string? scrollableAutomationId)
    {
        driver.DismissKeyboardIfPresent();

        // Cache container once (iOS element-targeted scroll) and window size (Android swipe area)
        string? containerId = null;
        if (TestConfig.IsIOS && scrollableAutomationId != null)
        {
            try { containerId = driver.FindElement(MobileBy.AccessibilityId(scrollableAutomationId)).Id; }
            catch (NoSuchElementException) { /* fall through to generic swipe */ }
        }
        var size = TestConfig.IsAndroid ? driver.Manage().Window.Size : default;

        // No-progress guard (#196): mobile: scroll / swipe / swipeGesture expose no scroll
        // offset, so the cheapest cross-platform "did the view actually move" signal is the
        // page source. When a scroll leaves it byte-for-byte unchanged the list is pinned at
        // an edge — already at the bottom, or too short to scroll at all — and every remaining
        // iteration is a wasted no-op sitting behind a ShortTimeout find-miss (a scan that
        // begins at the top of a short list otherwise burns the whole maxScrolls budget). Reuse
        // the post-scroll source as the next pre-scroll baseline so each iteration costs at most
        // one extra PageSource read, and only on iterations that miss (the already-visible case
        // returns before any read). Bailing is safe: the per-iteration find runs BEFORE the
        // scroll, so a genuinely-present element is still realized; we only stop scrolling once
        // the view can no longer change.
        string? sourceBeforeScroll = null;
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            for (int i = 0; i < maxScrolls; i++)
            {
                try
                {
                    var element = driver.FindElement(locator);
                    if (element.Displayed)
                        return (AppiumElement)element;
                }
                catch (NoSuchElementException) { }

                sourceBeforeScroll ??= driver.PageSource;
                ScrollDown(driver, size, containerId);
                var sourceAfterScroll = driver.PageSource;
                if (sourceAfterScroll == sourceBeforeScroll)
                    break; // scroll moved nothing — list is at an edge, stop burning the budget
                sourceBeforeScroll = sourceAfterScroll;
            }
        }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }

        throw new NoSuchElementException($"{description} not found after {maxScrolls} scrolls.");
    }

    /// <summary>
    /// Reset the Cards_List_Cards CollectionView to its top position. Best-
    /// effort: silently no-ops if the list isn't on screen (e.g. test isn't
    /// on the Cards tab). Called from ResetAppUIState so the next test gets
    /// a known scroll position. Uses the inverse of <see cref="ScrollDown"/>
    /// (Android: swipe down; iOS: <c>mobile: scroll direction=up</c>) and
    /// runs a generous-but-bounded number of iterations to reach the top.
    /// </summary>
    private static void ResetCardsListScroll(AppiumDriver driver)
    {
        const int MaxIterations = 8;

        // Cheap presence gate — bypass the loop on tabs that don't host the
        // cards list (Home / Prayers / Tags / Settings).
        if (!driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 0)) return;

        if (TestConfig.IsIOS)
        {
            string listId;
            try { listId = driver.FindByAutomationId("Cards_List_Cards").Id; }
            catch (WebDriverException) { return; }

            // Save/restore the implicit wait so this universal teardown doesn't
            // clobber a future caller that customised the wait window.
            var priorWait = driver.Manage().Timeouts().ImplicitWait;
            try
            {
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(0);
                for (int i = 0; i < MaxIterations; i++)
                {
                    driver.ExecuteScript("mobile: scroll", new Dictionary<string, object>
                    {
                        { "elementId", listId },
                        { "direction", "up" }
                    });
                }
            }
            catch (WebDriverException) { /* container went off-page mid-loop */ }
            finally { driver.Manage().Timeouts().ImplicitWait = priorWait; }
            return;
        }

        var size = driver.Manage().Window.Size;
        for (int i = 0; i < MaxIterations; i++)
        {
            try
            {
                driver.ExecuteScript("mobile: swipeGesture", new Dictionary<string, object>
                {
                    { "left", size.Width / 4 },
                    { "top", size.Height / 4 },
                    { "width", size.Width / 2 },
                    { "height", size.Height / 2 },
                    { "direction", "down" },
                    { "percent", 0.7 }
                });
            }
            catch (WebDriverException) { return; }
        }
    }

    /// <summary>Perform a single scroll-down gesture.</summary>
    /// <param name="containerId">Pre-resolved element ID for iOS container-targeted scroll, or null for full-screen swipe.</param>
    private static void ScrollDown(AppiumDriver driver, System.Drawing.Size size, string? containerId)
    {
        if (TestConfig.IsIOS)
        {
            if (containerId != null)
            {
                driver.ExecuteScript("mobile: scroll", new Dictionary<string, object>
                {
                    { "elementId", containerId },
                    { "direction", "down" }
                });
                return;
            }

            driver.ExecuteScript("mobile: swipe", new Dictionary<string, object>
            {
                { "direction", "up" }
            });
        }
        else
        {
            driver.ExecuteScript("mobile: swipeGesture", new Dictionary<string, object>
            {
                { "left", size.Width / 4 },
                { "top", size.Height / 4 },
                { "width", size.Width / 2 },
                { "height", size.Height / 2 },
                { "direction", "up" },
                { "percent", 0.5 }
            });
        }
    }

    /// <summary>
    /// iOS-only: scroll a CollectionView container back to its top via repeated
    /// <c>mobile: scroll direction=up</c> (the inverse of <see cref="ScrollDown"/>).
    /// Mirrors the iOS branch of <see cref="ResetCardsListScroll"/>, parameterised for any
    /// container so <see cref="ScrollToPrayerAndTap"/> can land on a known top position before
    /// its down-only search. Best-effort: silently no-ops if the container isn't on screen.
    /// Iterating from an already-top list is a harmless no-op (a list can't scroll past its top).
    /// </summary>
    private static void ResetIOSListScrollToTop(AppiumDriver driver, string containerAutomationId)
    {
        const int MaxIterations = 8;

        string listId;
        try { listId = driver.FindElement(MobileBy.AccessibilityId(containerAutomationId)).Id; }
        catch (WebDriverException) { return; }

        // Save/restore the implicit wait so this pre-scroll reset doesn't clobber the
        // caller's wait window; 0s keeps each no-op scroll cheap.
        var priorWait = driver.Manage().Timeouts().ImplicitWait;
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(0);
            for (int i = 0; i < MaxIterations; i++)
            {
                driver.ExecuteScript("mobile: scroll", new Dictionary<string, object>
                {
                    { "elementId", listId },
                    { "direction", "up" }
                });
            }
        }
        catch (WebDriverException) { /* container went off-page mid-loop */ }
        finally { driver.Manage().Timeouts().ImplicitWait = priorWait; }
    }

    // ── Navigation ───────────────────────────────────────────────

    /// <summary>
    /// Navigate to a Shell tab by tapping its tab bar item.
    /// Escalation: (1) back to clear nav stack, (2) dismiss known modals,
    /// (3) re-activate app, (4) XPath text fallback.
    /// </summary>
    public static void NavigateToTab(this AppiumDriver driver, string tabTitle)
    {
        driver.DismissKeyboardIfPresent();

        // Stage 1: Try up to 3 times to find the tab bar, going back each time
        // to clear modals, detail pages, and deep navigation stacks.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            driver.DismissAlertIfPresent();

            if (TryTapTab(driver, tabTitle))
                return;

            // Tab not found — go back to clear the current page/modal
            try { driver.Navigate().Back(); Thread.Sleep(TestConfig.DelayAfterTap); } catch (WebDriverException) { }
        }

        // Stage 2: Try dismissing a known modal (Cancel/Skip buttons that block tab access)
        foreach (var modalButton in new[] { "Welcome_Btn_Skip", "Banner_Btn_Skip", "Banner_Btn_GotIt", "Scope_Btn_Cancel", "QuickAdd_Btn_Cancel" })
        {
            try
            {
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(1);
                var btn = driver.FindElement(MobileBy.AccessibilityId(modalButton));
                btn.Click();
                Thread.Sleep(TestConfig.DelayModalAnimation);
            }
            catch (WebDriverException) { }
            finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

            if (TryTapTab(driver, tabTitle))
                return;
        }

        // Stage 3: Escape Prayer Time page (tab bar is hidden there).
        // Try tapping "I'm done" or "Finish" to exit back to Home.
        if (TryEscapePrayerTime(driver))
        {
            Thread.Sleep(TestConfig.DelayAfterNavigation);
            if (TryTapTab(driver, tabTitle))
                return;
        }

        // Stage 4: re-activate the app (handles case where GoBack closed it)
        try
        {
            var appId = TestConfig.IsIOS ? TestConfig.IOSBundleId : TestConfig.AndroidPackage;
            driver.ActivateApp(appId);
            Thread.Sleep(TestConfig.DelayModalAnimation);
        }
        catch (WebDriverException) { }

        if (TryTapTab(driver, tabTitle))
            return;

        // Stage 5: final XPath text fallback
        var tab = driver.FindElement(TextContainsLocator(tabTitle));
        tab.Click();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
    }

    /// <summary>Try to find and tap a tab by AccessibilityId. Returns true on success.</summary>
    /// <remarks>
    /// iOS scopes the query to <c>XCUIElementTypeTabBar</c>: a bare name lookup matches
    /// the NavBar title and any StaticText on the current page too, and WebDriver picks
    /// the first (non-interactive) match.
    /// </remarks>
    private static bool TryTapTab(AppiumDriver driver, string tabTitle)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            By locator = TestConfig.IsIOS
                ? By.XPath($"//XCUIElementTypeTabBar//XCUIElementTypeButton[@name='{tabTitle}']")
                : MobileBy.AccessibilityId(tabTitle);
            var tab = driver.FindElement(locator);
            tab.Click();
            Thread.Sleep(TestConfig.DelayAfterNavigation);
            return true;
        }
        catch (WebDriverException) { return false; }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }
    }

    /// <summary>
    /// Try to escape the Prayer Time page by tapping "I'm done" or "Finish".
    /// Prayer Time hides the tab bar, so NavigateToTab can't find tabs.
    /// Returns true if an exit button was found and tapped.
    /// </summary>
    private static bool TryEscapePrayerTime(AppiumDriver driver)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(1);

            // Try AutomationId first (works on Android)
            foreach (var id in new[] { "PrayerTime_Btn_Finish", "PrayerTime_Btn_Done" })
            {
                try
                {
                    var btn = driver.FindElement(MobileBy.AccessibilityId(id));
                    btn.Click();
                    return true;
                }
                catch (WebDriverException) { }
            }

            // iOS fallback: text-based search (accessibility flattening hides AutomationIds)
            foreach (var text in new[] { "Finish", "I'm done" })
            {
                try
                {
                    var btn = driver.FindElement(TextLocator(text));
                    btn.Click();
                    return true;
                }
                catch (WebDriverException) { }
            }

            // Last resort: CONTAINS search for "done"
            try
            {
                var btn = driver.FindElement(TextContainsLocator("done"));
                btn.Click();
                return true;
            }
            catch (WebDriverException) { }

            return false;
        }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }
    }

    /// <summary>Dismiss onboarding if needed and navigate to a tab. Replaces per-class EnsureOnX() methods.</summary>
    public static void EnsureOnTab(this AppiumDriver driver, string tabTitle, AppiumSetup setup)
    {
        setup.EnsureSessionAlive();
        // EnsureSessionAlive recreates the session (new setup.Driver) when it finds a dead
        // one, so re-fetch before using `driver` — same recreate-then-stale-param hazard as
        // ResetAppUIState/RecycleSessionIfDue (#164).
        driver = setup.Driver;
        // iOS: the software keyboard persists across navigations and can cover the
        // tab bar / toolbar items. Dismiss before attempting tab navigation.
        // See Lessons/maui-ios-appium-locators.md. No-op on Android.
        driver.DismissKeyboardIfPresent();
        driver.DismissOnboardingIfPresent(setup);
        driver.NavigateToTab(tabTitle);
    }

    /// <summary>
    /// Expand every collapsed collection section on the Cards page. The section's
    /// triangle + header text are hidden from the a11y tree, so collapsed state is
    /// inferred from the vertical gap to the next header (collapsed footer collapses
    /// to HeightRequest=0; expanded footer has at least one card row, ~60+ px).
    /// </summary>
    /// <remarks>
    /// Fixed-point loop: each iteration re-finds all headers and acts on the first
    /// collapsed one, then re-evaluates. Caching the header list upfront is unsafe —
    /// clicking to expand reflows the CollectionView, which invalidates later
    /// references with <c>StaleElementReferenceException</c> when reading Location/Size.
    /// Follows the re-find-per-iteration pattern used by <see cref="ScrollDownUntil"/>.
    /// </remarks>
    public static void EnsureAllSectionsExpanded(this AppiumDriver driver)
    {
        // Gap threshold: a collapsed section's footer collapses to HeightRequest=0,
        // so consecutive header tops differ by just the header's own height
        // (~60-120 px depending on platform). An expanded section adds at least one
        // ~60+ px card row, pushing the gap above 120 px. 100 px is the safe floor.
        const int CollapsedGapThreshold = 100;
        const int MaxIterations = 20;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            var headers = driver.FindElements(AutomationIdLocator("Cards_Section_Header"))
                .OfType<AppiumElement>()
                .Select(h =>
                {
                    try { return (elem: h, top: h.Location.Y, bottom: h.Location.Y + h.Size.Height); }
                    catch (WebDriverException) { return (elem: h, top: -1, bottom: -1); }
                })
                .Where(t => t.top >= 0)
                .OrderBy(t => t.top)
                .ToList();

            if (headers.Count == 0) return;

            int? firstCollapsed = null;
            for (int i = 0; i < headers.Count; i++)
            {
                int nextHeaderTop = (i + 1 < headers.Count) ? headers[i + 1].top : int.MaxValue;
                if (nextHeaderTop - headers[i].bottom < CollapsedGapThreshold)
                {
                    firstCollapsed = i;
                    break;
                }
            }

            if (firstCollapsed == null) return;

            try
            {
                headers[firstCollapsed.Value].elem.Click();
                Thread.Sleep(TestConfig.DelayAfterTap);
            }
            catch (WebDriverException)
            {
                // Element went stale between re-find and click — next iteration will
                // re-find and retry. Bail only if we hit MaxIterations.
            }
        }
    }

    // ── Diagnostics ──────────────────────────────────────────────

    /// <summary>
    /// Dump a screenshot + page source to a temp directory so that a failing test
    /// leaves behind evidence for post-mortem triage. Returns a human-readable
    /// suffix listing the file paths, suitable for appending to an exception message.
    /// Never throws — if the capture fails, it returns an annotated message instead.
    /// </summary>
    public static string CaptureDiagnostics(this AppiumDriver driver, string reason)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var dir = Path.Combine(Path.GetTempPath(), "prayerapp-uitest-diag");
            Directory.CreateDirectory(dir);

            var safeReason = string.Join("_", reason.Split(Path.GetInvalidFileNameChars()));
            var screenshotPath = Path.Combine(dir, $"{timestamp}-{safeReason}.png");
            var sourcePath = Path.Combine(dir, $"{timestamp}-{safeReason}.xml");

            driver.GetScreenshot().SaveAsFile(screenshotPath);
            File.WriteAllText(sourcePath, driver.PageSource);

            return $" (diagnostics — screenshot: {screenshotPath}, page source: {sourcePath})";
        }
        catch (Exception ex)
        {
            return $" (diagnostic capture failed: {ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// Clear transient UI state left by a prior test (open alerts, multi-select mode,
    /// leaked search term, stale deep-nav) so failures don't cascade. Tests that
    /// verify a transient state (e.g. multi-select toolbar) should NOT call this.
    /// See Lessons/uitest-per-test-ui-state-reset.md for rationale.
    /// </summary>
    public static void ResetAppUIState(this AppiumDriver driver, AppiumSetup setup)
    {
        // Per-test session isolation (#164). This runs at the very TOP of the per-test
        // reset — before any navigation or UI inspection — so when the cadence fires and
        // the driver is recreated, no in-progress test state is lost. On a recreate the
        // app relaunches to its landing page (Home); the reset steps below are then no-ops
        // (ResetCardsListScroll gates off the Cards page, the Home fast-path returns), and
        // the caller's following EnsureOnTab navigates to the wanted tab. noReset=true
        // preserves the once-seeded DB, so NO re-seed happens. See AppiumSetup #164 block.
        setup.RecycleSessionIfDue();
        // RecycleSessionIfDue may have torn down the session and assigned a NEW driver to
        // setup.Driver (Quit + new AndroidDriver/IOSDriver). Re-fetch so the reset steps
        // below run against the live session, not the just-quit `driver` param (#164).
        driver = setup.Driver;

        // Cards list scroll position is preserved across tab navigation. A
        // prior test (e.g. Slice 6g auto-reveal-after-save in
        // EmptyCardExpand) can leave the list mid-scrolled, putting the next
        // test's target card above the current viewport. EnsureCardVisible
        // only scrolls down, so without a top-reset its search-bar fallback
        // fires and the page lands in a filtered-list state where chips and
        // composed accessibility descriptions on cards aren't reachable.
        // Run BEFORE the fast-path so it applies even when we're already at
        // a tab root.
        ResetCardsListScroll(driver);

        // In-place recoveries that MUST run BEFORE the fast-path early-return below: clear a
        // leaked multi-select AND a leaked search term. BOTH must run before the fast-path for
        // the same reason — that check gates on "Home", which resolves to the always-visible
        // Home tab-BAR button (AppShell.xaml Title="Home"), present even on the Prayer Cards
        // page while in multi-select or with an active search — so either clear placed AFTER
        // the fast-path never runs when a prior test leaked that state (the fast-path
        // short-circuits and returns first, which is exactly the #205 defect this corrects).
        // Keeping both here leaves the fast-path purely gating the back-out navigation below.
        //
        // Multi-select clear: strictly gated on Cards_Bar_MultiSelect presence so "More" is
        // tapped only in multi-select; outside it "More" opens the overflow popup, which must
        // not happen from this reset. timeoutSeconds:0 — a leaked bar is fully realized, so
        // the instant check detects it at negligible per-reset cost and no-ops otherwise.
        try
        {
            if (driver.IsDisplayed("Cards_Bar_MultiSelect", timeoutSeconds: 0))
            {
                // Overflow button mutates visually to X/Cancel in multi-select mode
                // but keeps AutomationId="More" (MAUI AutomationId is set-once).
                driver.TapToolbarItemById("More");
                Thread.Sleep(TestConfig.DelayAfterTap);
            }
        }
        catch { /* not on Prayer Cards or not in multi-select */ }

        // Search-term clear: same before-the-fast-path reason as the multi-select clear above.
        try
        {
            if (driver.IsDisplayed("Cards_Search", timeoutSeconds: 0) &&
                !string.IsNullOrEmpty(driver.GetText("Cards_Search")))
            {
                driver.EnterText("Cards_Search", "");
                Thread.Sleep(TestConfig.DelayAfterTap);
            }
        }
        catch { /* not on Prayer Cards or search bar not rendered */ }

        // Fast path: already at a tab root with no pending alert.
        if (!driver.IsAlertPresent() && driver.IsDisplayed("Home", timeoutSeconds: 0))
            return;

        try { driver.DismissAlertIfPresent(); } catch { /* best effort */ }

        // Back out to a tab root, dismissing any alert each Back may raise (e.g.
        // "Discard changes?"). Bounded so a stuck page can't loop forever.
        void BackOutToHome()
        {
            for (int i = 0; i < 5; i++)
            {
                try { driver.DismissAlertIfPresent(); } catch { /* best effort */ }
                if (driver.IsDisplayed("Home", timeoutSeconds: 0)) break;
                try { driver.Navigate().Back(); Thread.Sleep(TestConfig.DelayShortSettle); } catch (WebDriverException) { break; }
            }
        }

        BackOutToHome();

        // The Prayer Time session page hides the tab bar and swallows Back, so the
        // back-out above can't reach Home from there. A prior test left on Prayer
        // Time would otherwise break the next test's tab/toolbar lookups (#180, #181;
        // isolation Principle 2 — every test starts on Home). Escape it with the same
        // exit-button helper NavigateToTab Stage 3 uses — but ONLY when actually on
        // Prayer Time, confirmed by the PrayerTime_Btn_Done/_Finish AutomationId other
        // Prayer Time tests already probe (reliable on both platforms). TryEscapePrayerTime
        // has a broad last-resort "done" substring tap that would mis-fire on any
        // unrelated stuck page whose text/content-desc merely contains "done", so it
        // must never run unguarded from this every-test reset path (#205, finding #1).
        // "Done"/"Finish" pops only one level via GoToAsync(".."), which may land on a
        // nested Prayer Time launched from a scope/card page rather than Home; loop
        // (bounded, so a persistent stuck state can't spin forever) — re-confirm Prayer
        // Time, escape, back out — until Home is reached or no Prayer Time level remains
        // (#180, #181, finding #2).
        for (int i = 0; i < 3; i++)
        {
            if (driver.IsDisplayed("Home", timeoutSeconds: 0)) break;
            if (!driver.IsDisplayed("PrayerTime_Btn_Done", timeoutSeconds: 0) &&
                !driver.IsDisplayed("PrayerTime_Btn_Finish", timeoutSeconds: 0)) break;
            if (!TryEscapePrayerTime(driver)) break;
            Thread.Sleep(TestConfig.DelayAfterNavigation);
            BackOutToHome();
        }
    }

    /// <summary>Go back (Android back button or iOS back nav).</summary>
    public static void GoBack(this AppiumDriver driver)
        => driver.Navigate().Back();

    // ── Onboarding ───────────────────────────────────────────────

    /// <summary>
    /// iOS: no-op assertion — onboarding is suppressed at the harness level by
    /// <see cref="TestDataSeed.PreSeedOnboardingCompleteAsync"/>, which writes
    /// <c>OnboardingComplete=YES</c> to NSUserDefaults before Appium launches the
    /// app. Reads the same key back via <c>simctl spawn defaults read</c> to catch
    /// harness-config regressions early (throws if the pre-seed didn't run).
    /// Android: keeps the legacy in-suite dismissal flow — taps "Skip tour" /
    /// "Skip" / "Got it!" then "Done" on the completion popup, with retries to
    /// handle slow popup rendering. Idempotent.
    /// </summary>
    public static void DismissOnboardingIfPresent(this AppiumDriver driver, AppiumSetup setup)
    {
        if (setup.OnboardingHandled) return;

        if (TestConfig.IsIOS)
        {
            AssertOnboardingPreSeeded();
            setup.OnboardingHandled = true;
            return;
        }

        // Android — legacy in-suite dismissal until the Android toolchain returns.
        // Fast path: if the tab bar is already rendered, no onboarding popup is
        // blocking. Saves up to 3s on test #1 vs probing Welcome_Btn_Skip first.
        if (driver.IsDisplayed("Home", timeoutSeconds: 1))
        {
            setup.OnboardingHandled = true;
            return;
        }

        // Retry loop — the welcome popup renders asynchronously from OnAppearing
        // and may not be visible immediately, especially on slow emulators.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Check for dismissal buttons — welcome popup, mid-onboarding banner, or final "Got it!"
            string? dismissButton = driver.IsDisplayed("Welcome_Btn_Skip", timeoutSeconds: 3) ? "Welcome_Btn_Skip"
                : driver.IsDisplayed("Banner_Btn_Skip", timeoutSeconds: 2) ? "Banner_Btn_Skip"
                : driver.IsDisplayed("Banner_Btn_GotIt", timeoutSeconds: 2) ? "Banner_Btn_GotIt"
                : null;

            if (dismissButton != null)
            {
                driver.Tap(dismissButton);
                Thread.Sleep(1000);

                if (driver.IsDisplayed("Complete_Btn_Done", timeoutSeconds: 10))
                {
                    driver.Tap("Complete_Btn_Done");
                    Thread.Sleep(500);
                }

                setup.OnboardingHandled = true;
                return;
            }

            // If tab bar is already accessible, no popup is blocking — we're good
            if (driver.IsDisplayed("Home", timeoutSeconds: 1))
            {
                setup.OnboardingHandled = true;
                return;
            }

            // Wait before retry to give the popup time to render
            Thread.Sleep(1000);
        }

        // After retries, mark handled to avoid infinite loops in future calls
        setup.OnboardingHandled = true;
    }

    /// <summary>
    /// Reads OnboardingComplete from NSUserDefaults on the booted simulator.
    /// Throws if the value isn't truthy, catching the failure mode where
    /// <see cref="TestDataSeed.PreSeedOnboardingCompleteAsync"/> didn't run.
    /// </summary>
    private static void AssertOnboardingPreSeeded()
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            "xcrun",
            $"simctl spawn booted defaults read {TestConfig.IOSBundleId} OnboardingComplete")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start xcrun simctl. Are Xcode CLI tools installed?");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        // `defaults read ... OnboardingComplete` prints `1` for true, `0` for false,
        // or exits non-zero with "does not exist" on the stderr when unset.
        if (proc.ExitCode != 0 || !stdout.Trim().StartsWith("1"))
        {
            throw new InvalidOperationException(
                "OnboardingComplete is not pre-seeded; AppiumSetup.InitializeAsync should call " +
                "TestDataSeed.PreSeedOnboardingCompleteAsync(). " +
                $"defaults read exit={proc.ExitCode}, stdout='{stdout.Trim()}', stderr='{stderr.Trim()}'.");
        }
    }

    // ── Alerts/Dialogs ───────────────────────────────────────────

    /// <summary>Check if an alert dialog is displayed and get its text.</summary>
    public static bool TryGetAlertText(this AppiumDriver driver, out string text)
    {
        if (TestConfig.IsIOS)
        {
            try { text = driver.SwitchTo().Alert().Text ?? ""; return true; }
            catch (WebDriverException) { text = ""; return false; }
        }

        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            text = driver.FindElement(By.Id("android:id/message")).Text;
            return true;
        }
        catch (WebDriverException) { text = ""; return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>Tap a button in an alert dialog by its text.</summary>
    public static void TapAlertButton(this AppiumDriver driver, string buttonText)
    {
        if (TestConfig.IsIOS)
        {
            // iOS alerts expose buttons as XCUIElementTypeButton with @name matching the button label
            var button = driver.FindElement(
                By.XPath($"//XCUIElementTypeButton[@name='{buttonText}']"));
            button.Click();
        }
        else
        {
            var button = driver.FindElement(By.XPath(
                $"//*[@resource-id='android:id/button1' or @resource-id='android:id/button2' or @resource-id='android:id/button3'][contains(@text,'{buttonText}')]"));
            button.Click();
        }
        Thread.Sleep(TestConfig.DelayAfterDismiss);
    }

    // ── Toolbar / Text Finders ────────────────────────────────────

    /// <summary>
    /// Tap a Shell ToolbarItem by its <c>AutomationId</c>. Fallback order: direct
    /// AutomationId → PrayerCardsPage overflow popup (cross-platform, common case
    /// for Add Card / Collections / Select) → iOS Secondary <c>UIMenu</c> (legacy
    /// path kept for PrayerDetailPage's Save+New). The last two fallbacks are tried
    /// only if the direct lookup misses.
    /// </summary>
    public static void TapToolbarItemById(this AppiumDriver driver, string automationId,
        int timeoutSeconds = 10)
    {
        driver.DismissKeyboardIfPresent();
        if (TestConfig.IsIOS) Thread.Sleep(TestConfig.DelayAfterTap);

        var locator = AutomationIdLocator(automationId);

        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(locator).Click();
            Thread.Sleep(TestConfig.DelayAfterTap);
            return;
        }
        catch (WebDriverException) { }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

        if (TryTapOverflowPopupItem(driver, automationId))
            return;

        if (TestConfig.IsIOS && TryTapIOSSecondaryMenuItem(driver, automationId))
            return;

        throw new NoSuchElementException(
            $"Toolbar item '{automationId}' not found on toolbar, in the overflow popup, or in the Secondary UIMenu.");
    }

    /// <summary>
    /// Whether a ToolbarItem is reachable directly, inside the PrayerCardsPage
    /// overflow popup, or behind MAUI's iOS <c>SecondaryToolbarMenuButton</c>. Use
    /// in "does this toolbar item exist" assertions where <see cref="IsDisplayed"/>
    /// alone would miss an item tucked behind an overflow.
    /// </summary>
    public static bool IsToolbarItemAvailable(this AppiumDriver driver, string automationId,
        int timeoutSeconds = 3)
    {
        if (driver.IsDisplayed(automationId, timeoutSeconds)) return true;

        if (IsInOverflowPopup(driver, automationId)) return true;

        if (!TestConfig.IsIOS) return false;

        if (!OpenIOSSecondaryMenu(driver)) return false;
        try
        {
            return driver.FindElements(IOSButtonByNameOrLabel(automationId)).Count > 0;
        }
        finally { CloseIOSPopover(driver); }
    }

    /// <summary>
    /// Open the "More" overflow button's popup, tap an item inside by AutomationId,
    /// and let the popup auto-close on tap. Returns false if the overflow button
    /// isn't present or the item isn't in the opened popup (popup is closed on miss).
    /// </summary>
    private static bool TryTapOverflowPopupItem(AppiumDriver driver, string automationId)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(MobileBy.AccessibilityId("More")).Click();
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

        // TODO(#11): map to TestConfig.Delay* — 400ms popup-animation settle,
        // between DelayAfterTap (300) and DelayAfterNavigation (500). Deferred ambiguous site.
        Thread.Sleep(400); // popup animation settle

        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(AutomationIdLocator(automationId)).Click();
            Thread.Sleep(TestConfig.DelayAfterTap);
            return true;
        }
        catch (WebDriverException)
        {
            CloseOverflowPopupIfOpen(driver);
            return false;
        }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>Does the target live inside the overflow popup? Opens+peeks+closes.</summary>
    private static bool IsInOverflowPopup(AppiumDriver driver, string automationId)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(MobileBy.AccessibilityId("More")).Click();
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }

        // TODO(#11): map to TestConfig.Delay* — 400ms popup-animation settle (mirror of line ~870).
        // Deferred ambiguous site.
        Thread.Sleep(400);

        bool found;
        try
        {
            found = driver.FindElements(AutomationIdLocator(automationId)).Count > 0;
        }
        finally { CloseOverflowPopupIfOpen(driver); }
        return found;
    }

    /// <summary>
    /// Dismiss the overflow popup by tapping outside its content. Popup is centered
    /// by default, so we tap near the top-left where the popup Border never extends.
    /// Android also accepts system back.
    /// </summary>
    private static void CloseOverflowPopupIfOpen(AppiumDriver driver)
    {
        try
        {
            if (TestConfig.IsAndroid)
            {
                driver.Navigate().Back();
            }
            else
            {
                var size = driver.Manage().Window.Size;
                driver.ExecuteScript("mobile: tap", new Dictionary<string, object>
                {
                    { "x", 10 }, { "y", size.Height - 20 }
                });
            }
            Thread.Sleep(TestConfig.DelayAfterDismiss);
        }
        catch (WebDriverException) { /* best effort */ }
    }

    /// <summary>Open MAUI's iOS Secondary toolbar menu. Returns true on success.</summary>
    /// <remarks>1s sleep: UIMenu popover animation + XCUITest tree settle; 500ms races on cold-start.</remarks>
    private static bool OpenIOSSecondaryMenu(AppiumDriver driver)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(MobileBy.AccessibilityId("SecondaryToolbarMenuButton")).Click();
            Thread.Sleep(TestConfig.DelayModalAnimation);
            return true;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>
    /// Dismiss the iOS <c>UIMenu</c> popover by tapping mid-screen. Re-tapping the
    /// trigger selects the first menu item instead of toggling the menu closed.
    /// </summary>
    private static void CloseIOSPopover(AppiumDriver driver)
    {
        try
        {
            var size = driver.Manage().Window.Size;
            driver.ExecuteScript("mobile: tap", new Dictionary<string, object>
            {
                { "x", size.Width / 2 }, { "y", size.Height / 2 }
            });
            Thread.Sleep(TestConfig.DelayAfterDismiss);
        }
        catch (WebDriverException) { /* best effort */ }
    }

    /// <summary>
    /// iOS: open the Secondary toolbar menu and tap a menu item whose name/label
    /// matches <paramref name="itemText"/>. UIMenu items carry no accessibilityIdentifier —
    /// only their title — so lookup is by name/label.
    /// </summary>
    private static bool TryTapIOSSecondaryMenuItem(AppiumDriver driver, string itemText)
    {
        if (!OpenIOSSecondaryMenu(driver)) return false;
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(IOSButtonByNameOrLabel(itemText)).Click();
            Thread.Sleep(TestConfig.DelayAfterTap);
            return true;
        }
        catch (WebDriverException)
        {
            CloseIOSPopover(driver);
            return false;
        }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>
    /// Tap a Shell ToolbarItem by visible text (e.g. "Save", "Edit"). Works for
    /// text-only toolbars (no <c>IconImageSource</c>). For iconized ToolbarItems, use
    /// <see cref="TapToolbarItemById"/> instead — icon-only rendering removes the
    /// visible <c>Text</c> from the UiAutomator2 tree on Android, which makes text
    /// lookup fragile. See <c>Lessons/uitest-automation-ids-over-visible-text.md</c>.
    /// </summary>
    public static void TapToolbarItem(this AppiumDriver driver, string text, int timeoutSeconds = 10)
    {
        driver.DismissKeyboardIfPresent();
        if (TestConfig.IsIOS) Thread.Sleep(TestConfig.DelayAfterTap);

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));

        By locator = TestConfig.IsIOS ? IOSButtonByNameOrLabel(text) : TextLocator(text);

        var element = wait.Until(d => d.FindElement(locator));
        element.Click();
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    /// <summary>Find an element by its visible text.</summary>
    public static AppiumElement FindByText(this AppiumDriver driver, string text, int timeoutSeconds = 10)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        return (AppiumElement)wait.Until(d => d.FindElement(TextLocator(text)));
    }

    /// <summary>Find an element whose text/label contains the given substring.</summary>
    public static AppiumElement FindByTextContains(this AppiumDriver driver, string text, int timeoutSeconds = 10)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        return (AppiumElement)wait.Until(d => d.FindElement(TextContainsLocator(text)));
    }

    /// <summary>Find and tap any element by its visible text.</summary>
    public static void TapByText(this AppiumDriver driver, string text, int timeoutSeconds = 10)
    {
        driver.FindByText(text, timeoutSeconds).Click();
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    /// <summary>Find and tap any element whose text/label <em>contains</em> the given substring.</summary>
    public static void TapByTextContains(this AppiumDriver driver, string text, int timeoutSeconds = 10)
    {
        driver.FindByTextContains(text, timeoutSeconds).Click();
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    /// <summary>
    /// iOS-only: select <paramref name="value"/> in a MAUI <c>Picker</c> that is already open.
    /// Call AFTER tapping the Picker open. No-op on Android (whose Picker opens a dialog of
    /// tappable rows, so the Android call sites keep using <see cref="TapByText"/>).
    /// </summary>
    /// <remarks>
    /// On iOS a MAUI Picker presents a native <c>UIPickerView</c> spinning wheel — Microsoft's
    /// Picker docs describe it as "a picker interface instead of a keyboard". A picker wheel's
    /// options are NOT individual accessibility elements — only the single
    /// <c>XCUIElementTypePickerWheel</c> exposes a settable value — so the Android pattern of
    /// tapping the option by its text (<see cref="TapByText"/> / <see cref="TapByTextContains"/>)
    /// can never match on iOS and dies with a NoSuchElement timeout. Instead set the wheel's value
    /// (Appium's picker-wheel pattern, which maps to XCUITest <c>adjustToPickerWheelValue:</c>) and
    /// return — there is no "Done" affordance to tap (page-source dump of the open picker on this
    /// MAUI 10 / iOS 26.4 build shows the inline wheel and a page toolbar, but no Done button) and
    /// none is needed: the iOS Picker's default <c>UpdateMode</c> is <c>Immediately</c> (Microsoft
    /// docs: "item selection occurs as the user browses items … the default behavior in .NET MAUI";
    /// <c>Detail_Picker_Card</c> sets no override), so settling the wheel fires the handler's
    /// <c>didSelectRow</c> and writes <c>SelectedItem</c> back live. The caller's own
    /// <c>TapToolbarItem("Save")</c> then persists the committed selection.
    /// </remarks>
    /// <param name="value">Exact option text to select — for the card picker this is the card Title.</param>
    public static void SelectIOSPickerValue(this AppiumDriver driver, string value, int maxAttempts = 6)
    {
        if (!TestConfig.IsIOS) return;

        // The open picker exposes exactly one spinning wheel (the card picker is single-column).
        // Locate it by XPath element type, not By.ClassName — in this Selenium version
        // By.ClassName emits a CSS selector ('.XCUIElementTypePickerWheel'), which Appium's
        // iOS driver rejects (InvalidSelectorException). XPath is the iOS-native strategy used
        // elsewhere in this file (e.g. IOSButtonByNameOrLabel).
        var wheelBy = By.XPath("//XCUIElementTypePickerWheel");
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        wait.Until(d => d.FindElement(wheelBy));

        // RE-FIND the wheel every pass — never cache it across a SendKeys. SendKeys maps to
        // XCUITest adjustToPickerWheelValue:, which re-renders the wheel and invalidates the
        // prior element's fb_uid, so a cached reference throws StaleElementReferenceException on
        // the next read. This mirrors the codebase's re-find-per-iteration idiom (ScrollDownUntil /
        // EnsureAllSectionsExpanded). adjustToPickerWheelValue rotates toward the value; some
        // XCUITest builds advance only one row per call, so retry (bounded) until the wheel's
        // reported value reaches the target rather than assuming a single hop suffices. Each settle
        // fires didSelectRow, committing SelectedItem live (Immediately mode) — no Done tap needed.
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var wheel = (AppiumElement)driver.FindElement(wheelBy);
                if ((wheel.Text ?? string.Empty).Contains(value, StringComparison.Ordinal))
                    break;
                wheel.SendKeys(value);
            }
            catch (StaleElementReferenceException) { /* wheel re-rendered mid-pass; re-find next loop */ }
            Thread.Sleep(TestConfig.DelayAfterTap);
        }
    }

    /// <summary>Check if an element with the given text is displayed.</summary>
    public static bool IsTextDisplayed(this AppiumDriver driver, string text, int timeoutSeconds = 3)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
            var element = driver.FindElement(TextLocator(text));
            return element.Displayed;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>Check if an element whose text/label <em>contains</em> the given substring is displayed.</summary>
    public static bool IsTextContainsDisplayed(this AppiumDriver driver, string text, int timeoutSeconds = 3)
    {
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
            var element = driver.FindElement(TextContainsLocator(text));
            return element.Displayed;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    // ── Swipe Gestures ──────────────────────────────────────────

    /// <summary>Swipe an element in the given direction.</summary>
    private static void SwipeElement(AppiumDriver driver, AppiumElement element, string direction)
    {
        var location = element.Location;
        var size = element.Size;
        var centerX = location.X + size.Width / 2;
        var centerY = location.Y + size.Height / 2;

        if (TestConfig.IsIOS)
        {
            // Use coordinate-based drag for reliable SwipeView triggering on iOS.
            // mobile: swipe with elementId doesn't always reach the inner SwipeView content.
            int dx = direction switch { "left" => -200, "right" => 200, _ => 0 };
            int dy = direction switch { "up" => -200, "down" => 200, _ => 0 };
            driver.ExecuteScript("mobile: dragFromToForDuration", new Dictionary<string, object>
            {
                { "fromX", centerX },
                { "fromY", centerY },
                { "toX", centerX + dx },
                { "toY", centerY + dy },
                { "duration", 0.3 }
            });
        }
        else
        {
            // Android UiAutomator2: "mobile: swipeGesture" with bounding area
            driver.ExecuteScript("mobile: swipeGesture", new Dictionary<string, object>
            {
                { "left", location.X },
                { "top", location.Y },
                { "width", size.Width },
                { "height", size.Height },
                { "direction", direction },
                { "percent", 0.5 }
            });
        }
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    /// <summary>Swipe left on an element (to reveal right swipe actions like Delete).</summary>
    public static void SwipeElementLeft(this AppiumDriver driver, AppiumElement element)
        => SwipeElement(driver, element, "left");

    /// <summary>Swipe right on an element (to reveal left swipe actions like Favorite/Edit).</summary>
    public static void SwipeElementRight(this AppiumDriver driver, AppiumElement element)
        => SwipeElement(driver, element, "right");

    /// <summary>Long-press an element (for multi-select entry). Duration 750ms.</summary>
    public static void LongPress(this AppiumDriver driver, AppiumElement element)
    {
        var location = element.Location;
        var size = element.Size;
        var centerX = location.X + size.Width / 2;
        var centerY = location.Y + size.Height / 2;

        if (TestConfig.IsIOS)
        {
            driver.ExecuteScript("mobile: touchAndHold", new Dictionary<string, object>
            {
                { "x", centerX },
                { "y", centerY },
                { "duration", 0.75 }
            });
        }
        else
        {
            driver.ExecuteScript("mobile: longClickGesture", new Dictionary<string, object>
            {
                { "x", centerX },
                { "y", centerY },
                { "duration", 750 }
            });
        }
    }

    /// <summary>
    /// iOS: tap an alert button by its visible label using Appium's native
    /// <c>mobile: alert</c>. Returns false if no alert or no such button. Preferred
    /// over XPath — iPad renders <c>UIAlertController</c> with styles that don't
    /// always surface as <c>XCUIElementTypeAlert</c> in the XCUI tree.
    /// </summary>
    private static bool TryTapIOSAlertButton(AppiumDriver driver, string buttonLabel)
    {
        if (!TestConfig.IsIOS) return false;
        try
        {
            driver.ExecuteScript("mobile: alert", new Dictionary<string, object>
            {
                { "action", "accept" },
                { "buttonLabel", buttonLabel }
            });
            Thread.Sleep(TestConfig.DelayAfterDismiss);
            return true;
        }
        catch (WebDriverException) { return false; }
    }

    /// <summary>Check if an alert dialog is currently showing.</summary>
    public static bool IsAlertPresent(this AppiumDriver driver)
    {
        if (TestConfig.IsIOS)
        {
            try { driver.SwitchTo().Alert(); return true; }
            catch (WebDriverException) { return false; }
        }

        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;
            driver.FindElement(By.XPath(
                "//*[@resource-id='android:id/message' or @resource-id='android:id/alertTitle']"));
            return true;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout; }
    }

    /// <summary>Navigate back to a tab root, going back multiple times if needed.</summary>
    public static void NavigateToTabRoot(this AppiumDriver driver, string tabTitle, string rootElementId,
        AppiumSetup setup, int maxBacks = 5)
    {
        driver.DismissOnboardingIfPresent(setup);
        driver.NavigateToTab(tabTitle);
        for (int i = 0; i < maxBacks; i++)
        {
            if (driver.IsDisplayed(rootElementId, timeoutSeconds: 2))
                return;
            driver.GoBack();
            Thread.Sleep(TestConfig.DelayAfterTap);
        }
    }

    // ── Test Setup Helpers ─────────────────────────────────────

    /// <summary>
    /// Lands on the Prayers tab with the list rendered. Trusts the seed DB — does NOT
    /// verify visibility of "UI Test Prayer" because CollectionView virtualizes off-screen
    /// rows and UIAutomator2 won't expose them, producing false-negatives on existence.
    /// TestDataSeed is the single source of truth for the seeded prayer; callers that need
    /// the prayer to be user-visible should drive to it explicitly (search, scroll, or card-expand).
    /// </summary>
    public static void EnsureOnPrayersTab(this AppiumDriver driver, AppiumSetup setup)
    {
        driver.EnsureOnTab("Prayers", setup);
        driver.WaitForElement("List_List_Prayers", timeoutSeconds: 10);
    }

    /// <summary>
    /// Ensures a tag named "UITest Tag" exists. Creates it via Tags tab if missing.
    /// Needed for tests that require the action sheet (prayers + tags must both exist).
    /// </summary>
    public static void EnsureUITestTagExists(this AppiumDriver driver, AppiumSetup setup)
    {
        driver.EnsureOnTab("Tags", setup);
        if (driver.IsTextDisplayed("UITest Tag", timeoutSeconds: 3))
            return;

        // Create via Tags tab toolbar
        driver.TapToolbarItem("Add");
        driver.WaitForElement("TagDetail_Entry_Name", timeoutSeconds: 10);
        driver.EnterText("TagDetail_Entry_Name", "UITest Tag");
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Verify we're back on tag list (iOS Bug #3: GoToAsync("..") may fail)
        if (!driver.IsDisplayed("Tags_List_Tags", timeoutSeconds: 10) && TestConfig.IsIOS)
            driver.NavigateToTab("Tags");

        Thread.Sleep(TestConfig.DelayCollectionRender);
    }

    /// <summary>
    /// Ensures a "UITest Collection" exists in the Manage Collections page.
    /// Creates it if not found, then navigates back to the calling context.
    /// </summary>
    public static void EnsureUITestCollectionExists(this AppiumDriver driver, AppiumSetup setup)
    {
        driver.EnsureOnTab("Prayer Cards", setup);
        driver.TapToolbarItemById("Collections");
        driver.WaitForElement("Boxes_List_Boxes", timeoutSeconds: 10);

        if (driver.IsTextDisplayed("UITest Collection", timeoutSeconds: 3))
        {
            driver.GoBack();
            return;
        }

        driver.TapToolbarItem("Add");
        driver.WaitForElement("BoxDetail_Entry_Name", timeoutSeconds: 10);
        driver.EnterText("BoxDetail_Entry_Name", "UITest Collection");
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // CollectionView virtualization can hide an existing row from the text check
        // above, so Save may raise a "Duplicate Name" alert. Accept it, discard the
        // dirty form, and return to the list instead of hanging.
        if (driver.IsAlertPresent())
        {
            driver.DismissAlertIfPresent();
            driver.GoBack();
            driver.DismissAlertIfPresent();
        }

        if (!driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10) && TestConfig.IsIOS)
            driver.GoBack();

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
    }

    /// <summary>
    /// Find a card cell element by name, suitable for swiping. On iOS, CollectionView
    /// flattens cells so text search returns a tiny inner label — this finds the parent
    /// XCUIElementTypeCell container which has enough height for reliable swipe gestures.
    /// </summary>
    public static AppiumElement? FindCardCell(this AppiumDriver driver, string cardName, int timeoutSeconds = 10)
    {
        try
        {
            if (TestConfig.IsIOS)
            {
                // Find the Cell that contains the card name text
                var xpath = $"//XCUIElementTypeCell[.//XCUIElementTypeStaticText[contains(@name, '{cardName}')]]";
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
                var cell = (AppiumElement)driver.FindElement(By.XPath(xpath));
                driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
                return cell;
            }
            return (AppiumElement)driver.FindElement(TextLocator(cardName));
        }
        catch (WebDriverException)
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
            return null;
        }
    }

    /// <summary>
    /// Lands on the Prayer Cards tab with the list rendered. Trusts the seed DB — does NOT
    /// verify visibility of "UITest Card" (see EnsureOnPrayersTab for rationale: visibility
    /// is not a reliable existence proof under CollectionView virtualization, and silent
    /// fallback-create was creating duplicate fixtures on every run).
    /// </summary>
    public static void EnsureUITestCardExists(this AppiumDriver driver, AppiumSetup setup)
    {
        driver.EnsureOnTab("Prayer Cards", setup);
        driver.WaitForElement("Cards_List_Cards", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayCollectionRender);
    }

    /// <summary>Navigate to a new prayer form in edit mode (Prayers tab → Add toolbar).</summary>
    public static void NavigateToNewPrayer(this AppiumDriver driver, AppiumSetup setup)
    {
        driver.EnsureOnTab("Prayers", setup);
        // Wait for the Prayers list to render before tapping the toolbar — prevents
        // racing the "Add" tap against an un-rendered Shell action bar.
        driver.WaitForElement("List_List_Prayers", timeoutSeconds: 10);
        // Wait for the Shell top-bar to actually render the "Add" item — it lags the page
        // content, and tapping before it exists (or before its Command="{Binding
        // NewCommand}" binding goes live) is a no-op: the click can return HTTP 200 yet
        // route nothing, so the detail page never opens. ById, not text: Android
        // uppercases the label to "ADD" and exposes no content-desc for a text match;
        // AutomationId maps to content-desc (case-exact), matching the cards/boxes pattern.
        // Settle so the binding is live, then retry the tap until the title entry appears.
        driver.WaitForElement("Add", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterNavigation);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!driver.IsDisplayed("Add", timeoutSeconds: 2)) break; // navigated away — Add gone
            driver.TapToolbarItemById("Add");
            if (driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 5)) break;
        }
        driver.WaitForElement("Detail_Entry_Title", timeoutSeconds: 10);
    }

    /// <summary>Accept any visible alert by tapping its positive button (OK/Yes).</summary>
    public static void DismissAlertIfPresent(this AppiumDriver driver)
    {
        if (TestConfig.IsIOS)
        {
            // Some iOS modals need two accept attempts — the first dismisses the
            // visible alert but a follow-up system alert (e.g. permission) may
            // pop immediately after. See Lessons/uitest-per-test-ui-state-reset.md.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // App-level DisplayAlertAsync "Unsaved Changes → Discard changes?" uses
                // Discard (destructive) + Cancel. Appium's `autoDismissAlerts` auto-taps
                // Cancel, leaving the form dirty and trapping the back-out loop. Explicit
                // Discard tap escapes. Mirrors any other destructive confirm we may add.
                if (TryTapIOSAlertButton(driver, "Discard")) continue;
                try
                {
                    driver.SwitchTo().Alert().Accept();
                    Thread.Sleep(TestConfig.DelayAfterDismiss);
                }
                catch (WebDriverException)
                {
                    return; // no (more) alerts
                }
            }
            return;
        }

        try
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.ShortTimeout;

            // Prefer positive button (button1 = OK/Yes) over negative (button2 = Cancel)
            var positiveButtons = driver.FindElements(By.Id("android:id/button1"));
            if (positiveButtons.Count > 0)
            {
                positiveButtons[0].Click();
            }
            else
            {
                var anyButton = driver.FindElement(By.XPath(
                    "//*[@resource-id='android:id/button2' or @text='OK' or @text='Cancel']"));
                anyButton.Click();
            }
            Thread.Sleep(TestConfig.DelayAfterDismiss);
        }
        catch (WebDriverException) { }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }
    }

    // ── iOS Action Sheet helpers ───────────────────────────────

    /// <summary>
    /// iOS-specific: Tap a button in an action sheet using <c>mobile: tap</c> with the
    /// element's center coordinates. On iPad, action sheets render as popovers whose
    /// animation causes WebDriver's <c>Click()</c> to send stale coordinates; the XCUITest
    /// native tap avoids that. Callers must ensure the popover has finished animating
    /// before invoking this (the element's rect is read before the tap fires).
    /// Falls back to standard <c>TapByText</c> on Android.
    /// </summary>
    public static void TapIOSActionSheetButton(this AppiumDriver driver, string buttonName,
        int timeoutSeconds = 10)
    {
        if (!TestConfig.IsIOS)
        {
            driver.TapByText(buttonName, timeoutSeconds);
            return;
        }

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        var element = (AppiumElement)wait.Until(d => d.FindElement(IOSButtonByNameOrLabel(buttonName)));

        // Use XCUITest native tap with element center coords — immune to iPad popover
        // animation coordinate drift, and the driver no longer accepts elementId alone.
        var loc = element.Location;
        var size = element.Size;
        driver.ExecuteScript("mobile: tap", new Dictionary<string, object>
        {
            { "x", loc.X + size.Width / 2 },
            { "y", loc.Y + size.Height / 2 }
        });
        Thread.Sleep(TestConfig.DelayAfterTap);
    }

    // ── iOS CollectionView helpers ─────────────────────────────

    /// <summary>
    /// iOS-specific: Uses <c>mobile: scroll</c> with <c>predicateString</c> to scroll a
    /// container until an element matching the predicate is visible. This reaches inside
    /// CollectionView cells where standard <c>findElement</c> cannot.
    /// </summary>
    /// <returns>True if the scroll command executed without error (element found by the driver).</returns>
    public static bool IOSScrollToPredicateInContainer(this AppiumDriver driver,
        string containerAutomationId, string predicateString, int maxAttempts = 3)
        => IOSScrollInContainer(driver, containerAutomationId, "predicateString", predicateString, maxAttempts);

    /// <summary>
    /// iOS-specific: Uses <c>mobile: scroll</c> with the <c>name</c> parameter to scroll a
    /// container until an element with the given accessibility ID is visible.
    /// </summary>
    public static bool IOSScrollToNameInContainer(this AppiumDriver driver,
        string containerAutomationId, string accessibilityName, int maxAttempts = 3)
        => IOSScrollInContainer(driver, containerAutomationId, "name", accessibilityName, maxAttempts);

    /// <summary>
    /// Shared implementation for iOS <c>mobile: scroll</c> with a finder parameter
    /// (e.g. "predicateString" or "name") that reaches inside CollectionView cells.
    /// </summary>
    private static bool IOSScrollInContainer(AppiumDriver driver,
        string containerAutomationId, string finderKey, string finderValue, int maxAttempts)
    {
        if (!TestConfig.IsIOS) return false;

        string? containerId;
        try
        {
            containerId = driver.FindElement(MobileBy.AccessibilityId(containerAutomationId)).Id;
        }
        catch (NoSuchElementException) { return false; }

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                driver.ExecuteScript("mobile: scroll", new Dictionary<string, object>
                {
                    { "elementId", containerId },
                    { "direction", "down" },
                    { finderKey, finderValue }
                });
                return true;
            }
            catch (WebDriverException)
            {
                // Element not yet reachable — try scrolling again
            }
        }
        return false;
    }

    // ── Accessibility Assertions ───────────────────────────────

    /// <summary>
    /// Get the accessible description of an element found by AutomationId.
    /// Android: reads content-desc. iOS: reads label attribute.
    /// Returns empty string if attribute not found.
    /// </summary>
    /// <remarks>
    /// iOS Secondary toolbar items live in a <c>UIMenu</c> pull-down, not in the
    /// navbar tree. If direct lookup fails on iOS, open the Secondary menu and read
    /// the <c>UIAction</c>'s label there, then close the menu.
    /// </remarks>
    public static string GetAccessibleDescription(this AppiumDriver driver, string automationId)
        => ReadToolbarAccessibleAttribute(driver, automationId,
            androidAttr: "content-desc", iosAttr: "label");

    /// <summary>
    /// Get the accessible hint of an element found by AutomationId.
    /// Android: reads hint attribute. iOS: reads value attribute.
    /// </summary>
    public static string GetAccessibleHint(this AppiumDriver driver, string automationId)
        => ReadToolbarAccessibleAttribute(driver, automationId,
            androidAttr: "hint", iosAttr: "value");

    /// <summary>
    /// Shared implementation: read an accessibility attribute by AutomationId, with
    /// iOS Secondary-menu fallback for items that only render inside MAUI's UIMenu.
    /// </summary>
    private static string ReadToolbarAccessibleAttribute(AppiumDriver driver,
        string automationId, string androidAttr, string iosAttr)
    {
        string Attr(AppiumElement el) => TestConfig.IsAndroid
            ? el.GetDomAttribute(androidAttr) ?? ""
            : el.GetDomAttribute(iosAttr) ?? "";

        // Fast path: item is in the tree directly. 0s wait lets us reuse FindByAutomationId.
        if (driver.IsDisplayed(automationId, timeoutSeconds: 2))
            return Attr(driver.FindByAutomationId(automationId));

        if (TestConfig.IsIOS && OpenIOSSecondaryMenu(driver))
        {
            try
            {
                return Attr((AppiumElement)driver.FindElement(IOSButtonByNameOrLabel(automationId)));
            }
            catch (WebDriverException) { }
            finally { CloseIOSPopover(driver); }
        }

        // Last resort: let the standard WaitForElement surface the canonical error.
        var element = driver.WaitForElement(automationId, timeoutSeconds: 10);
        return element == null ? "" : Attr(element);
    }

    /// <summary>
    /// Check whether an element with the given description/label text exists in the accessibility tree.
    /// Works on both platforms by searching content-desc (Android) or label (iOS).
    /// Useful for elements without AutomationId that only have SemanticProperties.Description.
    /// </summary>
    public static bool HasAccessibleElement(this AppiumDriver driver, string descriptionText,
        int timeoutSeconds = 3)
    {
        // Use contains() for partial matching — composed descriptions may have extra context.
        // Android: search both @content-desc and @text because UiAutomator2 may not expose
        // layout containers (Grid) as nodes — the text can live on child views instead.
        By locator = TestConfig.IsAndroid
            ? By.XPath($"//*[contains(@content-desc,'{descriptionText}') or contains(@text,'{descriptionText}')]")
            : By.XPath($"//*[contains(@label,'{descriptionText}') or contains(@name,'{descriptionText}')]");
        var prev = driver.Manage().Timeouts().ImplicitWait;
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
            driver.FindElement(locator);
            return true;
        }
        catch (WebDriverException) { return false; }
        finally { driver.Manage().Timeouts().ImplicitWait = prev; }
    }

    /// <summary>
    /// Assert that NO accessible element contains the given text.
    /// Used to verify decorative elements are hidden from the tree.
    /// Android-only — iOS flattening makes this unreliable for children inside Description'd containers.
    /// </summary>
    public static void AssertNotInAccessibleTree(this AppiumDriver driver, string text,
        int timeoutSeconds = 2)
    {
        if (TestConfig.IsIOS) return; // iOS flattening makes child-level tree assertions unreliable

        By locator = By.XPath($"//*[@content-desc='{text}' or @text='{text}']");
        var prev = driver.Manage().Timeouts().ImplicitWait;
        try
        {
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(timeoutSeconds);
            driver.FindElement(locator);
            throw new Xunit.Sdk.XunitException(
                $"Element with text '{text}' should not be in the accessibility tree but was found");
        }
        catch (WebDriverException)
        {
            // Expected — element correctly hidden
        }
        finally { driver.Manage().Timeouts().ImplicitWait = prev; }
    }

    /// <summary>
    /// Android-only: Assert an element is marked as a heading in the accessibility tree.
    /// iOS/XCUITest does not expose heading status via Appium.
    /// </summary>
    public static void AssertIsHeading(this AppiumDriver driver, string automationId)
    {
        if (TestConfig.IsIOS) return; // heading attribute not exposed on iOS

        var element = driver.WaitForElement(automationId, timeoutSeconds: 10)
            ?? throw new Xunit.Sdk.XunitException(
                $"Element '{automationId}' not found for heading check");
        var heading = element.GetDomAttribute("heading");
        if (heading != "true")
            throw new Xunit.Sdk.XunitException(
                $"Element '{automationId}' expected heading='true' but got '{heading}'");
    }

    // ── Diagnostics ──────────────────────────────────────────────

    /// <summary>
    /// Dumps the Appium page source (accessibility tree) to a file in the test output directory.
    /// Use this to diagnose elements that are visually present but not found by Appium locators.
    /// Returns the file path written to.
    /// </summary>
    public static string DumpPageSource(this AppiumDriver driver, string testName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var filePath = Path.Combine(dir, $"{testName}_{timestamp}.xml");
        File.WriteAllText(filePath, driver.PageSource);
        return filePath;
    }

    // ── Platform Intent Helpers ──────────────────────────────────

    /// <summary>
    /// Android-only: dispatch <c>ACTION_PROCESS_TEXT</c> at MainActivity so the
    /// Slice 2 selection-toolbar handoff can be exercised without driving Gmail's UI.
    /// Multi-line payloads are not supported here because <c>am start --es</c> tokenises
    /// values through adb's shell — newlines are stripped. Production multi-line parsing
    /// is covered by <c>TextSelectionParser</c> unit tests; manual emulator smoke covers
    /// the real Gmail → toolbar → modal end-to-end path.
    /// </summary>
    public static void LaunchProcessTextIntent(this AppiumDriver driver, AppiumSetup setup, string text)
    {
        if (TestConfig.IsIOS)
            throw new SkipException("Android-only: PROCESS_TEXT is the Android selection-toolbar entry point");

        ValidateAmShellText(text, nameof(text));

        // Two-stage foreground → dismiss-onboarding → dispatch, symmetric with
        // `LaunchProcessTextIntentSpannable`. Doing PROCESS_TEXT in a single `am start`
        // off a force-stopped app delivers the intent directly into HandleSelectionImport
        // during cold-start — which opens the ConfirmImport modal before any onboarding
        // popup is dismissed AND before DismissOnboardingIfPresent's fast-path
        // (IsDisplayed("Home", 1s)) can find a visible tab bar.
        //
        // Stage 1 foreground via plain LAUNCHER `am start` so MainActivity displays the
        // Home tab. DismissOnboarding then fast-paths in 1s when no popup is present, or
        // dismisses cleanly when one is. Stage 2 re-`am start`s with `-a PROCESS_TEXT`,
        // routing through OnNewIntent on the now-foregrounded SingleTop MainActivity.
        //
        // Explicit `-n pkg/activity` is required on both stages — `mobile: startActivity`
        // with appPackage + appActivity + intentAction builds an implicit intent that
        // Android's resolver routes to the system default PROCESS_TEXT handler
        // (DocumentsUI / PickActivity on API 36+) rather than MainActivity.
        //
        // Requires Appium server flag `--allow-insecure=uiautomator2:adb_shell`.

        // Invalidate the cached OnboardingHandled flag FIRST so any failure between
        // here and the post-dismiss readiness check leaves the flag in the "needs
        // re-check" state rather than a stale "true" from a prior test.
        setup.OnboardingHandled = false;

        // Stage 1: foreground MainActivity with LAUNCHER intent.
        RunAmShellOrThrow(driver,
            new[] { "start", "-n", $"{TestConfig.AndroidPackage}/{TestConfig.AndroidMainActivity}" },
            nameof(LaunchProcessTextIntent));

        WaitForAppReadyOrThrow(driver, nameof(LaunchProcessTextIntent));

        driver.DismissOnboardingIfPresent(setup);

        AssertHomeVisibleAfterDismiss(driver, setup, nameof(LaunchProcessTextIntent));

        // Stage 2: dispatch PROCESS_TEXT — routes through OnNewIntent on SingleTop.
        RunAmShellOrThrow(driver,
            new[]
            {
                "start",
                "-n", $"{TestConfig.AndroidPackage}/{TestConfig.AndroidMainActivity}",
                "-a", "android.intent.action.PROCESS_TEXT",
                "-t", "text/plain",
                "--es", "android.intent.extra.PROCESS_TEXT", text
            },
            nameof(LaunchProcessTextIntent));
    }

    /// <summary>
    /// Android-only: force-stop the app so the next intent launch starts a fresh
    /// <c>MainActivity</c>. Eliminates a class of flakes where a prior test left
    /// MainActivity backgrounded and the new PROCESS_TEXT intent is delivered to
    /// the existing instance (subject to <c>LaunchMode.SingleTop</c> reuse) instead
    /// of triggering an <c>OnCreate</c>/<c>OnNewIntent</c> path the test asserts on.
    /// Wraps <c>mobile: shell am force-stop</c>; requires Appium server flag
    /// <c>--allow-insecure=uiautomator2:adb_shell</c> (or <c>--relaxed-security</c>).
    /// </summary>
    public static void ForceStopApp(this AppiumDriver driver)
    {
        if (TestConfig.IsIOS)
            throw new SkipException("Android-only: force-stop is an adb/am operation");

        driver.ExecuteScript("mobile: shell", new Dictionary<string, object>
        {
            { "command", "am" },
            { "args", new[] { "force-stop", TestConfig.AndroidPackage } }
        });
    }

    /// <summary>
    /// Android-only DEBUG-build helper: dispatch <c>ACTION_PROCESS_TEXT</c> with a
    /// real <c>SpannableString</c> payload, mirroring how Chrome / Gmail deliver the
    /// extra in production (the OS attaches markup spans to selected rich text).
    /// Invokes the host-side <c>DebugProcessTextShim</c> broadcast receiver, which
    /// constructs a <c>SpannableString</c> and re-dispatches via the real PROCESS_TEXT
    /// pipeline — so production code (<c>GetCharSequenceExtra</c>) is exercised.
    /// </summary>
    /// <remarks>
    /// The companion <see cref="LaunchProcessTextIntent"/> uses <c>am start --es</c>,
    /// which only puts a plain <c>String</c> extra — it does NOT defend the
    /// SpannableString boundary. Receiver is <c>#if DEBUG</c> only and must NOT
    /// ship to Release. Requires Appium server flag
    /// <c>--allow-insecure=uiautomator2:adb_shell</c> (or <c>--relaxed-security</c>).
    /// </remarks>
    public static void LaunchProcessTextIntentSpannable(this AppiumDriver driver, AppiumSetup setup, string text)
    {
        if (TestConfig.IsIOS)
            throw new SkipException("Android-only: PROCESS_TEXT is the Android selection-toolbar entry point");

        ValidateAmShellText(text, nameof(text));

        // Foreground MainActivity FIRST. Android 14+ (API 34+) enforces Background
        // Activity Launch (BAL) restrictions: when the broadcast receiver below
        // fires inside a force-stopped or backgrounded app, its StartActivity call
        // is blocked with BAL_BLOCK. Foregrounding MainActivity gives the receiver's
        // process a visible-activity owner, which qualifies for BAL_ALLOW_VISIBLE_WINDOW.
        // Companion `LaunchProcessTextIntent` foregrounds for a different reason
        // (intent-resolution explicitness rather than BAL): both helpers share the
        // foreground → poll → dismiss → dispatch structure for symmetry.

        // Invalidate the cached OnboardingHandled flag FIRST (force-stop destroyed
        // the underlying UI state but not the cache) so any failure between here
        // and the post-dismiss readiness check leaves the flag in the "needs
        // re-check" state.
        setup.OnboardingHandled = false;

        RunAmShellOrThrow(driver,
            new[] { "start", "-n", $"{TestConfig.AndroidPackage}/{TestConfig.AndroidMainActivity}" },
            nameof(LaunchProcessTextIntentSpannable));

        WaitForAppReadyOrThrow(driver, nameof(LaunchProcessTextIntentSpannable));

        driver.DismissOnboardingIfPresent(setup);

        AssertHomeVisibleAfterDismiss(driver, setup, nameof(LaunchProcessTextIntentSpannable));

        // `am broadcast` arg list. Appium's `mobile: shell` invocation passes the
        // args array as separate argv tokens (not via `sh -c` string concatenation),
        // so spaces inside `text` survive — `ValidateAmShellText` above rejects the
        // shell metacharacters that would actually corrupt the payload. Newlines
        // are still stripped by the adb tokeniser; multi-line payloads remain out
        // of scope for this helper.
        RunAmShellOrThrow(driver,
            new[]
            {
                "broadcast",
                "-a", "com.multithreadedllc.prayercards.PRAYER_TEST_SPANNABLE",
                "-n", $"{TestConfig.AndroidPackage}/.DebugProcessTextShim",
                "--es", "text", text
            },
            nameof(LaunchProcessTextIntentSpannable));
    }

    /// <summary>
    /// Reject `text` values containing shell metacharacters that would corrupt
    /// `am`-shell argument tokenisation. The args-array invocation style of
    /// <c>mobile: shell</c> passes each element as a separate argv token, but
    /// embedded metacharacters can still interact poorly with adb's parser on
    /// some Appium driver versions. Whitelisting the safe set is cheaper than
    /// guessing the actual tokenisation contract.
    /// </summary>
    private static void ValidateAmShellText(string text, string paramName)
    {
        if (text is null)
            throw new ArgumentNullException(paramName);

        foreach (var c in text)
        {
            if (c is '\'' or '"' or '`' or '$' or '\\' or '\n' or '\r')
                throw new ArgumentException(
                    $"`{paramName}` contains a shell metacharacter (quote, backtick, $, backslash, or newline). " +
                    "These corrupt am-shell argument tokenisation across Appium driver versions. " +
                    "Use plain alphanumeric + space text only — production multi-line / rich-text parsing " +
                    "is covered by TextSelectionParser unit tests.",
                    paramName);
        }
    }

    /// <summary>
    /// Poll up to 20s for the app to display either the Home tab (warm/onboarded
    /// state) or the Welcome popup (fresh-emulator state). Throws on timeout so a
    /// stale CRC, missing Appium insecure flag, or severely under-resourced
    /// emulator surfaces as a clear error rather than a downstream
    /// <c>WebDriverTimeoutException</c> at <c>WaitForElement</c>.
    /// </summary>
    /// <remarks>
    /// Budget sized for cold-start: MAUI's <c>App.InitTask</c> + DB seed +
    /// Shell render can easily take 10-15s on the first invocation after an
    /// emulator restart or full force-stop, before the Home tab becomes visible
    /// to UIAutomator2. Each iteration costs up to ~2.2s (two 1s IsDisplayed
    /// probes plus a 200ms settle), so a 20s budget yields ~8-9 sampling
    /// opportunities.
    /// </remarks>
    private static void WaitForAppReadyOrThrow(AppiumDriver driver, string callerName)
    {
        var pollStart = Stopwatch.GetTimestamp();
        var pollBudget = TimeSpan.FromSeconds(20);

        while (Stopwatch.GetElapsedTime(pollStart) < pollBudget)
        {
            if (driver.IsDisplayed("Home", timeoutSeconds: 1) ||
                driver.IsDisplayed("Welcome_Btn_Skip", timeoutSeconds: 1))
            {
                return;
            }
            Thread.Sleep(TestConfig.DelayShortSettle);
        }

        throw new InvalidOperationException(
            $"{callerName}: app did not display Home tab or Welcome popup within 20s of `am start`. " +
            "Common causes: stale `AndroidComponentNames.MainActivity` CRC hash (rebuild required after a " +
            ".NET-for-Android toolchain bump), missing Appium server flag " +
            "`--allow-insecure=uiautomator2:adb_shell`, or a severely under-resourced emulator.");
    }

    /// <summary>
    /// Final readiness gate: after <see cref="DismissOnboardingIfPresent"/> returns,
    /// the Home tab MUST be visible. The dismissal helper sets
    /// <c>OnboardingHandled = true</c> on retry exhaustion even when no popup was
    /// actually dismissed (a documented safety valve to prevent infinite loops in
    /// long-running suites). Without this gate, a still-covered Home leaks downstream
    /// as a confusing <c>WaitForElement</c> timeout on the modal probe.
    /// </summary>
    /// <remarks>
    /// On throw, invalidates <see cref="AppiumSetup.OnboardingHandled"/> back to
    /// <c>false</c> so the NEXT test that calls <see cref="DismissOnboardingIfPresent"/>
    /// directly (e.g. via <c>EnsureOnTab</c>, <c>NavigateToTabRoot</c>,
    /// <c>DarkModeRenderingTests</c>, <c>PrayerListTests</c>) does not short-circuit
    /// via the stale <c>true</c> flag that <see cref="DismissOnboardingIfPresent"/>
    /// set on its safety-valve path — which would mask a real popup blocking that
    /// next test.
    /// </remarks>
    private static void AssertHomeVisibleAfterDismiss(AppiumDriver driver, AppiumSetup setup, string callerName)
    {
        if (!driver.IsDisplayed("Home", timeoutSeconds: 2))
        {
            // Cross-test state hygiene: roll back the cached flag before throwing.
            setup.OnboardingHandled = false;

            throw new InvalidOperationException(
                $"{callerName}: Home tab not visible after `DismissOnboardingIfPresent`. " +
                "An onboarding popup, modal, or stale UI is still covering the Home tab — " +
                $"the `{nameof(DismissOnboardingIfPresent)}` retry-exhaustion safety valve " +
                "(sets OnboardingHandled=true after 3 failed dismiss attempts) may have fired.");
        }
    }

    /// <summary>
    /// Invoke <c>am</c> via <c>mobile: shell</c> and inspect the output for the
    /// failure markers Android prints (<c>Error type N:</c>, <c>Exception occurred</c>).
    /// Raw <c>mobile: shell</c> does NOT throw on a non-zero <c>am</c> exit — it
    /// returns stdout/stderr concatenated as a string. Without this check, a stale
    /// component name or a missing Appium <c>--allow-insecure</c> flag surfaces as
    /// a confusing <c>WaitForAppReadyOrThrow</c> timeout 10s later rather than at
    /// the actual failure site.
    /// </summary>
    private static void RunAmShellOrThrow(AppiumDriver driver, IEnumerable<string> args, string callerName)
    {
        var result = driver.ExecuteScript("mobile: shell", new Dictionary<string, object>
        {
            { "command", "am" },
            { "args", args }
        });

        var output = result?.ToString() ?? string.Empty;
        // SecurityException (one word) is Android's actual stderr marker when the
        // shell uid can't run the requested am operation (e.g., missing Appium
        // `--allow-insecure=uiautomator2:adb_shell` flag). The space-separated
        // "Security exception" variant catches verbose-mode wrappers; both forms
        // are checked to defend against driver-version output drift.
        if (output.Contains("Error type", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Exception occurred", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("SecurityException", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Security exception", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{callerName}: `am` shell command failed. Output:\n{output}\n" +
                "Common causes: stale `AndroidComponentNames.MainActivity` CRC, " +
                "missing Appium server flag `--allow-insecure=uiautomator2:adb_shell`, " +
                "or a package/activity that is not installed or not exported.");
        }
    }
}
