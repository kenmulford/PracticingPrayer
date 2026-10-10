---
name: prayer-app-ui-testing
description: >
  Use when writing, debugging, or reviewing Appium UI tests in PrayerApp.UITests/.
  Covers: infrastructure setup, platform detection, AutomationId locators, every
  helper in AppExtensions.cs, delay constants, iOS gotchas, TestDataSeed workflow,
  test traits, SkippableFact patterns, and operational "never do X" rules that
  prevent cascade failures. Invoke before touching any UITest file.
---

# PrayerApp UI Testing

UI test project: `PrayerApp.UITests/` using Appium.WebDriver 8.1.0 with xUnit 2.9.2.
24 test files; shared Appium collection fixture with a single driver across the session.

---

## When to Use

Invoke this skill before:
- Writing a new UI test or helper
- Debugging a failing test (platform-specific race, stale element, alert trap)
- Changing `TestConfig.cs` or `AppExtensions.cs`
- Seeding or replacing the Android/iOS test DB
- Running the full UITest suite for the first time on a machine

---

## Infrastructure Files

| File | Purpose |
|------|---------|
| `Infrastructure/AppiumSetup.cs` | `IAsyncLifetime` fixture — shared driver lifecycle, retry, health check |
| `Infrastructure/TestConfig.cs` | Platform detection, timeouts, delay constants, device config |
| `Infrastructure/AppiumCollection.cs` | Collection fixture definition |
| `Infrastructure/TestDataSeed.cs` | Seed DB builder + adb/simctl push workflow |
| `Infrastructure/SettingsShim.cs` | Shim for settings access in test context |
| `Helpers/AppExtensions.cs` | All ~22 element, tap, scroll, navigation, and diagnostic helpers |

---

## Platform Detection

```csharp
// TestConfig.cs
public static readonly bool IsAndroid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
public static readonly bool IsIOS    = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
```

Run Android tests on Windows, iOS tests on macOS. Same binary, platform-guarded helpers.

---

## AppiumSetup (IAsyncLifetime)

Shared driver instance across all tests in the `"Appium"` collection:
- 3 000 ms initial wait for splash screen + UI render
- Session health check via `mobile: queryAppState`
- Automatic driver recreation with 3 retry attempts
- State tracking: `OnboardingHandled` boolean

```csharp
[CollectionDefinition("Appium")]
public class AppiumCollection : ICollectionFixture<AppiumSetup> { }
```

---

## Test Config Constants

### Timeouts

| Constant | Value | Usage |
|----------|-------|-------|
| `DefaultTimeout` | **10 s** | Standard element wait (base; see note) |
| `ShortTimeout` | 3 s | Quick presence checks |
| `SessionTimeout` | 60 s | Driver session |
| `IOSSessionTimeout` | 240 s | iOS driver session |

> `WaitForElement` and `WaitAndTap` default to **15 s** (`timeoutSeconds: 15`), not `DefaultTimeout`.
> `DefaultTimeout` (10 s) is the implicit-wait fallback, not the polling-wait default.

### Named Delays (milliseconds)

| Constant | Value | Usage |
|----------|-------|-------|
| `DelayAfterTap` | 300 | After tapping any element |
| `DelayAfterSave` | 1 000 | After save / navigation from save |
| `DelayAfterNavigation` | 500 | Shell tab / page transition |
| `DelayAfterDismiss` | 300 | Alert / modal dismiss animation |
| `DelayDirtyRegistration` | 500 | After editing, before dirty-state check |
| `DelayAppRelaunch` | 5 000 | After full `RecreateDriver` |
| `DelayCollectionRender` | 1 500 | CollectionView item materialisation |
| `DelayModalAnimation` | 1 000 | Modal / action-sheet present or dismiss |

---

## Device Configuration

### Android

```
Package:     com.multithreadedllc.prayercards
Automation:  UiAutomator2
Activity:    crc6425c6d21f3599989c.MainActivity
Device:      env ANDROID_AVD ?? "pixel_9_-_api_36_0"
autoGrantPermissions: true
noReset:     true (when APK not provided)
```

### iOS

```
BundleId:              com.multithreadedllc.prayercards
Automation:            XCUITest
Device:                env IOS_SIMULATOR ?? "Test iPad"   (an iPad (A16))
PlatformVersion:       env IOS_VERSION ?? "27.0"
Udid:                  TestConfig.IOSUdid (env IOS_UDID, else resolved from Device + PlatformVersion)
connectHardwareKeyboard: true
noReset:               true
```

**Why iPad?** iPhone has no keyboard dismiss button; `DismissKeyboardIfPresent` fails there, causing cascade failures on text-input tests.

**Why `connectHardwareKeyboard`?** Hardware keyboard hides the software keyboard, preventing `SendKeys` from hitting dictation/emoji intercepts. Only works when Appium boots the simulator.

---

## AutomationId Locators

### Android — XPath with Package Prefix

```csharp
By.XPath($"//*[@resource-id='com.multithreadedllc.prayercards:id/{id}' or @content-desc='{id}']")
```

MAUI maps `AutomationId` to `resource-id` on interactive elements and `content-desc` on layout containers; the XPath covers both.

### iOS — Accessibility ID

```csharp
MobileBy.AccessibilityId(automationId)
```

### Naming Convention

`{Page}_{Type}_{Name}` — e.g. `Home_Btn_QuickAdd`, `Cards_Entry_Search`, `Cards_Section_Header`.

---

## Quick Reference: All Helpers (AppExtensions.cs)

| Helper | Signature | Notes |
|--------|-----------|-------|
| `WaitForElement` | `(id, timeoutSeconds=15)` | Polls; default 15 s |
| `WaitForElementGone` | `(id, timeoutSeconds=10)` | Wait for disappear |
| `FindByAutomationId` | `(id)` | Throws if absent |
| `Tap` | `(id)` | FindBy then Click |
| `WaitAndTap` | `(id, timeoutSeconds=15)` | Most common action |
| `EnterText` | `(id, text)` | Platform-aware clear+type |
| `GetText` | `(id)` | Returns `.Text` |
| `IsDisplayed` | `(id, timeoutSeconds=3)` | Non-throwing; returns bool |
| `DismissKeyboardIfPresent` | `()` | iOS-only; no-op on Android |
| `ScrollDownTo` | `(id, maxScrolls=5, scrollableId?)` | Scroll by AutomationId |
| `ScrollDownToText` | `(text, maxScrolls=5, scrollableId?)` | Scroll by visible text |
| `EnsureCardVisible` | `(cardName)` | 3-stage: visible → scroll → expand-all + search fallback |
| `EnsureAllSectionsExpanded` | `()` | Fixed-point loop, re-finds headers per iteration |
| `NavigateToTab` | `(tabTitle)` | 5-stage escalation (see below) |
| `EnsureOnTab` | `(tabTitle, setup)` | Session check + dismiss onboarding + NavigateToTab |
| `TapToolbarItemById` | `(id, timeoutSeconds=10)` | 3-stage: direct → overflow popup → iOS Secondary menu |
| `TapToolbarItem` | `(text, timeoutSeconds=10)` | Text-only toolbars; prefer `ById` for icon items |
| `TapIOSActionSheetButton` | `(name, timeoutSeconds=10)` | iPad popover-safe; reads element center at call time |
| `TapAlertButton` | `(buttonText)` | Platform-aware alert button |
| `DismissAlertIfPresent` | `()` | iOS: tries Discard first; Android: positive button |
| `IsAlertPresent` | `()` | Non-throwing bool |
| `ResetAppUIState` | `(setup)` | Clear search, exit multi-select, back to tab root |
| `CaptureDiagnostics` | `(reason)` | Screenshot + page source to temp dir; never throws |
| `EnsureUITestCardExists` | `(setup)` | Navigates to Cards tab; trusts seed DB |
| `EnsureUITestPrayerExists` | `(setup)` | Navigates to Prayers tab; trusts seed DB |
| `EnsureUITestTagExists` | `(setup)` | Creates "UITest Tag" if missing |
| `EnsureUITestCollectionExists` | `(setup)` | Creates "UITest Collection" if missing |
| `FindCardCell` | `(cardName, timeoutSeconds=10)` | iOS: returns parent XCUIElementTypeCell for reliable swipes |
| `LongPress` | `(element)` | 750 ms; enters multi-select |
| `SwipeElementLeft` | `(element)` | Reveal right swipe actions (Delete) |
| `SwipeElementRight` | `(element)` | Reveal left swipe actions (Favorite/Edit) |
| `IOSScrollToPredicateInContainer` | `(containerId, predicate, maxAttempts=3)` | `mobile: scroll` with `predicateString` |
| `IOSScrollToNameInContainer` | `(containerId, name, maxAttempts=3)` | `mobile: scroll` with `name` parameter |
| `GetAccessibleDescription` | `(id)` | content-desc (Android) or label (iOS); Secondary-menu fallback |
| `GetAccessibleHint` | `(id)` | hint (Android) or value (iOS) |
| `GoBack` | `()` | `Navigate().Back()` |

---

## Key Helper Details

### `EnsureAllSectionsExpanded`

Fixed-point loop up to `MaxIterations=20`. Each iteration re-finds all `Cards_Section_Header` elements and infers collapsed state from the vertical gap to the next header (`< 100 px` = collapsed). Acts on the first collapsed header, then re-evaluates from scratch.

**Why re-find per iteration?** Clicking to expand reflows the CollectionView. Caching the header list upfront causes `StaleElementReferenceException` on subsequent Location/Size reads.

### `EnsureCardVisible`

Three-stage fallback:
1. **Visible check** — if already on screen, return.
2. **Scroll** — `ScrollDownToText` with `Cards_List_Cards` as container.
3. **Expand-all + retry** — calls `EnsureAllSectionsExpanded`, retries scroll.
4. **Search-bar fallback (TD-19)** — types the card name into `Cards_Search` to force MAUI to materialize the row. `ResetAppUIState` clears this at the next test start.

### `TapIOSActionSheetButton`

Reads `element.Location` and `element.Size` **at call time**, then fires `mobile: tap` with the center coordinates. This avoids stale-coordinate drift that occurs when iPad popover animation is still in progress when WebDriver's `Click()` fires. **Always add `Thread.Sleep(TestConfig.DelayModalAnimation)` before calling** if the action sheet was just presented (see `PrayerTime_TagScoped` — `PrayerTimeTests.cs:280`).

### `NavigateToTab` — 5-Stage Escalation

1. Back up to 3 times, dismissing alerts, trying `TryTapTab` after each.
2. Dismiss known modal buttons (`Welcome_Btn_Skip`, `Scope_Btn_Cancel`, etc.).
3. Escape Prayer Time page (tab bar is hidden there — taps "I'm done" / "Finish").
4. Re-activate the app via `ActivateApp`.
5. Final XPath text fallback.

### `TapToolbarItemById` — 3-Stage Escalation

1. Direct `AutomationId` lookup.
2. Open overflow popup (`More` button), tap item inside.
3. iOS only: open `SecondaryToolbarMenuButton` UIMenu, tap by name/label.

---

## BUG-74: Timestamped Titles

When creating cards/prayers in tests, use timestamped titles to avoid SQLite UNIQUE-constraint violations that trigger duplicate-title alerts and trap the test:

```csharp
var title = $"Race Regression {DateTime.Now:HHmmss}";
```

Pattern from `PrayerCardTests.cs:125`. The alert would otherwise cascade into `DismissAlertIfPresent`'s "Discard" path, leaving the form open.

---

## SkippableFact Pattern

Use `[SkippableFact]` + `throw new SkipException(...)` for tests that depend on platform or app state that may not be available:

```csharp
[SkippableFact]
public void PrayerTime_SessionStarts_ShowsCarousel()
{
    if (!TryStartPrayerTime())
        throw new SkipException("Could not start Prayer Time session");
    // ...
}

// Android-only guard:
[SkippableFact]
public void HardwareBack_DirtyDetail_ShowsDiscardDialog()
{
    if (TestConfig.IsIOS)
        throw new SkipException("Android-only: hardware back button does not exist on iOS");
    // ...
}
```

Skipped tests do not count as failures. Required for `PrayerTimeTests.cs` and `AndroidTests.cs`.

---

## Test Structure

```csharp
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "3-Cards")]
public class PrayerCardTests
{
    private readonly AppiumSetup _setup;
    public PrayerCardTests(AppiumSetup setup) => _setup = setup;

    [Fact]
    public void Cards_CreateCard_IsVisible()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);

        var title = $"My Card {DateTime.Now:HHmmss}"; // BUG-74: timestamped
        _setup.Driver.TapToolbarItemById("Add Card");
        _setup.Driver.WaitForElement("Card_Entry_Title");
        _setup.Driver.EnterText("Card_Entry_Title", title);
        _setup.Driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        _setup.Driver.EnsureCardVisible(title);
        Assert.True(_setup.Driver.IsTextDisplayed(title, timeoutSeconds: 5));
    }
}
```

### Test Traits

| Trait | Values | Purpose |
|-------|--------|---------|
| `Platform` | `"CrossPlatform"`, `"Android"`, `"iOS"` | Filter by platform |
| `Section` | `"1-Onboarding"`, `"2-Home"`, `"3-Cards"`, etc. | UAT section grouping |

### Running Tests

```bash
# Android (Windows)
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"

# iOS (macOS)
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=iOS"
```

For long suites (>5 min / >200 lines of output): hand Ken the one-liner and poll the log file rather than running it yourself. The full suite takes ~20 min on a warm simulator.

---

## TestDataSeed — Android DB Seeding Workflow

`TestDataSeed.cs` builds a seed SQLite DB and pushes it to the device/simulator before the test session starts.

### Android: `adb push` + `run-as cp`

```bash
# Sequence executed by PushSeedToDeviceAsync():
adb shell am force-stop com.multithreadedllc.prayercards
adb shell rm -f /data/local/tmp/prayer_app_seed.db
adb push <localSeedPath> /data/local/tmp/prayer_app_seed.db
adb shell run-as com.multithreadedllc.prayercards cp /data/local/tmp/prayer_app_seed.db files/prayer_app.db
adb shell run-as com.multithreadedllc.prayercards rm -f files/prayer_app.db-wal files/prayer_app.db-shm
adb shell rm -f /data/local/tmp/prayer_app_seed.db
```

**Why this sequence?**
- The app data dir is restricted to the app UID; `adb push` directly into it fails.
- `/data/local/tmp` is world-writable; `run-as cp` then moves it with the correct UID.
- WAL/SHM sidecars must be removed; if they linger, SQLite replays them on top of the fresh seed, corrupting the baseline.

### iOS: `xcrun simctl` + `File.Copy`

Simulator containers are host-filesystem-accessible. `PushSeedToSimulatorAsync` resolves the container path via `simctl get_app_container`, terminates the app, and copies the seed directly.

---

## Common Mistakes

### NEVER set `autoDismissAlerts: true` on iOS

`autoDismissAlerts` auto-taps **Cancel** on every alert. The "Unsaved Changes → Discard/Cancel" dialog uses Cancel as the **reject** action — enabling `autoDismissAlerts` taps Cancel, keeps the form dirty, and traps every `NavigateToTab` back-out loop permanently. Tests dismiss alerts explicitly via `DismissAlertIfPresent()`, which tries "Discard" first.

**Source:** `TestConfig.cs:109` comment — this is intentional and must not be reverted.

### NEVER open Appium Inspector during a UITest run

Opening a new Inspector session kills the UiAutomator2/XCUITest instrumentation. All remaining tests fail with "cannot be proxied ... instrumentation process is not running". Close the Inspector session before starting any test run.

### NEVER `adb uninstall` a MAUI Debug APK

MAUI Fast Deployment stores override assemblies on-device **separately** from the APK. Uninstalling the APK wipes those assemblies. Reinstalling the APK without them causes a `SIGABRT` on launch. Recovery: use `dotnet build -t:Install` instead of uninstall+reinstall.

### NEVER `adb pull` a live DB

Pulling the SQLite DB while the app is running risks pulling a partial/corrupt WAL state. Force-stop the app first: `adb shell am force-stop com.multithreadedllc.prayercards`.

### Do NOT cache header elements before `EnsureAllSectionsExpanded` clicks

CollectionView reflows after each section-expand, invalidating all previously retrieved element references. Reading `Location` or `Size` on a stale reference throws `StaleElementReferenceException`. Always re-find headers at the top of each iteration (the fixed-point pattern).

### Do NOT use `WaitForElement`/`WaitAndTap` with the `DefaultTimeout` mental model

`WaitForElement(id)` defaults to **15 s**, not 10 s. `DefaultTimeout` (10 s) is the implicit-wait setting, not the `WebDriverWait` timeout. Pass an explicit `timeoutSeconds` argument when you need a different budget.

### Do NOT skip `Thread.Sleep(DelayModalAnimation)` before `TapIOSActionSheetButton`

On iPad, the action sheet / popover animates in. Tapping before animation completes sends coordinates that land on the wrong button. The `PrayerTime_TagScoped` test (`PrayerTimeTests.cs:280`) demonstrates the required pattern: `Thread.Sleep(TestConfig.DelayModalAnimation)` immediately before `TapIOSActionSheetButton(...)`.

### NEVER seed the DB with a Release build — `run-as` requires a debuggable APK

The Android seed copies the prepared DB via `adb shell run-as <pkg> cp /data/local/tmp/…db files/…db`. `run-as` only works on a **debuggable** app, so a Release build fails this step with `run-as … cp … failed (exit 1)` before any test runs. **`run-uitests.ps1` now defaults to a Debug build** (PR #116 — it previously built `-c Release`, which broke the seed); it builds+installs via the MAUI `-t:Install` target and accepts `-Configuration Debug|Release`. Leave it on the default (Debug) for seeding to work. For a manual run, build/install `-c Debug` (standalone `adb install` of a Debug build may need `-p:EmbedAssembliesIntoApk=true`), then `dotnet test PrayerApp.UITests --filter "FullyQualifiedName~<TestClass>"`.

### Do NOT locate a bare `Label` by AutomationId — locate text by visible text

On Android, MAUI maps `AutomationId` to `resource-id` only for interactive controls and containers; a bare `<Label>` gets `content-desc` instead. If that Label also sets `SemanticProperties.Description`, the Description **overwrites** `content-desc`, so the locator's `resource-id OR content-desc='<id>'` (`AppExtensions.cs:43-44`) matches neither and the element is "not found." Locate text labels by their visible text — `IsTextContainsDisplayed(…)` / `FindByTextContains(…)` (Android `@text` is always populated) — or move the AutomationId onto a wrapping `Border`.

---

## CSO Keywords

Appium, UITest, AppExtensions, TestConfig, autoDismissAlerts, AutomationId, StaleElementReference, run-as, adb push, EnsureAllSectionsExpanded, EnsureCardVisible, TapIOSActionSheetButton, TapToolbarItemById, SkippableFact, TestDataSeed, NavigateToTab, ResetAppUIState, CaptureDiagnostics
