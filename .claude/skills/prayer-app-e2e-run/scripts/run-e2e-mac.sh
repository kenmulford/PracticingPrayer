#!/usr/bin/env bash
#
# run-e2e-mac.sh — run the PrayerApp.UITests Appium suite on this Mac.
#
# Encodes the proven launch sequence from the prayer-app-e2e-run skill. It does
# NOT improvise: if a precondition isn't met it stops with a clear error naming
# the step to fix by hand — it never cold-boots, hand-rolls an APK, shuts the sim
# down, or reinstalls to "reset". Read ../SKILL.md before changing this.
#
# Usage:
#   run-e2e-mac.sh [both|android|ios] [--skip-deploy] [--skip-build]
#
# Blocks until the suites finish, so launch it detached (nohup, or a background task).
# Exit: 0 every suite passed, 1 a suite failed, 2 stopped before any test ran.
#
set -euo pipefail

PLATFORM="both"
SKIP_DEPLOY=0
SKIP_BUILD=0
for a in "$@"; do
  case "$a" in
    both|android|ios) PLATFORM="$a" ;;
    --skip-deploy)    SKIP_DEPLOY=1 ;;
    --skip-build)     SKIP_BUILD=1 ;;
    *) echo "unknown arg: $a" >&2; exit 2 ;;
  esac
done

# --- environment (this Mac uses a non-default SDK/JDK path; env is not persisted) ---
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Developer/Android/sdk}"
export JAVA_HOME="${JAVA_HOME:-$HOME/Library/Developer/Android/jdk}"
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$JAVA_HOME/bin:$PATH"

APP_ID="com.multithreadedllc.prayercards"
AVD="${ANDROID_AVD:-pp_api36}"
IOS_SIM="${IOS_SIMULATOR:-Test iPad}"
IOS_VER="${IOS_VERSION:-27.0}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
CSPROJ="$REPO_ROOT/PrayerApp/PrayerApp.csproj"
UITESTS="$REPO_ROOT/PrayerApp.UITests/PrayerApp.UITests.csproj"
LOGDIR="${TMPDIR:-/tmp}/pp-e2e"
mkdir -p "$LOGDIR"

step() { printf '\n=== %s ===\n' "$1"; }
die()  { printf '\n[run-e2e] STOP: %s\n' "$1" >&2; exit 2; }
want_android() { [ "$PLATFORM" = both ] || [ "$PLATFORM" = android ]; }
want_ios()     { [ "$PLATFORM" = both ] || [ "$PLATFORM" = ios ]; }

# --- 1. Android emulator: reuse if up, else boot WARM (snapshot — never -no-snapshot-load) ---
if want_android; then
  step "android emulator"
  adb start-server >/dev/null 2>&1 || true
  if ! adb devices | grep -qE 'emulator-[0-9]+\s+device'; then
    echo "booting $AVD (snapshot)…"
    nohup emulator -avd "$AVD" >"$LOGDIR/emulator.log" 2>&1 &
    emu_pid=$!
    # Bounded poll, not `adb wait-for-device`, which blocks forever if the emulator never attaches.
    for _ in $(seq 1 90); do
      [ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && break
      kill -0 "$emu_pid" 2>/dev/null || break
      sleep 2
    done
  fi
  [ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] \
    || die "emulator not booted — check $LOGDIR/emulator.log"
  adb devices
fi

# --- 2. iOS simulator: boot and KEEP booted (never shut down before the run) ---
if want_ios; then
  step "ios simulator"
  command -v jq >/dev/null || die "jq not found (ships with macOS at /usr/bin/jq)"
  # A name alone matches one simulator per installed runtime, and the test harness addresses
  # "booted": resolve one simulator by name + runtime, address it by UDID, refuse a second booted one.
  SIM_JSON=$(xcrun simctl list devices -j) || die "xcrun simctl list devices -j failed"
  IOS_RUNTIME="com.apple.CoreSimulator.SimRuntime.iOS-${IOS_VER//./-}"
  SIM_MATCHES=$(printf '%s\n' "$SIM_JSON" | jq -r --arg rt "$IOS_RUNTIME" --arg name "$IOS_SIM" \
    '(.devices[$rt] // [])[] | select(.name == $name and .isAvailable == true) | .udid') \
    || die "could not read the simulator list"
  case "$SIM_MATCHES" in
    "")      die "no available simulator named '$IOS_SIM' on iOS $IOS_VER; list them with 'xcrun simctl list devices available' and set IOS_SIMULATOR / IOS_VERSION" ;;
    *$'\n'*) die "several available simulators named '$IOS_SIM' on iOS $IOS_VER: ${SIM_MATCHES//$'\n'/ }; set IOS_SIMULATOR to a unique name" ;;
  esac
  IOS_UDID="$SIM_MATCHES"
  echo "ios simulator: $IOS_SIM, iOS $IOS_VER, $IOS_UDID"
  SIM_OTHERS=$(printf '%s\n' "$SIM_JSON" | jq -r --arg u "$IOS_UDID" \
    '.devices | to_entries[] | .key as $rt | .value[] | select(.state == "Booted" and .udid != $u) | "\(.name) \($rt | ltrimstr("com.apple.CoreSimulator.SimRuntime.")) \(.udid) (xcrun simctl shutdown \(.udid))"') \
    || die "could not read the simulator list"
  [ -z "$SIM_OTHERS" ] || die "another simulator is booted and the harness addresses 'booted': ${SIM_OTHERS//$'\n'/; }"
  echo "ensuring ${IOS_SIM} is booted…"
  xcrun simctl bootstatus "$IOS_UDID" -b || die "iOS sim '$IOS_SIM' ($IOS_UDID) did not boot"
fi

# --- 3/4. Deploy (one MAUI build at a time; never concurrent) ---
if [ "$SKIP_DEPLOY" -eq 0 ]; then
  if want_android; then
    step "deploy android (-t:Install, Debug)"
    dotnet build "$CSPROJ" -t:Install -f net10.0-android -c Debug || die "android -t:Install failed"
  fi
  if want_ios; then
    step "deploy ios (-r iossimulator-arm64, Debug)"
    dotnet build "$CSPROJ" -f net10.0-ios -c Debug -r iossimulator-arm64 || die "ios build failed"
    xcrun simctl install "$IOS_UDID" \
      "$REPO_ROOT/PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app" || die "simctl install failed"
  fi
fi

# --- 5. Verify seed-ready ---
step "verify seed-ready"
if want_android; then
  adb shell run-as "$APP_ID" ls files/ 2>/dev/null | grep -q prayer_app.db \
    || die "android app data dir not seed-ready (run-as can't see files/prayer_app.db). Emulator is cold — boot warm (snapshot), see SKILL.md step 1."
  echo "android: files/ present"
fi
if want_ios; then
  xcrun simctl list devices booted | grep -q "$IOS_UDID" || die "iOS sim not booted"
  echo "ios: sim booted"
fi

# --- 6. Appium servers (reuse if up) ---
appium_up() { curl -sf --max-time 3 "http://127.0.0.1:$1/status" >/dev/null 2>&1; }
start_appium() {  # port, extra-args
  local port="$1"; shift
  if appium_up "$port"; then
    echo "appium :$port already up"
  else
    echo "starting appium :${port}…"
    nohup appium --port "$port" "$@" >"$LOGDIR/appium-$port.log" 2>&1 &
    for _ in $(seq 1 15); do appium_up "$port" && break; sleep 1; done
    appium_up "$port" || die "appium :$port failed to start — see $LOGDIR/appium-$port.log"
  fi
}
step "appium servers"
want_android && start_appium 4723 --allow-insecure=uiautomator2:adb_shell
want_ios     && start_appium 4725

# --- 7. Build the test project ONCE (both runs use --no-build) ---
if [ "$SKIP_BUILD" -eq 0 ]; then
  step "build test project once"
  dotnet build "$UITESTS" -c Debug || die "PrayerApp.UITests build failed (check <Compile Include> — issue #302 class)"
fi

# --- 8. Run (detached background; never kill mid-run) ---
step "run"
PIDS=(); PLATS=(); LOGS=()
run_platform() {  # platform, port, log, extra-env...
  local plat="$1" port="$2" log="$3"; shift 3
  echo "$plat → :$port → $log"
  nohup env UITEST_PLATFORM="$plat" APPIUM_SERVER_URL="http://127.0.0.1:$port" "$@" \
    dotnet test "$UITESTS" --no-build >"$log" 2>&1 &
  PIDS+=("$!"); PLATS+=("$plat"); LOGS+=("$log")
}
if want_android; then run_platform android 4723 "$LOGDIR/android.log" ANDROID_AVD="$AVD"; fi
if want_ios;     then run_platform ios 4725 "$LOGDIR/ios.log" IOS_SIMULATOR="$IOS_SIM" IOS_VERSION="$IOS_VER"; fi

echo
echo "runs launched (PIDs: ${PIDS[*]}). Do NOT kill mid-test (wedges UiAutomator2)."
echo "watch:  tail -f ${LOGS[*]}"
echo "waiting for completion…"

# Wait on the test runs only: a bare `wait` also blocks on an emulator or appium this script started.
STATUS=()
for pid in "${PIDS[@]}"; do s=0; wait "$pid" || s=$?; STATUS+=("$s"); done

step "results"
rc=0
for i in "${!PIDS[@]}"; do
  echo "-- ${PLATS[$i]} (dotnet test exit ${STATUS[$i]}) --"
  grep -E 'Passed!|Failed!|Total tests|Passed:|Failed:|Skipped:' "${LOGS[$i]}" | tail -6 || echo "no test summary in ${LOGS[$i]}"
  [ "${STATUS[$i]}" -eq 0 ] || rc=1
done
exit "$rc"
