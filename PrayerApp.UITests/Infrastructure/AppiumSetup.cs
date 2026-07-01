using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.Android.Interfaces;
using OpenQA.Selenium.Appium.iOS;
using Xunit;

namespace PrayerApp.UITests.Infrastructure;

/// <summary>
/// Shared xUnit class fixture that manages the Appium driver lifecycle.
/// One driver instance is shared across all tests in a collection.
/// </summary>
public class AppiumSetup : IAsyncLifetime
{
    public AppiumDriver Driver { get; private set; } = null!;

    /// <summary>Whether onboarding has been handled (dismissed or verified) this session.</summary>
    public bool OnboardingHandled { get; set; }

    // ── Per-test session-isolation strategy (issue #164) ─────────────────────────
    //
    // PROBLEM: every test shares ONE Appium driver session for the whole run (see
    // AppiumCollection — "one app launch for the entire test run"). On a long run the
    // single UiAutomator2 / XCUITest session accumulates driver/state drift, and late
    // tests start hitting timeouts that trace to the aged session, not the app.
    //
    // STRATEGY: a bounded session-recreate CADENCE rather than a per-test relaunch.
    // RecycleSessionIfDue() is called once at each test's start (from ResetAppUIState),
    // and every SessionRecreateCadence tests it tears the session down and brings up a
    // fresh one via the existing RecreateDriver path. That caps how much drift any one
    // session can carry to <= SessionRecreateCadence tests, so drift never compounds to
    // the end of the run.
    //
    // WHY NOT a fresh session per test: a Debug MAUI cold start is ~17-23s on the Android
    // emulator (TestConfig.GetAndroidOptions remarks). Recreating per test would add that
    // cost to all ~87 tests (~35 min of pure relaunch). A cadence of 10 recreates only
    // ~8 times across the suite (~3-4 min added) while still bounding drift — the
    // least-cost option that satisfies the isolation requirement.
    //
    // SEED IS NOT RE-RUN: TestDataSeed.SeedAsync runs ONCE in InitializeAsync. Both the
    // Android and iOS options set noReset=true (TestConfig), so a recreated session
    // re-attaches to the same app data — the once-seeded DB survives. Recreation must
    // therefore NEVER re-seed.
    //
    // TUNING: raise SessionRecreateCadence to trade isolation for speed, lower it to
    // trade speed for isolation. This single constant is the only knob.

    /// <summary>Recreate the driver session after this many test entry-points, to bound
    /// the drift a single long-lived Appium session accumulates over a run (#164).</summary>
    public const int SessionRecreateCadence = 10;

    /// <summary>Tests served by the current driver session. Reset to 0 whenever a fresh
    /// session is created (see <see cref="RecreateDriver"/>).</summary>
    private int _testsOnCurrentSession;

    public async Task InitializeAsync()
    {
        // Register the SQLitePCL provider before the first in-process SQLite use
        // below (TestDataSeed.SeedAsync builds the seed DB on the test host).
        // #158 swapped sqlite-net-pcl -> sqlite-net-base, which no longer
        // implicitly registers a provider; the app does this at MauiProgram.cs
        // (Batteries_V2.Init), but the UITest host has no MAUI startup to do it.
        SQLitePCL.Batteries_V2.Init();

        // Seed deterministic baseline data BEFORE Appium launches the app.
        // Terminates the app, pushes a pre-built SQLite file into the app's
        // data dir, so every suite starts with known Collections/Cards/Prayers.
        await TestDataSeed.SeedAsync();

        // Pre-seed OnboardingComplete=true in NSUserDefaults (iOS only) so the
        // welcome popup never appears. Replaces the in-suite DismissOnboardingIfPresent
        // flow, which depended on popup-render timing and was empirically flaky
        // across iOS versions / simulator devices.
        await TestDataSeed.PreSeedOnboardingCompleteAsync();

        CreateDriver();
        // Wait for the app to fully load (splash screen + initial page render).
        // Inline 3000ms — one-off post-driver-create splash settle, not part of
        // the TestConfig.Delay* per-test sweep (#11). Tuning this number is a
        // session-level concern, not a per-action concern.
        await Task.Delay(3000);
    }

    /// <summary>
    /// Per-test session-isolation hook (#164). Called once at each test's START — from
    /// <c>ResetAppUIState</c>, before the test navigates or touches any UI — so a recreate
    /// here can never discard in-progress test state. Every <see cref="SessionRecreateCadence"/>
    /// tests it proactively recreates the driver session via the existing
    /// <see cref="RecreateDriver"/> path, bounding the driver/state drift a single long-lived
    /// session accumulates over a long run. <c>noReset=true</c> (see TestConfig) means the
    /// recreated session re-attaches to the once-seeded app data — the seed is NOT re-run.
    /// </summary>
    public void RecycleSessionIfDue()
    {
        if (_testsOnCurrentSession >= SessionRecreateCadence)
            RecreateDriver(); // resets _testsOnCurrentSession to 0
        _testsOnCurrentSession++;
    }

    /// <summary>
    /// Check if the driver session and app are alive using the documented
    /// mobile: queryAppState API. If the app isn't in the foreground,
    /// try to activate it. If the session is dead, recreate it.
    /// </summary>
    public void EnsureSessionAlive()
    {
        try
        {
            var appId = TestConfig.IsIOS ? TestConfig.IOSBundleId : TestConfig.AndroidPackage;
            var paramName = TestConfig.IsIOS ? "bundleId" : "appId";
            var state = Driver.ExecuteScript("mobile: queryAppState",
                new Dictionary<string, object> { { paramName, appId } });
            int appState = Convert.ToInt32(state);

            if (appState < 3) // not running or background-suspended
            {
                Driver.ActivateApp(appId);
                Thread.Sleep(TestConfig.DelayLongSettle);
            }
        }
        catch (WebDriverException)
        {
            RecreateDriver();
        }
    }

    /// <summary>Tear down the current driver and create a fresh session with retry.</summary>
    private void RecreateDriver()
    {
        // A fresh session carries no accumulated drift — restart the cadence counter so
        // the next recreate is SessionRecreateCadence tests away, whether this recreate
        // was the proactive cadence (RecycleSessionIfDue) or dead-session recovery
        // (EnsureSessionAlive).
        _testsOnCurrentSession = 0;

        try { Driver.Quit(); } catch { }
        try { Driver.Dispose(); } catch { }

        // Android-only instrumentation recovery (#191/#192). On a long run the shared
        // UiAutomator2 instrumentation accumulates state and eventually wedges
        // ("instrumentation process is not running"), crashing the in-flight test and the
        // one after it. Uninstalling Appium's server packages now — after the old session is
        // torn down, before the fresh CreateDriver below — makes that CreateDriver redeploy
        // clean instrumentation. This rides the existing #164 recreate cadence (it only ever
        // runs on the RecreateDriver path, NEVER on the first InitializeAsync CreateDriver,
        // so it adds no cost to a normal session start) and reuses no second counter.
        // Best-effort and no-op off Android — RecoverAndroidInstrumentationAsync self-guards
        // on TestConfig.IsAndroid and swallows its own failures, so it can never abort the
        // recreate. Blocking is consistent with this method's synchronous lifecycle.
        TestDataSeed.RecoverAndroidInstrumentationAsync().GetAwaiter().GetResult();

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            CreateDriver();
            Thread.Sleep(TestConfig.DelayAppRelaunch);
            OnboardingHandled = false;

            try
            {
                Driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
                _ = Driver.PageSource;
                return;
            }
            catch
            {
                try { Driver.Quit(); } catch { }
                try { Driver.Dispose(); } catch { }
            }
        }
        // All 3 attempts failed — driver may be unusable; next test will likely throw
    }

    private void CreateDriver()
    {
        var options = TestConfig.GetOptions();

        if (TestConfig.IsAndroid)
        {
            var androidDriver = new AndroidDriver(TestConfig.AppiumServerUri, options, TestConfig.SessionTimeout);
            Driver = androidDriver;
            Driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
            ApplyAndroidTreeReadSettings(androidDriver);
            return;
        }

        Driver = new IOSDriver(TestConfig.AppiumServerUri, options, TestConfig.SessionTimeout);
        Driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
    }

    /// <summary>
    /// Issue #117: make the shared UiAutomator2 session's accessibility-tree read
    /// reliable on the Cards page right after an in-place tap-to-expand. Without
    /// these, probing chips on the transient/non-idle post-expand window
    /// intermittently returns a blank tree (root <c>displayed=false</c>, 0 children),
    /// even though <c>adb shell uiautomator dump</c> and a FRESH Appium session both
    /// read a full ~112 KB tree at the same instant — so the defect is the shared
    /// session's read, not the app's realization.
    /// <para>
    /// Applied here (not as a capability) so the single code path covers the initial
    /// session AND every <see cref="RecreateDriver"/> retry. Android-only: these are
    /// UiAutomator2 settings; the iOS XCUITest driver ignores them.
    /// </para>
    /// <para>
    /// Sourced from the appium-uiautomator2-driver Settings API
    /// (github.com/appium/appium-uiautomator2-driver — "Settings"):
    /// <list type="bullet">
    /// <item><c>enableMultiWindows=true</c> — "include all windows that the user can
    /// interact with … while building the XML page source". UiAutomator2 defaults to
    /// only the single active window; mid-animation that active window can be a
    /// transient one whose root reads <c>displayed=false</c> (the blank tree).
    /// Aggregating all windows pulls the real app window into the source so the chips
    /// are present once on screen.</item>
    /// <item><c>waitForIdleTimeout=100</c> (default 10000 ms) — the per-lookup wait for
    /// the UI to go idle. A MAUI CollectionView mid-expand is never fully idle, so the
    /// default makes each lookup block ~10 s then read a stale/transient frame. The doc
    /// says: "Consider lowering the value of this setting if you experience long delays
    /// while interacting with accessibility elements." 100 ms keeps lookups responsive.</item>
    /// </list>
    /// Deliberately NOT set: <c>allowInvisibleElements</c> (would let IsDisplayed match
    /// <c>displayed=false</c> chips → false greens) and <c>ignoreUnimportantViews</c>
    /// (hierarchy compression can drop the very chip nodes under assertion).
    /// </para>
    /// </summary>
    private static void ApplyAndroidTreeReadSettings(AndroidDriver driver)
    {
        try
        {
            // Settings setter issues the documented Update Settings command
            // (POST /session/:id/appium/settings), using the EXACT UiAutomator2
            // Setting names from the driver docs. Accessed through the IHasSettings
            // interface (AndroidDriver implements it). Avoids the typed Configurator*
            // convenience methods, whose key (the UiAutomator Configurator's idle
            // timeout) differs from the driver Setting documented in the Settings API.
            ((IHasSettings)driver).Settings = new Dictionary<string, object>
            {
                ["enableMultiWindows"] = true,
                ["waitForIdleTimeout"] = 100,
            };
        }
        catch (WebDriverException)
        {
            // Best-effort: an older UiAutomator2 build that rejects a key shouldn't
            // abort session creation. The tests still fall back to their settle +
            // controlled-scroll path; they just lose the tree-read hardening.
        }
    }

    public async Task DisposeAsync()
    {
        if (Driver != null)
        {
            Driver.Quit();
            Driver.Dispose();
        }
        await Task.CompletedTask;
    }
}
