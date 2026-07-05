---
name: prayer-app-e2e-run
description: >
  Use whenever the task is to RUN the Appium/UITest E2E suite on the Mac —
  "run the e2e tests", "run the uitest suite", "run android + ios sims", a
  full-suite or single-platform run, a parallel both-platforms run, a
  Definition-of-Done gate, or capturing screenshots through the suite. Also use
  when debugging a device SETUP failure that blocks the run — emulator/sim boot,
  app deploy/install, DB seeding (`run-as couldn't stat` / `exit 148`), a wedged
  UiAutomator2 session, or a corrupt install. This is the launch runbook, not
  the test-authoring guide (that is `prayer-app-ui-testing`). Follow it exactly;
  the sequence is deterministic and every step earns its place — improvising any
  of it corrupts device state and costs an hour.
---

# PrayerApp E2E Run (Mac — Android + iOS)

Build, deploy, seed, and run the `PrayerApp.UITests` Appium suite on **this Mac**,
one platform or both in parallel. The suite is one binary (`PrayerApp.UITests`);
the target platform is chosen at runtime by `UITEST_PLATFORM`, not the host OS.

---

## The one rule: follow this exactly, never improvise

Every failure we've hit came from *guessing* a step instead of following the runbook,
and each guess corrupts device state that then takes an hour to unwind (cold-booted
emulator → empty data dir → seed fails; shut-down sim → seed cascade; embedded-APK
`adb install -r` → corrupt install with no launcher activity). The launch is a fixed,
ordered sequence — there is no room for creativity in it.

**If a step here doesn't fit the situation, or something fails in a way this doc
doesn't name → STOP and ask.** Do not invent a workaround (do not swap build
configs, hand-roll `adb install`, uninstall/reinstall, or reboot a sim to "reset").
The process below is proven; when in doubt, re-read it, don't freelance.

Related: [`maui-android-emulator`](../maui-android-emulator/SKILL.md) (Android build/deploy gotchas in depth),
[`prayer-app-ui-testing`](../prayer-app-ui-testing/SKILL.md) (writing/debugging the tests themselves).

---

## When to Use

- Running the E2E suite for a Definition-of-Done gate, a release check, or to verify a change on-device.
- Running **both** Android + iOS in parallel on the Mac (the default ask — it's faster and there's no reason not to).
- Running a single platform, a section filter, or a screenshot-capture test.
- Debugging why the suite won't start: boot, deploy, seed, or session-wedge failures.

The fastest path is the bundled script: **`scripts/run-e2e-mac.sh`** (see [The script](#the-script)).
It encodes the whole sequence below. Read this doc first so you understand what it does
and can drive it by hand when a step needs attention.

---

## Environment (NOT persisted — export in every shell)

This Mac uses a **non-default** Android SDK/JDK location and does not persist the env.
Prepend this to any `adb`/`emulator`/`dotnet build -f net10.0-android` command:

```bash
export ANDROID_HOME="$HOME/Library/Developer/Android/sdk"
export JAVA_HOME="$HOME/Library/Developer/Android/jdk"
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$JAVA_HOME/bin:$PATH"
```

| Thing | Value on this Mac |
|---|---|
| App id / bundle | `com.multithreadedllc.prayercards` (both platforms) |
| Android AVD | `pp_api36` (NOT the `pixel_9_-_api_36_0` default in the docs/`TestConfig`) |
| iOS sim | `iPad (A16)` on iOS **26.5** (iPad on purpose — keyboard has a Done button) |
| Appium | 3.2.2, drivers `uiautomator2` + `xcuitest` installed |
| Android Appium port | `4723` (default) |
| iOS Appium port | `4725` (2nd server, for parallel) |

`run-uitests.ps1` is Windows-only and `pwsh` isn't installed here — **drive the steps
manually / via the bundled `.sh`, never that script.**

---

## The canonical sequence

Do these in order. Each `⚠` marks a spot where a past guess broke things.

### 0. Prereq — the UITest project must compile
The suite cross-compiles app source; if `PrayerApp.UITests` won't build, nothing runs.
`dotnet build PrayerApp.UITests/PrayerApp.UITests.csproj` and fix any missing
`<Compile Include>` before going further (this is the class of bug behind issue #302 —
a confidential-cards model the linked `PrayerCard.cs` references wasn't in the csproj).

### 1. Android emulator — boot WARM (snapshot), reuse if already up
```bash
adb devices    # reuse if an emulator-XXXX shows "device"
# else boot PLAIN — this loads the saved snapshot (warm state: app installed, data dir present):
emulator -avd pp_api36 &        # then: adb wait-for-device ; check getprop sys.boot_completed == 1
```
⚠ **Never boot with `-no-snapshot-load`.** A cold boot gives a fresh userdata where the app's
data dir `/data/user/0/com.multithreadedllc.prayercards/files/` doesn't exist, so the `run-as`
DB seed fails with `run-as: couldn't stat … No such file or directory` before a single test.
The snapshot boot restores the warm state where that dir already exists.

### 2. iOS simulator — boot and KEEP it booted
```bash
xcrun simctl boot "iPad (A16)"   # (or by UDID); reuse if already booted
```
⚠ **Do NOT shut the sim down before the run** — even though the `connectHardwareKeyboard`
comment in `TestConfig.GetIOSOptions` says to. The iOS seed (`SeedIOSAsync`,
`PreSeedOnboardingCompleteAsync`) shells out to `xcrun simctl … booted`; with the sim down
those fail `exit 148` ("No devices are booted") and cascade every test in ~1 ms. The iPad's
Done-button keyboard covers dismissal anyway, so leaving it booted costs nothing.

### 3. Deploy Android — MAUI `-t:Install`, Debug
```bash
dotnet build PrayerApp/PrayerApp.csproj -t:Install -f net10.0-android -c Debug
```
⚠ **Use `-t:Install`, not a hand-rolled APK.** Do NOT build `-p:EmbedAssembliesIntoApk=true`
and `adb install -r` it — layering that over an existing install produced a corrupt package
with **no launcher activity** (`monkey`/`resolve-activity` find nothing). `-t:Install` handles
FastDev + signing and registers the activity correctly. **Debug is mandatory** — the `run-as`
seed only works on a debuggable app (Release fails the seed). Never `adb uninstall` a working
FastDev install (wipes override assemblies → SIGABRT); redeploy with `-t:Install`.

### 4. Deploy iOS — sim slice, then `simctl install`, stay booted
```bash
dotnet build PrayerApp/PrayerApp.csproj -f net10.0-ios -c Debug -r iossimulator-arm64
xcrun simctl install "iPad (A16)" PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app
```
`-r iossimulator-arm64` selects the simulator slice of the App-Shortcuts native lib; without it
the link fails (issue #150). The harness attaches by `bundleId` with `noReset` and **no `app`
capability**, so you must install it yourself.

⚠ **One MAUI build at a time.** Concurrent Android+iOS builds fight over `obj/`/adb (`XAFD7000
device offline`). Build Android, then iOS. **Never kill a build mid-flight** (corrupts `obj/`
→ next build hangs at `aapt2`).

### 5. Verify seed-ready before running
```bash
adb shell run-as com.multithreadedllc.prayercards ls files/   # → prayer_app.db, profileInstalled, diagnostics.log
xcrun simctl list devices booted | grep -i ipad               # → iPad (A16) … (Booted)
```
If `run-as` still can't stat the dir, the emulator is cold — go back to step 1 (snapshot boot),
don't improvise a fix.

### 6. Start TWO Appium servers (one per platform → parallel)
```bash
appium --port 4723 --allow-insecure=uiautomator2:adb_shell &   # Android
appium --port 4725 &                                            # iOS
```
⚠ The Android flag must be driver-scoped: `--allow-insecure=uiautomator2:adb_shell` (or `*:adb_shell`).
Appium 2/3 **rejects** the bare `--allow-insecure adb_shell`. Confirm each with `curl :PORT/status`.

### 7. Build the test project ONCE
```bash
dotnet build PrayerApp.UITests/PrayerApp.UITests.csproj -c Debug
```
Both runs then use `--no-build` so two `dotnet test` processes never race on the same `obj/bin`.

### 8. Run both platforms in parallel (`--no-build`), as detached background processes
```bash
# Android → :4723 → emulator
UITEST_PLATFORM=android APPIUM_SERVER_URL=http://127.0.0.1:4723 ANDROID_AVD=pp_api36 \
  dotnet test PrayerApp.UITests/PrayerApp.UITests.csproj --no-build
# iOS → :4725 → iPad (A16) 26.5
UITEST_PLATFORM=ios APPIUM_SERVER_URL=http://127.0.0.1:4725 IOS_SIMULATOR="iPad (A16)" IOS_VERSION=26.5 \
  dotnet test PrayerApp.UITests/PrayerApp.UITests.csproj --no-build
```
Different devices + ports + processes → they don't collide; each `AppiumSetup` seeds its own device.
The harness picks the platform's tests at runtime and skips the other platform's (`SkippableFact`),
so no `--filter` is required.

⚠ **Drive these from the main thread as detached background processes (or `nohup`), never from a
background subagent** — background subagents auto-deny the `adb`/`dotnet` prompts and spin. A quick
one-time read of each log to confirm it cleared the seed is fine; then let them run.

### 9. Never kill a run mid-test
Killing `dotnet test` mid-run wedges the UiAutomator2 instrumentation; the next run fails every
test with *"instrumentation process is not running"*. Prefer letting a bad run finish (results
are still readable). **Recovery if wedged:** `adb shell am force-stop com.multithreadedllc.prayercards`
(do NOT uninstall the app), then `adb uninstall io.appium.uiautomator2.server` +
`io.appium.uiautomator2.server.test` so Appium redeploys fresh instrumentation next session.

---

## Failure modes (the same crap, solved)

| Symptom | Root cause | Fix |
|---|---|---|
| `run-as: couldn't stat /data/user/0/…: No such file or directory` at seed | Emulator cold-booted (`-no-snapshot-load`) → app data dir never created | Boot the emulator **plain** (snapshot); if truly fresh, `-t:Install` then launch the app once to create `files/` |
| iOS `xcrun simctl … booted … exit 148`, all tests fail in ~1 ms | Sim was shut down before the run | **Keep the sim booted** (step 2); ignore the `connectHardwareKeyboard` shutdown comment |
| Installed app has **no launcher activity** (`monkey`: "No activities found") | Hand-rolled embedded-APK `adb install -r` over an existing install | Uninstall the corrupt package, redeploy with `-t:Install` |
| `Fast Deployment is not currently supported on this device` during `-t:Install` | The app data dir doesn't exist yet (never-launched fresh install) | Boot warm (snapshot). On a genuinely fresh AVD: `-t:Install`, launch once, force-stop, then run |
| `CS0246 … 'CardProtectionMode' could not be found` building `PrayerApp.UITests` | A linked app source file references a type whose `.cs` isn't in the csproj `<Compile Include>` | Add the missing `<Compile Include="../PrayerApp/Models/…​.cs" />` (issue #302) |
| Every test: *"instrumentation process is not running"* | A prior run was killed mid-test (wedged UiAutomator2) | Force-stop the app; `adb uninstall` the two `io.appium.uiautomator2.server*` packages; re-run |
| `appium: feature name must include … Got 'adb_shell'` | Bare `--allow-insecure adb_shell` | Use `--allow-insecure=uiautomator2:adb_shell` |
| `XAFD7000 … device offline` at install | Concurrent MAUI builds / adb churn | One build at a time; `adb reconnect offline`; verify `getprop sys.boot_completed == 1` |

---

## The script

`scripts/run-e2e-mac.sh` runs the whole sequence: reuse/boot the emulator (warm) + sim,
deploy both (`-t:Install` Android, `-r iossimulator-arm64` iOS), verify seed-ready, stand up the
two Appium servers, build the test project once, then fire both `--no-build` runs in the
background and tail their logs.

```bash
./.claude/skills/prayer-app-e2e-run/scripts/run-e2e-mac.sh            # both platforms
./.claude/skills/prayer-app-e2e-run/scripts/run-e2e-mac.sh android    # one platform
./.claude/skills/prayer-app-e2e-run/scripts/run-e2e-mac.sh --skip-deploy   # devices already deployed
```

It mirrors the manual steps above and **stops with a clear error** rather than improvising if a
precondition isn't met (emulator not warm, sim not booted, build fails). If it stops, fix the named
step by hand from this doc — don't work around it.
