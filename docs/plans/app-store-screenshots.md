# App Store Screenshots — Plan & Runbook

> Created 2026-03-28 after completing the first iOS screenshot session.

---

## Goal

Capture polished App Store screenshots on iPhone and iPad simulators with realistic prayer data, in both light and dark mode.

---

## Device & Dimension Matrix

Apple's docs and App Store Connect validation **disagree**. These are the validated sizes that actually upload successfully:

| Category | Simulator | Native Resolution | Accepted Size | Action |
|----------|-----------|-------------------|---------------|--------|
| iPhone 6.5" portrait | iPhone 17 Pro Max | 1320 × 2868 | **1284 × 2778** | Resize with `sips -z 2778 1284` |
| iPhone 6.5" landscape | iPhone 17 Pro Max | 2868 × 1320 | **2778 × 1284** | Resize with `sips -z 1284 2778` |
| iPad 13" portrait | iPad Pro 13" (M5) | 2064 × 2752 | **2064 × 2752** | No resize needed |

> The 6.9" category (1320×2868) that Apple's docs list as the primary required size is **rejected** by App Store Connect validation as of March 2026. Use the 6.5" category instead.

---

## Screenshot List

### Per Device (iPhone + iPad)

| # | Screen | Filename | Notes |
|---|--------|----------|-------|
| 1 | Welcome / Onboarding | `01-welcome-onboarding.png` | Requires fresh install (uninstall + reinstall) |
| 2 | Prayer Cards | `02-prayer-cards.png` | Cards organized into collection sections (Personal, Ministry). Expand one to show nested requests. |
| 3 | Prayer Detail | `03-prayer-detail.png` | Tap "Wisdom for the job transition" — shows title, details, tags, Share/Mark Answered |
| 4 | Prayer List (Active) | `04-prayer-list.png` | Prayers tab with Active filter, tag chips, all requests listed |
| 5 | Answered Prayers | `05-answered-prayers.png` | Prayers tab with Answered filter — strikethrough text + date |
| 6 | Tags List | `06-tags-list.png` | Tags tab showing colored dots |
| 7 | Tag Detail | `07-tag-detail.png` | Tap "Family" tag — shows name, color picker swatches |
| 8 | Prayer Time | `08-prayer-time.png` | **iPhone: landscape** (rotate image with `sips -r 270`), **iPad: portrait** |
| 9 | Quick Add | `09-quick-add.png` | Home → Quick Add button — shows modal with prayer title field |
| 10 | Manage Collections | `10-manage-collections.png` | Cards tab → Collections toolbar — shows user + system collections with card counts |
| 11 | Confirm Import | `11-confirm-import.png` | **iPhone only — light + dark.** Reached by triggering the share-extension import flow (tap a share-extension shortcut OR open a `.json` import payload from another app). Confirm Import page shows parsed prayer items + Import/Cancel buttons. **No iPad capture needed.** |

### Dark Mode (high-impact only, both devices)

| # | Screen | Filename | Notes |
|---|--------|----------|-------|
| 1 | Prayer Cards (expanded) | `dark/02-prayer-cards.png` | |
| 2 | Prayer List (Active) | `dark/04-prayer-list.png` | |
| 3 | Prayer Time | `dark/08-prayer-time.png` | iPhone: landscape (rotate with `sips -r 270`), iPad: portrait |
| 4 | Manage Collections | `dark/10-manage-collections.png` | |
| 11 | Confirm Import | `dark/11-confirm-import.png` | iPhone only |

---

## Seed Data

All data is inserted directly into the SQLite database while the app is terminated. Timestamps use .NET ticks (100-nanosecond intervals since Jan 1, 0001).

### Timestamp Reference

```
NOW           = 639102550000000000  # ~March 28, 2026
WEEK_AGO      = 639101945200000000
TWO_WEEKS     = 639101340400000000
MONTH_AGO     = 639100130800000000
THREE_MONTHS  = 639092370000000000
ANSWERED_AT   = 639101500000000000
```

### Card Boxes (Collections)

| Id | Name | IsSystem | SystemKey | SortOrder |
|----|------|----------|-----------|-----------|
| 1 | System | 1 | system | 900 |
| 2 | Archived | 1 | archived | 999 |
| 3 | Personal | 0 | (null) | 0 |
| 4 | Ministry | 0 | (null) | 0 |

### Prayer Cards

| Id | Title | IsFavorite | IsSystem | BoxId | Created |
|----|-------|-----------|----------|-------|---------|
| 1 | Quick Add | 0 | 1 | 1 | (auto-created by app) |
| 2 | Family & Health | 1 | 0 | 3 | THREE_MONTHS |
| 3 | Career & Purpose | 0 | 0 | 4 | TWO_WEEKS |
| 4 | Gratitude | 0 | 0 | 3 | MONTH_AGO |

### Prayer Requests

| Id | Card | Title | Details | IsAnswered |
|----|------|-------|---------|-----------|
| 1 | Family & Health | Mom's recovery from knee surgery | She had surgery last Tuesday. Praying for a smooth recovery and that physical therapy goes well. The doctor says 6-8 weeks for full mobility. | No |
| 2 | Family & Health | Peace for Sarah during finals | She's been so stressed with exams coming up. Praying she finds rest and clarity, and remembers how capable she is. | No |
| 3 | Family & Health | Strength for Dad during treatment | Grateful the prognosis is good. Praying for endurance through the remaining rounds and for the whole family's peace. | **Yes** |
| 4 | Career & Purpose | Wisdom for the job transition | The new role starts in April. Praying for confidence in the learning curve and discernment about which opportunities to pursue. | No |
| 5 | Career & Purpose | Direction for the ministry opportunity | The youth group asked me to lead a small group this fall. Praying about whether this is the right season to say yes. | No |
| 6 | Gratitude | Thankful for our new church community | We've felt so welcomed since joining. Grateful for the connections and friendships forming. Praying we can give back and serve. | No |
| 7 | Quick Add | Safe travels for the mission trip team | *(empty details)* | No |

### Tags

| Id | Name | Color (light hex) |
|----|------|-------------------|
| 1 | Recently Notified | #505050 (system) |
| 2 | Family | #B84040 (Red) |
| 3 | Health | #1E7870 (Teal) |
| 4 | Work | #2E5A9A (Blue) |
| 5 | Guidance | #663C8C (Purple) |
| 6 | Gratitude | #B35A20 (Orange) |

### Tag Assignments (PrayerCardTag)

| Card | Tag | Request |
|------|-----|---------|
| Family & Health | Family | Mom's recovery |
| Family & Health | Health | Mom's recovery |
| Family & Health | Family | Peace for Sarah |
| Family & Health | Health | Strength for Dad |
| Family & Health | Family | Strength for Dad |
| Career & Purpose | Work | Wisdom for job |
| Career & Purpose | Guidance | Wisdom for job |
| Career & Purpose | Guidance | Direction for ministry |
| Gratitude | Gratitude | Thankful for community |

### Prayer Interactions

Interactions populate the "Last prayed" timestamps.

| Request | Type | When |
|---------|------|------|
| Mom's recovery | Prayed | WEEK_AGO |
| Peace for Sarah | Prayed | WEEK_AGO |
| Wisdom for job | Prayed | NOW |
| Direction for ministry | Prayed | NOW |
| Thankful for community | Prayed | TWO_WEEKS |
| Safe travels | Prayed | WEEK_AGO |

---

## Step-by-Step Runbook

> **Capture script.** Per-screen Appium navigation entry points live in [`scripts/screenshot_nav.py`](../../scripts/screenshot_nav.py) (pure-stdlib, no pip deps; one sim per invocation).

### 1. Build for Simulator

```bash
dotnet build PrayerApp/PrayerApp.csproj -c Debug -f net10.0-ios -r iossimulator-arm64
```

Output: `PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app`

> **Use Debug, not Release.** The Confirm Import capture path (#11) depends on the Developer-section "Stage sample payload" diagnostic, which is wrapped in `#if DEBUG` (`Views/Settings/AppSettingsPage.xaml.cs`) and does not exist in Release builds.

### 2. Boot & Install

```bash
sim_udid() { xcrun simctl list devices -j | jq -r --arg n "$1" '.devices[][] | select(.name == $n) | .udid'; }
IPHONE=$(sim_udid "Dev iPhone")  # iPhone 17 Pro Max; see "Simulators" in CLAUDE.md
IPAD=$(sim_udid "Dev iPad")      # iPad Pro 13" (M5)
APP=PrayerApp/bin/Debug/net10.0-ios/iossimulator-arm64/PrayerApp.app

xcrun simctl boot "$IPHONE"
xcrun simctl boot "$IPAD"
xcrun simctl install "$IPHONE" $APP
xcrun simctl install "$IPAD" $APP
xcrun simctl ui "$IPHONE" appearance light
xcrun simctl ui "$IPAD" appearance light
```

### 3. Capture Onboarding (fresh install only)

Launch the app. The "Welcome to Practicing Prayer" popup appears on first launch.

```bash
xcrun simctl launch $IPHONE com.multithreadedllc.prayercards
sleep 3
xcrun simctl io $IPHONE screenshot screenshots/iphone/light/01-welcome-onboarding.png
```

Repeat for iPad. Then dismiss the popup via Appium or terminate + mark onboarding complete.

### 4. Populate Seed Data

Terminate the app, find the DB, insert data:

```bash
xcrun simctl terminate $IPHONE com.multithreadedllc.prayercards
# iOS-specific path: MAUI's FileSystem.AppDataDirectory resolves to Library/ on iOS,
# NOT Documents/ (which is the Android pattern). Constrain the find pattern to Library/
# so we don't accidentally pick up a stale seed in the wrong directory.
DB=$(find ~/Library/Developer/CoreSimulator/Devices/$IPHONE/data/Containers/Data -path "*/Library/prayer_app.db")
# Sanity-check before inserting — empty DB var means the app hasn't created the DB yet
# (launch the app once, then terminate, then re-run the find).
[ -z "$DB" ] && echo "ERROR: prayer_app.db not found under any Library/ directory" && return 1
echo "Seeding into: $DB"
# Run the INSERT statements from the Seed Data section above
# Mark onboarding complete:
xcrun simctl spawn $IPHONE defaults write com.multithreadedllc.prayercards OnboardingComplete -bool true
xcrun simctl spawn $IPHONE defaults write com.multithreadedllc.prayercards QuickAddTipDismissed -bool true
xcrun simctl spawn $IPHONE defaults write com.multithreadedllc.prayercards CollectionsBannerDismissed -bool true
```

> **Why the `Library/` constraint matters.** On iOS, MAUI's `FileSystem.AppDataDirectory` resolves to `Library/`, not `Documents/`. The canonical path is constructed in `PrayerApp/MauiProgram.cs:35` (`Path.Combine(FileSystem.AppDataDirectory, "prayer_app.db")`) and re-used in `PrayerApp/Services/BackupService.cs:28`. Seed copies dropped into `Documents/` will be silently ignored by the running app.

### 5. Take Screenshots

Use Appium MCP for navigation, `xcrun simctl io` for capture:

```bash
xcrun simctl io $IPHONE screenshot <output-path>.png
```

For iPhone Prayer Time (landscape): capture in portrait, then rotate:
```bash
sips -r 270 input.png --out output.png
```

### 5b. Confirm Import (#11) — capture path

Confirm Import has no end-user-driven entry point that's reliably reproducible inside an Appium-driven sim run (the share-extension flow requires another app holding share-able text). Use the **Debug-only diagnostic** instead:

| Step | AutomationId / target |
|------|-----------------------|
| 1. Tap the Settings tab in the bottom tab bar | (tab bar — Settings) |
| 2. Drill into App Settings | `Settings_Row_AppSettings` |
| 3. Scroll to the Developer section, tap "Stage sample payload" | `AppSettings_Btn_StageSamplePayload` |

Lands on the Confirm Import page populated with a sample payload (3 demo prayers, suggested card title `Imported {today's month + day}`). Capture light + dark, then back-out (or terminate + relaunch).

> **Debug-only.** `AppSettings_Btn_StageSamplePayload` lives in the Developer section, which is wrapped in `#if DEBUG` (`Views/Settings/AppSettingsPage.xaml.cs`). It does not exist in Release builds — this is the reason step 1 builds Debug throughout.

### 6. Switch to Dark Mode

```bash
xcrun simctl ui "$IPHONE" appearance dark
```

Retake the 3 key screenshots (prayer cards, prayer list, prayer time).

### 7. Batch Resize iPhone Screenshots

```bash
for f in screenshots/iphone/{light,dark}/*.png; do
  w=$(sips -g pixelWidth "$f" | awk '/pixelWidth/{print $2}')
  h=$(sips -g pixelHeight "$f" | awk '/pixelHeight/{print $2}')
  if [ "$w" = "1320" ] && [ "$h" = "2868" ]; then
    sips -z 2778 1284 "$f"
  elif [ "$w" = "2868" ] && [ "$h" = "1320" ]; then
    sips -z 1284 2778 "$f"
  fi
done
```

iPad screenshots (2064×2752) need no resize.

---

## Output Structure

```
screenshots/
  iphone/
    light/   02 through 11
    dark/    02, 04, 08, 10, 11
  ipad/
    light/   01 through 10
    dark/    02, 04, 08, 10
```

---

## Gotchas

| Issue | Workaround |
|-------|-----------|
| App Store Connect rejects 1320×2868 (6.9") | Resize to 1284×2778 (6.5") with `sips` |
| `xcrun simctl orientation` doesn't exist | Capture portrait, rotate with `sips -r 270` |
| Appium sessions drop frequently | Recreate session before each navigation burst |
| Onboarding popup only shows on fresh install | Uninstall + reinstall to retrigger |
| Prayer Time auto-rotates to landscape on iPhone | App setting "Landscape Mode" controls this |
| iPad screenshots accepted as-is | 2064×2752 matches the 13" category exactly |
| Seed data timestamps | Must be .NET ticks format, not Unix epoch |
| **Prayer Time action sheet auto-dismissed** | Prayer Time button shows a scope picker ("All Requests" / "By Tags" / "By Collection") when data has both tags and user collections. **`autoDismissAlerts: true` silently dismisses this**, making it look like the button does nothing. Use `autoDismissAlerts: false` for the entire session. |
| **Appium MCP screenshot too large** | `appium_screenshot` returns base64 that exceeds tool result token limits. Use `xcrun simctl io $UDID screenshot <path>` for all captures — faster and saves directly to disk. |
| **Exiting Prayer Time via Appium unreliable** | Landscape orientation confuses Appium element finding — buttons like "I'm Done" can't be located. Workaround: `xcrun simctl terminate` + `xcrun simctl launch` to restart the app on Home. |
| **Tag Detail requires two taps** | Tap the tag row to expand (shows Edit/Delete inline buttons), then tap Edit to navigate to the tag edit page with color picker swatches. |
| **Keyboard on Quick Add / Tag Edit** | Quick Add modal auto-focuses the title field, showing keyboard. `mobile: hideKeyboard` fails on Quick Add. On iPad, `hideKeyboard` works. For iPhone Quick Add, accept the keyboard in the screenshot or tap an empty area to dismiss. |
| **iOS notification-permission system alert blocks Settings flow on fresh install** | First tap into App Settings on a fresh-install session triggers iOS's "Prayer Would Like to Send You Notifications" system alert. The capture script must handle it via Appium `mobile: alert` `action=accept` (or dismiss) BEFORE proceeding to the `AppSettings_Btn_StageSamplePayload` row. Sometimes takes 1-2 attempts to register — wrap in a short retry. |

---

## Efficiency Tips for Future Passes

1. **Always use `autoDismissAlerts: false`** — prevents silent dismissal of action sheets and confirmation dialogs
2. **Use `xcrun simctl io` for screenshots, Appium only for navigation** — avoids token limit issues and saves directly to disk
3. **Terminate + relaunch to escape Prayer Time** — faster than finding exit buttons through Appium in landscape
4. **Do one device fully (light + dark) before moving to the next** — reduces session creation overhead
5. **Appium session creation per navigation burst** — sessions go stale; create fresh sessions after terminate/relaunch cycles
6. **Absolute paths for `xcrun simctl io screenshot`** — relative paths fail silently with "No such file or directory"
7. **Batch resize at the end** — resize all iPhone screenshots in one pass after all captures are done
