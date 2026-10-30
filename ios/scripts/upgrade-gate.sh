#!/usr/bin/env bash
# Upgrade-in-place gate for the 2.0.0 spike (#381).
# Installs the shipped 1.7.2 MAUI build on the "Test iPad" simulator, seeds data and PIN 135790
# through the Appium suite, records row counts, installs the native build over it (no uninstall),
# then runs the UpgradeGateTests XCUITest, which asserts the counts and the PIN in-app.
# Exit 0 only when every check passes. Needs Appium on 127.0.0.1:4725 (prayer-app-e2e-run skill).
set -euo pipefail

SIM="Test iPad"
SIM_OS="${IOS_VERSION:-27.2}"
BUNDLE_ID="com.multithreadedllc.prayercards"
OLD_TAG="v1.7.2"
APPIUM_URL="http://127.0.0.1:4725"
HARD_CAP_SECONDS=900
GRACE_SECONDS=30

ROOT="$(git rev-parse --show-toplevel)"
# Resolved because the MAUI resizetizer fails when the project path goes through the /var symlink.
TMP="$(realpath "$(mktemp -d)")"
WORKTREE="$TMP/pp-1.7.2-issue-381"
DERIVED="$TMP/derived"
RESULT_BUNDLE="$TMP/gate.xcresult"
XCODE_LOG="$TMP/xcodebuild-test.log"
XCODE_PID=""
LOG_PID=""

GATE_LOG="${GATE_LOG:-$TMP/gate.log}"
GATE_DIR="$(dirname "$GATE_LOG")"
mkdir -p "$GATE_DIR"
: >"$GATE_LOG"
exec > >(tee -a "$GATE_LOG") 2>&1
touch "$TMP/run-start"

STEP_NUMBER=0
step() { STEP_NUMBER=$((STEP_NUMBER + 1)); printf '\n%s step %s: %s\n' "$(date +%T)" "$STEP_NUMBER" "$1"; }
tail_log() { tail -n "${2:-5}" "$1" | cut -c1-300; }
fail() { printf '\nupgrade-gate: %s\n' "$1" >&2; exit 1; }

cleanup() {
  local code=$?
  if [ -n "$XCODE_PID" ] && kill -0 "$XCODE_PID" 2>/dev/null; then
    pkill -P "$XCODE_PID" 2>/dev/null || true
    kill "$XCODE_PID" 2>/dev/null || true
  fi
  if [ -n "$LOG_PID" ]; then
    kill "$LOG_PID" 2>/dev/null || true
  fi
  find "$HOME/Library/Logs/DiagnosticReports" -name 'PrayerApp*.ips' -newer "$TMP/run-start" \
    -exec cp {} "$GATE_DIR/" \; 2>/dev/null || true
  if [ -d "$WORKTREE" ]; then
    git -C "$ROOT" worktree remove --force "$WORKTREE" 2>/dev/null || true
  fi
  git -C "$ROOT" worktree prune 2>/dev/null || true
  xcrun simctl shutdown "$SIM" 2>/dev/null || true
  rm -rf "$TMP"
  exit "$code"
}
trap cleanup EXIT

# Simulator builds embed entitlements in the binary's __TEXT,__entitlements section; codesign does not report them.
# otool prints little-endian 32-bit words, so each word is byte-swapped before decoding.
app_identifier() {
  local exe
  exe="$(plutil -extract CFBundleExecutable raw -o - "$1/Info.plist" 2>/dev/null)" || return 0
  otool -X -s __TEXT __entitlements "$1/$exe" 2>/dev/null \
    | awk '{for (i = 2; i <= NF; i++) printf "%s%s%s%s", substr($i,7,2), substr($i,5,2), substr($i,3,2), substr($i,1,2)}' \
    | xxd -r -p | tr -d '\0' \
    | plutil -extract application-identifier raw -o - - 2>/dev/null || true
}

table_count() {
  sqlite3 "$1" "select count(*) from $2" 2>/dev/null || echo 0
}

# UNAuthorizationStatus of the app as SpringBoard stored it: 0 not determined, 1 denied, 2 authorized.
# Prints "missing" until the plist holds the app and "error:<message>" on any failure, never a traceback.
notification_status() {
  python3 -I - "$(xcrun simctl getenv "$SIM" HOME)/Library/BulletinBoard/VersionedSectionInfo.plist" "$BUNDLE_ID" <<'PY' || echo "error:python exited $?"
import plistlib, sys

def find(o, bundle):
    if isinstance(o, dict):
        if isinstance(o.get(bundle), bytes):
            return o[bundle]
        children = o.values()
    elif isinstance(o, list):
        children = o
    else:
        return None
    for v in children:
        found = find(v, bundle)
        if found is not None:
            return found

def status(path, bundle):
    try:
        with open(path, "rb") as f:
            sections = plistlib.load(f)
    except FileNotFoundError:
        return "missing"
    data = find(sections, bundle)
    if data is None:
        return "missing"
    for obj in plistlib.loads(data)["$objects"]:
        if isinstance(obj, dict) and "authorizationStatus" in obj:
            return str(obj["authorizationStatus"])
    return "missing"

try:
    print(status(sys.argv[1], sys.argv[2]))
except Exception as e:
    print("error:%s: %s" % (type(e).__name__, e))
PY
}

# SpringBoard writes the plist after the alert is answered, so poll up to 10 s for a readable status.
wait_notification_status() {
  local status="" tries=0
  while [ "$tries" -lt 20 ]; do
    status="$(notification_status)"
    case "$status" in 0|1|2) break ;; esac
    sleep 0.5
    tries=$((tries + 1))
  done
  printf '%s' "$status"
}

step "appium"
curl -fsS "$APPIUM_URL/status" >/dev/null || fail "Appium is not running at $APPIUM_URL (start: appium --port 4725)"

step "build native app"
xcodebuild build-for-testing \
  -project "$ROOT/ios/PrayerApp.xcodeproj" -scheme PrayerApp -configuration Debug \
  -destination "platform=iOS Simulator,name=$SIM,OS=$SIM_OS" \
  -derivedDataPath "$DERIVED" >"$TMP/build-native.log" 2>&1 \
  || { tail -30 "$TMP/build-native.log" >&2; fail "native build failed"; }
NATIVE_APP="$DERIVED/Build/Products/Debug-iphonesimulator/PrayerApp.app"
tail_log "$TMP/build-native.log" 3
[ -d "$NATIVE_APP" ] || fail "native app not found at build output"

# GATE_V172_APP names a built 1.7.2 .app to reuse; when it does not exist yet, the fresh build is copied there.
if [ -n "${GATE_V172_APP:-}" ] && [ -d "$GATE_V172_APP" ]; then
  step "reuse built $OLD_TAG app"
  OLD_APP="$GATE_V172_APP"
else
  step "build $OLD_TAG"
  git -C "$ROOT" worktree add "$WORKTREE" "$OLD_TAG" >/dev/null 2>&1 || fail "cannot create worktree for $OLD_TAG"
  if [ -f "$ROOT/PrayerApp/iOS-signing.local.props" ]; then
    cp "$ROOT/PrayerApp/iOS-signing.local.props" "$WORKTREE/PrayerApp/iOS-signing.local.props"
  fi
  dotnet build "$WORKTREE/PrayerApp/PrayerApp.csproj" -f net10.0-ios -c Debug -r iossimulator-arm64 \
    -p:ValidateXcodeVersion=false >"$TMP/build-maui.log" 2>&1 \
    || { tail -30 "$TMP/build-maui.log" >&2; fail "$OLD_TAG build failed"; }
  OLD_APP="$WORKTREE/PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app"
  tail_log "$TMP/build-maui.log" 5
  [ -d "$OLD_APP" ] || fail "$OLD_TAG app not found at build output"
  if [ -n "${GATE_V172_APP:-}" ]; then
    mkdir -p "$(dirname "$GATE_V172_APP")"
    ditto "$OLD_APP" "$GATE_V172_APP"
  fi
fi

step "keychain access group guard"
OLD_ID="$(app_identifier "$OLD_APP")"
NEW_ID="$(app_identifier "$NATIVE_APP")"
echo "1.7.2 application-identifier: ${OLD_ID:-none}"
echo "native application-identifier: ${NEW_ID:-none}"
if [ -z "$OLD_ID" ] || [ "$OLD_ID" != "$NEW_ID" ]; then
  fail "keychain access group mismatch"
fi

step "install $OLD_TAG"
xcrun simctl boot "$SIM" 2>/dev/null || true
xcrun simctl bootstatus "$SIM" -b >/dev/null
xcrun simctl keychain "$SIM" reset
xcrun simctl uninstall "$SIM" "$BUNDLE_ID" 2>/dev/null || true
xcrun simctl install "$SIM" "$OLD_APP"

step "seed data and PIN"
UPGRADE_GATE=1 UITEST_PLATFORM=ios APPIUM_SERVER_URL="$APPIUM_URL" \
  IOS_SIMULATOR="$SIM" IOS_VERSION="$SIM_OS" \
  dotnet test "$ROOT/PrayerApp.UITests/PrayerApp.UITests.csproj" \
  --filter "FullyQualifiedName~UpgradeGateSeedTests" \
  || fail "seed and PIN step failed"

step "record row counts"
xcrun simctl terminate "$SIM" "$BUNDLE_ID" 2>/dev/null || true
CONTAINER="$(xcrun simctl get_app_container "$SIM" "$BUNDLE_ID" data)"
DB="$CONTAINER/Library/prayer_app.db"
CARDS="$(table_count "$DB" PrayerCard)"
PRAYERS="$(table_count "$DB" PrayerRequest)"
TAGS="$(table_count "$DB" PrayerTag)"
BOXES="$(table_count "$DB" CardBox)"
EXPECTED="PrayerCard=$CARDS,PrayerRequest=$PRAYERS,PrayerTag=$TAGS,CardBox=$BOXES"
echo "recorded: $EXPECTED"
if [ "$CARDS" = 0 ] && [ "$PRAYERS" = 0 ] && [ "$TAGS" = 0 ] && [ "$BOXES" = 0 ]; then
  fail "no prior install"
fi
REMINDERS="$(sqlite3 -separator ':' "$DB" "select Id, PrayerFrequency, ifnull(NotifyHour,''), ifnull(NotifyMinute,''), ifnull(NotifyDayOfWeek,''), ifnull(NotifyDayOfMonth,''), ifnull(NotifyDate,'') from PrayerRequest where Title like '% UpgradeGate' order by Id" | paste -sd ';' -)"
[ "$(sqlite3 "$DB" "select count(*) from PrayerRequest where Title like '% UpgradeGate'")" = 5 ] || fail "seed missing reminder prayers"
echo "recorded reminders: $REMINDERS"

APP_GROUP="$(xcrun simctl get_app_container "$SIM" "$BUNDLE_ID" groups | awk -F'\t' '$1 == "group.com.multithreadedllc.prayercards" {print $2}')"
[ -n "$APP_GROUP" ] || fail "app group container missing"
cp "$ROOT/contracts/app-group/pending-import.golden.json" "$APP_GROUP/pending-import.json"
IMPORT_TEXT="$(plutil -extract raw raw -o - "$APP_GROUP/pending-import.json" | head -n 1)"

step "notification authorization"
AUTH="$(wait_notification_status)"
echo "1.7.2 notification authorization status: $AUTH"
[ "$AUTH" = 2 ] || fail "1.7.2 is not authorized for notifications (status '$AUTH'; 2 = authorized, 0 = undecided, 1 = denied, missing/error:... = SpringBoard record unreadable); the seed step did not get the system alert accepted"

step "install native app over $OLD_TAG"
xcrun simctl install "$SIM" "$NATIVE_APP"

step "run gate"
: >"$GATE_DIR/gate-app.log"
xcrun simctl spawn "$SIM" log stream --level debug \
  --predicate 'process == "PrayerApp" OR subsystem == "com.multithreadedllc.prayercards"' >"$GATE_DIR/gate-app.log" 2>&1 &
LOG_PID=$!
# xcodebuild can hang after the suite result prints, so the log is polled and the process killed after a grace period.
TEST_RUNNER_UPGRADE_GATE_EXPECTED="$EXPECTED" \
TEST_RUNNER_UPGRADE_GATE_REMINDERS="$REMINDERS" \
TEST_RUNNER_UPGRADE_GATE_IMPORT_TEXT="$IMPORT_TEXT" \
xcodebuild test-without-building \
  -project "$ROOT/ios/PrayerApp.xcodeproj" -scheme PrayerApp -configuration Debug \
  -destination "platform=iOS Simulator,name=$SIM,OS=$SIM_OS" \
  -derivedDataPath "$DERIVED" \
  -only-testing:PrayerAppUITests/UpgradeGateTests \
  -resultBundlePath "$RESULT_BUNDLE" >"$XCODE_LOG" 2>&1 &
XCODE_PID=$!

elapsed=0
while kill -0 "$XCODE_PID" 2>/dev/null; do
  if grep -qE "Test Suite 'UpgradeGateTests' (passed|failed)" "$XCODE_LOG"; then
    grace=0
    while kill -0 "$XCODE_PID" 2>/dev/null && [ "$grace" -lt "$GRACE_SECONDS" ]; do
      sleep 5
      grace=$((grace + 5))
    done
    break
  fi
  if [ "$elapsed" -ge "$HARD_CAP_SECONDS" ]; then
    break
  fi
  sleep 5
  elapsed=$((elapsed + 5))
done
if kill -0 "$XCODE_PID" 2>/dev/null; then
  pkill -P "$XCODE_PID" 2>/dev/null || true
  kill "$XCODE_PID" 2>/dev/null || true
  sleep 2
  kill -9 "$XCODE_PID" 2>/dev/null || true
fi

if grep -q "Test Suite 'UpgradeGateTests' passed" "$XCODE_LOG"; then
  echo "upgrade-gate: PASS ($EXPECTED)"
  exit 0
fi

if grep -q "error:" "$XCODE_LOG"; then
  grep -m1 -E "error:" "$XCODE_LOG" | cut -c1-600
else
  tail -30 "$XCODE_LOG" >&2
fi
fail "FAIL (expected $EXPECTED)"
