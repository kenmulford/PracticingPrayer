# run-uitests.ps1 — Build, launch emulator, start Appium, run UI tests
# Usage: .\run-uitests.ps1 [-SkipBuild] [-SkipEmulator] [-SkipAppium] [-Configuration Debug|Release]
#
# Builds a DEBUG (debuggable) APK by default. The Android UI-test DB seed copies the
# prepared SQLite DB into the app sandbox via `adb shell run-as <pkg> cp ...`, and
# `run-as` only works on a debuggable app. A Release build fails the seed with
# `run-as ... cp ... failed (exit 1)` before any test runs. Building/installing Debug
# lets the seed succeed. (See the prayer-app-ui-testing skill: "NEVER seed the DB with
# a Release build — run-as requires a debuggable APK".)

param(
    [switch]$SkipBuild,
    [switch]$SkipEmulator,
    [switch]$SkipAppium,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$androidSdk = "$env:LOCALAPPDATA\Android\Sdk"
$emulatorExe = "$androidSdk\emulator\emulator.exe"
$adb = "$androidSdk\platform-tools\adb.exe"
$avdName = if ($env:ANDROID_AVD) { $env:ANDROID_AVD } else { "pixel_9_-_api_36_0" }
$package = "com.multithreadedllc.prayercards"

# Step 1: Start emulator (if not already running). Must precede build+install —
# the MAUI Install target deploys to a running device.
if (-not $SkipEmulator) {
    $devices = & $adb devices 2>&1
    if ($devices -notmatch "emulator|device") {
        Write-Host "`n=== Starting Android Emulator: $avdName ===" -ForegroundColor Cyan
        Start-Process $emulatorExe -ArgumentList "-avd", $avdName, "-no-snapshot-load" -WindowStyle Hidden
        Write-Host "Waiting for emulator to boot..."
        & $adb wait-for-device
        # Wait for FULL boot (sys.boot_completed), not just device visibility — installing
        # or seeding before boot completes is a flake source.
        for ($i = 0; $i -lt 60; $i++) {
            $booted = (& $adb shell getprop sys.boot_completed 2>$null).Trim()
            if ($booted -eq "1") { break }
            Start-Sleep -Seconds 5
        }
    } else {
        Write-Host "`n=== Emulator already running ===" -ForegroundColor Green
    }
}

# Step 2: Start Appium (if not already running)
if (-not $SkipAppium) {
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:4723/status" -TimeoutSec 2 -ErrorAction Stop
        Write-Host "`n=== Appium already running ===" -ForegroundColor Green
    } catch {
        Write-Host "`n=== Starting Appium server ===" -ForegroundColor Cyan
        Start-Process appium -ArgumentList "--port", "4723", "--allow-insecure", "uiautomator2:adb_shell", "--log-level", "warn" -WindowStyle Hidden
        Start-Sleep -Seconds 3
    }
}

# Step 3: Build + install the (debuggable) APK via the MAUI Install target.
# -t:Install builds and deploys with Fast Deployment, signature handling, and the
# correct per-config APK path — replacing a hard-coded Release apk path + `adb install`.
if (-not $SkipBuild) {
    Write-Host "`n=== Building + installing Android APK ($Configuration) ===" -ForegroundColor Cyan
    dotnet build PrayerApp/PrayerApp.csproj -f net10.0-android -c $Configuration -t:Install
    if ($LASTEXITCODE -ne 0) {
        # Common trap: a stale install with a higher versionCode (or a different signing
        # config, e.g. a prior Release build) blocks install with
        # INSTALL_FAILED_VERSION_DOWNGRADE / signature mismatch. Uninstall and retry once.
        # Re-running -t:Install redeploys Fast Deployment assemblies, so uninstall-then-
        # reinstall here is safe (unlike a bare `adb install` after an uninstall).
        Write-Host "Install failed — uninstalling stale app and retrying once..." -ForegroundColor Yellow
        & $adb uninstall $package 2>&1 | Out-Null
        dotnet build PrayerApp/PrayerApp.csproj -f net10.0-android -c $Configuration -t:Install
        if ($LASTEXITCODE -ne 0) { throw "APK build/install failed" }
    }
}

# Step 4: Run UI tests
Write-Host "`n=== Running UI Tests ===" -ForegroundColor Cyan
dotnet test PrayerApp.UITests/PrayerApp.UITests.csproj --logger "console;verbosity=detailed"

Write-Host "`n=== Done ===" -ForegroundColor Green
