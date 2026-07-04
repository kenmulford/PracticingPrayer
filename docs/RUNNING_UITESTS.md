# Running the Appium UI Test Suite

> 81 automated tests across 15 test files. Each `dotnet test` run targets **one** platform. By default the host OS chooses it — **Windows runs Android, macOS runs iOS** — or set the `UITEST_PLATFORM` environment variable (`android` | `ios`) to choose explicitly, which lets **macOS run the Android suite** too. The driver is a single shared session, so run the two platforms as **separate, sequential** `dotnet test` runs — never both at once.

---

## Prerequisites

### Both Platforms

| Tool | Version | Install |
|------|---------|---------|
| .NET SDK | 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Node.js | 18+ | [nodejs.org](https://nodejs.org) (needed for Appium) |
| Appium | 2.x | `npm install -g appium` |

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
| Xcode | 16+ | Mac App Store |
| Xcode Command Line Tools | — | `xcode-select --install` |
| iOS Simulator | — | Default: `iPad (A16)` on iOS 26.4 |
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

# Boot a simulator (use the iPad A16 or similar)
xcrun simctl boot "iPad (A16)"
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

# Install on booted simulator
xcrun simctl install booted PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app
```

### 4. Start the Appium server

In a **separate terminal** (leave it running for the entire test session):

**PC (PowerShell):**
```powershell
appium
```

**Mac (Terminal):**
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
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=iOS"
```

### Mac (Android — Terminal)

The Mac can run the Android suite too. Set `UITEST_PLATFORM=android` so the harness builds an `AndroidDriver` against the local emulator instead of the iOS default. Boot the Android emulator and install the app first (see [Setup](#setup)), then:

```bash
UITEST_PLATFORM=android dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"
```

Run only **one** platform per `dotnet test` invocation. To exercise both from the Mac, run the iOS command and the Android command **one after the other** — never in parallel. They share a single Appium driver session and would collide.

### Run a single test section

Tests are organized by numbered sections:

| Section | Tests |
|---------|-------|
| `1-Onboarding` | Onboarding flow, banner steps |
| `2-Home` | Dashboard metrics, Quick Add |
| `3-PrayerCards` | Card CRUD, expand/collapse, action chips |
| `4-Prayers` | Prayer list, search, filters |
| `5-UnsavedChanges` | Dirty-form guards on all edit pages |
| `6-Reminders` | Notification toggle, frequency picker |
| `7-Tags` | Tag CRUD, color picker |
| `8-Collections` | Box CRUD, multi-select, move |
| `8-PrayerTime` | Prayer Time session flow |
| `9-Settings` | Settings hub, backup, about |
| `12-EdgeCases` | Empty states, edge behaviors |
| `13-FeatureGaps` | F-23 card, FAQ accordion, collection scope, overdue, favorite |
| `14-Android` | Android-only (TimePicker input mode) |
| `15-Accessibility` | Composed descriptions, chip state, headings, tree exclusion |

```powershell
# Run only Home tests
dotnet test PrayerApp.UITests/ --filter "Section=2-Home"

# Run a single test by name
dotnet test PrayerApp.UITests/ --filter "FullyQualifiedName~Home_QuickAdd"
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
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=iOS" -v detailed 2>&1 |
  grep -E "Failed|Passed|Skipped"
```

**Rerun a specific failed test by name:**
```powershell
dotnet test PrayerApp.UITests/ --filter "FullyQualifiedName~Cards_FilterChip"
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

### Important: test order matters

Tests within the `"Appium"` collection share a single driver session (one app launch for the entire run). Onboarding is dismissed once at the start. Running tests out of section order may cause failures if earlier tests create data that later tests expect.

---

## Environment Variables

All optional. Set before running `dotnet test`.

| Variable | Default | Description |
|----------|---------|-------------|
| `UITEST_PLATFORM` | _(host OS default)_ | Force the target platform: `android` or `ios`. Unset → Windows runs Android, macOS runs iOS. Set `android` to run the Android suite from macOS. An unrecognized value runs nothing (the harness throws `PlatformNotSupportedException`). |
| `APPIUM_SERVER_URL` | `http://127.0.0.1:4723` | Appium server endpoint |
| `ANDROID_AVD` | `pixel_9_-_api_36_0` | Android emulator AVD name |
| `PRAYER_APK_PATH` | _(pre-installed)_ | Path to APK; if set, Appium installs it |
| `IOS_SIMULATOR` | `iPad (A16)` | iOS simulator device name |
| `IOS_VERSION` | `26.4` | iOS platform version string |

**PC (PowerShell — session-only):**
```powershell
$env:ANDROID_AVD = "Pixel_8_API_35"
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=Android"
```

To set permanently on PC:
```powershell
[System.Environment]::SetEnvironmentVariable("ANDROID_AVD", "Pixel_8_API_35", "User")
```

**Mac (Terminal — session-only):**
```bash
export IOS_SIMULATOR="iPhone 16 Pro"
export IOS_VERSION="26.4"
dotnet test PrayerApp.UITests/ --filter "Platform=CrossPlatform|Platform=iOS"
```

To set permanently on Mac, add to `~/.zshrc`:
```bash
echo 'export IOS_SIMULATOR="iPhone 16 Pro"' >> ~/.zshrc
echo 'export IOS_VERSION="26.4"' >> ~/.zshrc
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

Several tests already call `DumpPageSource` on failure paths (EdgeCaseTests, PrayerTimeTests, PrayerListTests).

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
| iOS text input fails | Use iPad simulator (has keyboard dismiss button). If iPhone is needed, ensure `DismissKeyboardIfPresent()` is called |
| Android `resource-id` not found | AutomationId maps to `content-desc` on some elements. The helpers try both strategies automatically |
| `adb: command not found` | Run the environment variable setup in [Prerequisites](#pc-android) to add `platform-tools` to PATH |
| `emulator: command not found` | The emulator isn't on PATH by default — use the full path: `& "$env:ANDROID_HOME\emulator\emulator.exe"` |
| `appium: command not found` | Run `npm install -g appium` |

---

## Project Structure Reference

```
PrayerApp.UITests/
  PrayerApp.UITests.csproj      # net10.0, Appium.WebDriver 8.1.0, xUnit 2.9.2
  Infrastructure/
    AppiumSetup.cs               # IAsyncLifetime — driver lifecycle, retry logic
    AppiumCollection.cs          # xUnit collection fixture definition
    TestConfig.cs                # Platform detection, timeouts, device config
  Helpers/
    AppExtensions.cs             # Element locators, tap/type/scroll, onboarding
  Tests/
    OnboardingTests.cs           # Section 1 — must run first
    HomeTests.cs                 # Section 2
    QuickAddTests.cs             # Section 2
    PrayerCardTests.cs           # Section 3
    PrayerListTests.cs           # Section 4
    UnsavedChangesTests.cs       # Section 5
    ReminderTests.cs             # Section 6
    TagTests.cs                  # Section 7
    BoxTests.cs                  # Section 8
    PrayerTimeTests.cs           # Section 8
    SettingsTests.cs             # Section 9
    EdgeCaseTests.cs             # Section 12
    FeatureGapTests.cs           # Section 13
    AndroidTests.cs              # Section 14 (Android-only)
    AccessibilityTests.cs        # Section 15
```
