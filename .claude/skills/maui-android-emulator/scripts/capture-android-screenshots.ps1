<#
.SYNOPSIS
  Build a .NET MAUI app, deploy it to a running Android emulator, run a named
  screenshot-capture UI test, and collect the PNGs it produced — the proof that
  a feature renders correctly.

  Abstracted from a project's run-uitests.ps1 + screenshot runbook into one
  reusable command. Defaults to -c Debug on purpose: a Release build breaks any
  UI-test harness that seeds its DB via `adb shell run-as` (run-as needs a
  debuggable app). Self-heals the common adb wedge (kills stray clients, starts
  one clean server) so a piled-up adb daemon can't hang the install.

.NOTES
  Run from the repo root, in PowerShell — NOT Git Bash (Git Bash mangles adb
  device paths). Emulator + Appium are reused if already running, started if not.
  Do not kill the build mid-flight: that corrupts obj/ and hangs the next aapt2.

.EXAMPLE
  ./capture-android-screenshots.ps1 -Filter "FullyQualifiedName~<YourCaptureTest>" -OutDir "screenshots/<feature>"
#>
param(
  [Parameter(Mandatory)] [string]$Filter,                  # xUnit --filter selecting the capture test
  [string]$Csproj   = "PrayerApp/PrayerApp.csproj",
  [string]$Tfm      = "net10.0-android",
  [string]$Config   = "Debug",                             # Debug: required if the suite seeds via run-as
  [string]$AppId    = "com.multithreadedllc.prayercards",
  [string]$UiTests  = "PrayerApp.UITests/PrayerApp.UITests.csproj",
  [string]$Avd      = $(if ($env:ANDROID_AVD) { $env:ANDROID_AVD } else { "pixel_9_-_api_36_0" }),
  [string]$OutDir   = "screenshots/_capture",
  [string]$DiagDir  = "$env:TEMP\prayerapp-uitest-diag",   # where CaptureDiagnostics writes PNGs
  [string]$AppiumUrl= "http://127.0.0.1:4723",
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$adb = "$sdk\platform-tools\adb.exe"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# 1. adb: ensure ONE healthy server with an ONLINE device. Only hard-reset when
#    needed — killing a healthy adb can momentarily knock the emulator transport
#    "offline", which then fails the install step. Reset is the recovery path,
#    not the default.
Step "adb — ensure healthy"
& $adb start-server | Out-Null
$dev = (& $adb devices) -match "emulator-\d+\s+device"
if (-not $dev) {
  Write-Host "device not online — hard-resetting adb (clearing stray/hung clients)"
  Get-Process adb -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 1
  & $adb start-server | Out-Null
  & $adb reconnect offline 2>&1 | Out-Null
  Start-Sleep 2
  $dev = (& $adb devices) -match "emulator-\d+\s+device"
}

# 2. Emulator — reuse if up, else boot and wait.
Step "emulator"
if (-not $dev) {
  Start-Process "$sdk\emulator\emulator.exe" -ArgumentList "-avd",$Avd -WindowStyle Hidden
  & $adb wait-for-device
  Start-Sleep 10
}
& $adb devices

# 3. Appium — reuse if up, else start and wait.
Step "appium"
try { Invoke-WebRequest "$AppiumUrl/status" -TimeoutSec 3 | Out-Null; Write-Host "appium already running" }
catch { Start-Process appium -ArgumentList "--port","4723","--log-level","warn" -WindowStyle Hidden; Start-Sleep 5 }

# 4. Build + install via MAUI's -t:Install target (handles FastDev + signing; no manual adb install).
if (-not $SkipBuild) {
  Step "build + install ($Config) — do not interrupt"
  $t0 = Get-Date
  dotnet build $Csproj -t:Install -f $Tfm -c $Config
  if ($LASTEXITCODE -ne 0) { throw "build/install failed (exit $LASTEXITCODE)" }
  Write-Host ("build+install: {0}s" -f [int]((Get-Date)-$t0).TotalSeconds)
}

# 5. Run the capture test. Clear old PNGs first so we collect only this run's.
Step "capture test — $Filter"
Remove-Item "$DiagDir\*.png" -ErrorAction SilentlyContinue
$t1 = Get-Date
dotnet test $UiTests --filter $Filter   # native exit code; a test fail still leaves PNGs, so don't abort

# 6. Collect the proof PNGs produced this run.
Step "collect -> $OutDir"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$shots = Get-ChildItem "$DiagDir\*.png" -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $t1 }
if (-not $shots) { Write-Warning "No screenshots captured — confirm the test ran and CaptureDiagnostics succeeded."; exit 1 }
foreach ($s in $shots) { $d = Join-Path $OutDir $s.Name; Copy-Item $s.FullName $d -Force; Write-Host "captured: $d" }
Write-Host ("`n{0} screenshot(s) in {1}" -f $shots.Count, (Resolve-Path $OutDir)) -ForegroundColor Green
