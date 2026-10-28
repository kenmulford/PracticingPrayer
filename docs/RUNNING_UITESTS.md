# Running the Appium UI Test Suite

> 23 Appium tests across 13 test classes, plus 2 host-side `TestConfig` unit-test classes (`Category=Unit`, 17 cases) that need no device. Each `dotnet test` run targets **one** platform. By default the host OS chooses it (**Windows runs Android, macOS runs iOS**), or set the `UITEST_PLATFORM` environment variable (`android` | `ios`) to choose explicitly, which lets **macOS run the Android suite** too. Each run drives one Appium session, so two platforms run at once only against two Appium servers on different ports (see [Mac (Android)](#mac-android--terminal)).

---

## Prerequisites

### Both Platforms

| Tool | Version | Install |
|------|---------|---------|
| .NET SDK | 10.0.401 (`rollForward: latestPatch`) + workload set 10.0.402, both pinned in `global.json` | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Node.js | 20.19+, 22.12+, or 24+ | [nodejs.org](https://nodejs.org) (Appium 3's `engines` range) |
| Appium | 3.x | `npm install -g appium` |

### PC (Android)

| Tool | Version | Install |
|------|---------|---------|
| Android SDK | API 36+ | Via Android Studio or `sdkmanager` |
| Android Emulator | — | Create an AVD (default: `pixel_9_-_api_36_0`) |
| Appium UiAutomator2 driver | latest | `appium driver install uiautomator2` |
| Java JDK | 17+ | Required by UiAutomator2 |

**Environment variables** (set permanently via PowerShell — run as Administrator):
```powershell
[System.Environment]::SetEnvironmentVariable("ANDROID_HOME", "$env:LOCALAPPDATA\Android\Sdk", "User")
[System.Environment]::SetEnvironmentVariable("JAVA_HOME", "C:\Program Files\Java\jdk-17", "User")

# Add platform-tools (adb) and emulator to PATH
$sdkPath = "$env:LOCALAPPDATA\Android\Sdk"
$currentPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
[System.Environment]::SetEnvironmentVariable("Path", "$currentPath;$sdkPath\platform-tools;$sdkPath\emulator", "User")
```
Restart your terminal after setting these.

### Mac (iOS)

| Tool | Version | Install |
|------|---------|---------|
| Xcode | 27.0 | Mac App Store |
| Xcode Command Line Tools | — | `xcode-select --install` |
| iOS Simulator | — | Default: `Test iPad` (an iPad (A16)) on iOS 27.0. See "Simulators" in `CLAUDE.md` |
| Appium XCUITest driver | latest | `appium driver install xcuitest` |

---

## Setup

### 1. Install Appium and drivers

**PC (PowerShell):**
```powershell
npm install -g appium
appium driver install uiautomator2
appium driver list --installed
```

**Mac (Terminal):**
```bash
npm install -g appium
appium driver install xcuitest
appium driver list --installed
```

### 2. Prepare the emulator / simulator

**PC (Android — PowerShell):**
```powershell
# List available AVDs
& "$env:ANDROID_HOME\emulator\emulator.exe" -list-avds

# Launch emulator (use the name from the list above)
& "$env:ANDROID_HOME\emulator\emulator.exe" -avd pixel_9_-_api_36_0
```

If your AVD has a different name, set the `ANDROID_AVD` environment variable (see [Environment Variables](#environment-variables)).

**Mac (iOS):**
```bash
# List available simulators
xcrun simctl list devices available

# Boot the Test iPad by name
xcrun simctl boot "Test iPad"
open -a Simulator
```

### 3. Install the app on the device

**PC — Option A: pre-install via adb** (recommended, PowerShell):
```powershell
# Build the APK
dotnet build PrayerApp/PrayerApp.csproj -f net10.0-android -c Debug

# Install on the running emulator
adb install -r PrayerApp\bin\Debug\net10.0-android\com.multithreadedllc.prayercards-Signed.apk
```

**PC — Option B: let Appium install it** (set the APK path, PowerShell):
```powershell
$env:PRAYER_APK_PATH = "C:\path\to\com.multithreadedllc.prayercards-Signed.apk"
```

**Mac (iOS):**
```bash
# Build for iOS Simulator
# NOTE: -r iossimulator-arm64 is REQUIRED on iOS-simulator Debug builds. It selects the
# simulator slice of the App-Shortcuts native lib (libHostShortcuts.a); without it the
# native linker picks the device (iphoneos) slice and the build fails at `ld` (issue #150).
dotnet build PrayerApp/PrayerApp.csproj -f net10.0-ios -c Debug \
  -r iossimulator-arm64 -p:_DeviceType=Simulator

# Install on the Test iPad
xcrun simctl install "Test iPad" PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app
```

### 4. Start the Appium server

In a **separate terminal** (leave it running for the entire test session).

**Android (PC or Mac):** the `adb_shell` feature is required because helpers such as `ForceStopApp` call `mobile: shell`. Appium 3 accepts it only driver-scoped:
```bash
appium --allow-insecure=uiautomator2:adb_shell
```

**iOS (Mac):**
```bash
appium
```

You should see:
```
[Appium] Appium REST http interface listener started on 0.0.0.0:4723
```

The test suite connects to `http://127.0.0.1:4723` by default.

---

## Running Tests

### PC (Android — PowerShell)

```powershell
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"
```

### Mac (iOS — Terminal)

```bash
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform"
```

There are no iOS-only tests. Android-only tests are `SkippableFact`s that skip on iOS, so an unfiltered run also works; the filter only excludes the `Category=Unit` tests.

### Mac (Android — Terminal)

The Mac can run the Android suite too. Set `UITEST_PLATFORM=android` so the harness builds an `AndroidDriver` against the local emulator instead of the iOS default. On this Mac the AVD is `pp_api36` and the SDK lives in a non-default location; follow the `prayer-app-e2e-run` skill (`.claude/skills/prayer-app-e2e-run/`, script `scripts/run-e2e-mac.sh`) to boot, deploy, and seed, then:

```bash
UITEST_PLATFORM=android ANDROID_AVD=pp_api36 dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"
```

Run only **one** platform per `dotnet test` invocation. With one Appium server, run the iOS and Android commands one after the other. To run both in parallel, start a second Appium server (`appium --port 4725` for iOS), point each run at its own server with `APPIUM_SERVER_URL`, build the test project once, and pass `--no-build` to both runs; the `prayer-app-e2e-run` skill scripts this.

### Run a single test section

Tests carry a `Section` trait:

| Section | Tests |
|---------|-------|
| `2-Home` | Active Cards metric navigates to Cards tab; Quick Add prayer appears on Cards tab |
| `3-PrayerCards` | Create card, delete expanded card, collection picker close, card Save + |
| `4-Prayers` | Add prayer; one-time reminder date in read view |
| `5-UnsavedChanges` | Edited title then back shows the discard dialog |
| `8-Collections` | Collection section headers, Manage Collections navigation, create collection, protect-all-cards radios |
| `8-PrayerTime` | Session carousel, tag scope page |
| `9-Settings` | App Settings row touch target (Android), dark-mode switch thumb (Android, pixel sampling needs Windows) |
| `12-EdgeCases` | Rapid tab switching |
| `14-Android` | Hardware back from a clean Settings sub-page, context-menu import, import skips duplicates |
| `15-Accessibility` | Decorative card elements excluded from the accessibility tree |

`DarkModeRenderingTests` also carries `Host=Windows` so a filter can exclude it off Windows.

```powershell
# Run only Home tests
dotnet test PrayerApp.UITests/ --filter "Section=2-Home"

# Run a single test by name
dotnet test PrayerApp.UITests/ --filter "FullyQualifiedName~QuickAdd_PrayerAppearsOnCardsTab"
```

### Rerunning failed tests

**See which tests failed (detailed output):**

PC (PowerShell):
```powershell
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android" -v detailed 2>&1 |
  Select-String "Failed|Passed|Skipped"
```

Mac (Terminal):
```bash
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform" -v detailed 2>&1 |
  grep -E "Failed|Passed|Skipped"
```

**Rerun a specific failed test by name:**
```powershell
dotnet test PrayerApp.UITests/ --filter "FullyQualifiedName~Cards_SaveAndNew"
```

**Rerun all tests in a failed section:**
```powershell
dotnet test PrayerApp.UITests/ --filter "Section=3-PrayerCards"
```

**Generate a structured results file** (opens in VS / VS Code with TRX Viewer extension):
```powershell
dotnet test PrayerApp.UITests/ --logger "trx;LogFileName=results.trx"
```

The `.trx` file is written to `PrayerApp.UITests/TestResults/results.trx` and shows a tree view of every test with pass/fail status, duration, and failure messages.

### Important: test isolation

Tests in the `"Appium"` collection share one driver session and one SQLite database, seeded once by `TestDataSeed.SeedAsync` before the driver starts (`noReset=true`, never re-seeded). On iOS, `TestDataSeed.PreSeedOnboardingCompleteAsync` marks onboarding complete so the welcome popup never appears. Order does not matter, provided every test follows two rules:

1. A test that needs pre-existing data seeds its own uniquely named fixture (names live in `TestSeedFixtures`) and touches only that data.
2. Every test starts on Home: `ResetAppUIState` and `EnsureOnTab` run at test start.

A test that passes alone but fails in the suite breaks one of these rules; fix the rule, not the timeout.

---

## Environment Variables

All optional. Set before running `dotnet test`.

| Variable | Default | Description |
|----------|---------|-------------|
| `UITEST_PLATFORM` | _(host OS default)_ | Force the target platform: `android` or `ios`. Unset → Windows runs Android, macOS runs iOS. Set `android` to run the Android suite from macOS. An unrecognized value runs nothing (the harness throws `PlatformNotSupportedException`). |
| `APPIUM_SERVER_URL` | `http://127.0.0.1:4723` | Appium server endpoint |
| `ANDROID_AVD` | `pixel_9_-_api_36_0` | Android emulator AVD name |
| `PRAYER_APK_PATH` | _(pre-installed)_ | Path to APK; if set, Appium installs it |
| `IOS_SIMULATOR` | `Test iPad` | iOS simulator device name |
| `IOS_VERSION` | `27.0` | iOS platform version string |
| `IOS_UDID` | _(resolved)_ | Optional override: UDID of the target simulator. Unset, the harness uses the one available simulator named `IOS_SIMULATOR` on iOS `IOS_VERSION` |

**PC (PowerShell — session-only):**
```powershell
$env:ANDROID_AVD = "pp_api36"
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"
```

To set permanently on PC:
```powershell
[System.Environment]::SetEnvironmentVariable("ANDROID_AVD", "pp_api36", "User")
```

**Mac (Terminal — session-only):**
```bash
export IOS_SIMULATOR="Test iPad"
export IOS_VERSION="27.0"
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform"
```

To set permanently on Mac, add to `~/.zshrc`:
```bash
echo 'export IOS_SIMULATOR="Test iPad"' >> ~/.zshrc
echo 'export IOS_VERSION="27.0"' >> ~/.zshrc
source ~/.zshrc
```

> **Note on iOS device choice:** iPad simulators are preferred because they have a keyboard dismiss button. iPhone simulators lack this, which makes text input unreliable in Appium.

---

## Debugging & Error Logging

### Page source dumps

When a test fails or needs diagnostics, it can dump the full accessibility tree:

```csharp
driver.DumpPageSource("MyTest_FailureContext");
```

Dumps are saved as XML files to:
```
PrayerApp.UITests/bin/Debug/net10.0/diagnostics/
```

Each file is named `{testName}_{timestamp}.xml` and contains the platform's accessibility tree — all visible elements, their AutomationIds, text, and hierarchy. Open in any text editor to inspect element structure.

`PrayerTimeTests` and the `ScrollToPrayerAndTap` helper call `DumpPageSource` on failure paths.

### Appium server logs

The Appium server terminal shows every command the test suite sends (element finds, taps, text input). When a test times out or can't find an element, the server log shows exactly which locator strategy was tried and what the response was.

For verbose output:
```bash
appium --log-level debug
```

Or save to a file:
```bash
appium --log appium.log
```

### xUnit test output

Run with detailed verbosity to see per-test pass/fail timing:
```bash
dotnet test PrayerApp.UITests/ -v detailed
```

Or generate a results file:
```bash
dotnet test PrayerApp.UITests/ --logger "trx;LogFileName=results.trx"
```

The `.trx` file can be opened in Visual Studio or VS Code (with the TRX Viewer extension) for structured failure details.

### In-app diagnostic log

The app itself maintains an append-only diagnostic log (`IDiagnosticLog` / `DiagnosticLog`) that captures fire-and-forget async errors and crash context. Access it via **Settings > About > Send Diagnostic Info** which opens the OS share sheet with the log file (the button is hidden when no log file exists yet). Useful when a test triggers a crash or unexpected behavior that isn't visible in the Appium session.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Could not create session` | Emulator/simulator not running, or Appium server not started |
| `An element could not be located` | Check AutomationId matches XAML; run `driver.DumpPageSource()` to inspect the tree |
| `Original error: Couldn't find app` | App not installed; use `adb install` (Android) or `xcrun simctl install` (iOS) |
| `Session timed out` | App may have crashed; check Appium logs and the in-app diagnostic log |
| Tests pass individually but fail together | Shared driver state; earlier test may have left app on unexpected page. Check `EnsureOnTab` calls |
| iOS text input fails | Use the `Test iPad` simulator; `DismissKeyboardIfPresent()` fails on iPhone because its keyboard has no dismiss button |
| Android `resource-id` not found | AutomationId maps to `content-desc` on some elements. The helpers try both strategies automatically |
| `adb: command not found` | Run the environment variable setup in [Prerequisites](#pc-android) to add `platform-tools` to PATH |
| `emulator: command not found` | The emulator isn't on PATH by default — use the full path: `& "$env:ANDROID_HOME\emulator\emulator.exe"` |
| `appium: command not found` | Run `npm install -g appium` |

---

## Project Structure Reference

```
PrayerApp.UITests/
  PrayerApp.UITests.csproj      # net10.0, Appium.WebDriver 8.1.0, xUnit 2.9.2, Xunit.SkippableFact 1.5.61
  Infrastructure/
    AppiumSetup.cs               # IAsyncLifetime: seed, driver lifecycle, retry logic
    AppiumCollection.cs          # xUnit collection fixture definition
    TestConfig.cs                # Platform resolution, timeouts, device config, iOS UDID
    TestDataSeed.cs              # One-time seed DB build and push
    TestSeedFixtures.cs          # Names of per-test seeded fixtures
    SettingsShim.cs              # Host stand-in for the app's Settings class
  Helpers/
    AppExtensions.cs             # Element locators, tap/type/scroll, navigation reset
  Tests/
    HomeTests.cs                 # 2-Home
    QuickAddTests.cs             # 2-Home
    PrayerCardTests.cs           # 3-PrayerCards
    PrayerListTests.cs           # 4-Prayers
    UnsavedChangesTests.cs       # 5-UnsavedChanges
    BoxTests.cs                  # 8-Collections
    PrayerTimeTests.cs           # 8-PrayerTime
    DarkModeRenderingTests.cs    # 9-Settings (Android, Host=Windows)
    EdgeCaseTests.cs             # 12-EdgeCases
    AndroidTests.cs              # 14-Android
    ContextMenuImportTests.cs    # 14-Android
    ConfirmImportDuplicatesTests.cs  # 14-Android
    AccessibilityTests.cs        # 15-Accessibility
    TestConfigPlatformResolutionTests.cs  # Category=Unit
    TestConfigIOSUdidResolutionTests.cs   # Category=Unit
```
