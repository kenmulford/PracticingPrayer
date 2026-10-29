# Changelog

Release notes for Practicing Prayer, newest first.

## 2.0.0 Data and domain

**Theme:** `PrayerKit` carries the 1.7.2 data and domain rules in Swift, with share, backup, and archive formats proven both ways against MAUI. Lands on `ios-2.0-refactor`, not a release.

### ✨ Features / enhancements

| Issue | PR | What |
|---|---|---|
| #382 Run the 1.7.2 schema upkeep in PrayerKit | #443 | `SchemaUpkeep` runs the 13 idempotent 1.7.2 upkeep steps, adds `PrayerRequest.NotifyDate`, and backfills it |
| #383 Read the existing preferences through AppPreferences | #443 | `AppPreferences` reads and writes the 1.7.2 `UserDefaults` keys in place |
| #384 Query cards grouped by collection in the 1.7.2 order | #443 | `cardList` returns sections in 1.7.2 order; a NULL `BoxId` groups as Loose Cards |
| #385 Create, edit, answer, and query prayers | #443 | `PrayerStore` with request-level tags, segments, search, and a by-card query |
| #386 Manage tags and user colors | #443 | `TagStore` and `UserColorStore`; duplicate names surface as typed errors |
| #387 Encode and decode share links and .prayercard files | #443 | `ShareCodec` reads and writes links and files MAUI produces, checked by `contracts/share` fixtures in both suites |
| #388 Encrypt and decrypt confidential backups | #443 | `confidential.enc` interoperates with MAUI; `contracts/backup` fixtures decrypt in both suites |
| #389 Create, edit, favorite, move, archive, and delete cards | #443 | `CardMutations`; a card delete removes its prayers, interactions, and tag rows in one transaction |
| #390 Record prayed interactions and compute the Home metrics | #443 | `recordPrayed` and `homeMetrics` (overdue, last prayed, answered on this day) |
| #391 Apply the protection policy to card and prayer queries | #443 | `ProtectionPolicy` masks Locked cards as "Protected" and drops Hidden and filtered protected cards while locked |
| #392 Schedule reminders and reconcile every launch | #443 | `ReminderScheduler` clears MAUI-era requests once, then rebuilds one calendar trigger per notifying prayer each launch |
| #393 Export and restore .pcrd archives compatibly with MAUI | #443 | `BackupArchive` writes and restores `.pcrd` files MAUI reads, rolling back on failure |
| #394 Create, rename, protect, and delete collections | #443 | `CollectionStore`; Delete All and unassign each run in one transaction |
| #395 Resolve an import payload against new or existing cards | #443 | `ImportResolver` detects duplicates and saves a new card and its prayers in one transaction |

### Consumer notes

- iOS work lands on `ios-2.0-refactor`. The MAUI app still ships iOS and Android from `dev`.
- **Schema change on the native side:** upkeep adds `PrayerRequest.NotifyDate` when absent, as 1.7.2 does.
- `contracts/backup/` is un-ignored in `.gitignore` past the `Backup*/` rule.

### ⚖️ Post-run audit trail

Judgment-call PRs: none.

- An automated security review of the milestone's commits found four issues, fixed in this PR: unscrubbed free pages in the backup copy, uncapped gzip and ZIP inflation, unvalidated archive structure, and Home queries that ignored lock state.
- MAUI `BackupService.BuildScrubbedDbBytesAsync` has the same free-page gap; it is unchanged here.
- Reminders show the prayer title for protected cards, as 1.7.2 does.

## 2.0.0 Spike - upgrade in place

**Theme:** The native iOS app opens the 1.7.2 database and Keychain PIN in place after an App Store update, proven on Test iPad. Lands on `ios-2.0-refactor`, not a release.

### ✨ Features / enhancements

| Issue | PR | What |
|---|---|---|
| #372 Scaffold the native iOS project and the PrayerKit package under ios/ | #442 | `ios/PrayerApp.xcodeproj` keeps the bundle ID and App Group, and `ios/PrayerKit` is a local Swift package for data and domain code |
| #373 Create the shared contracts/ fixture folder and move the pending-import golden into it | #442 | `contracts/` holds fixtures both apps read; the pending-import golden moved there |
| #374 Point the driver profile and standing docs at the native app | #442 | The driver profile and `.project/` docs cover `ios/**` and the Swift test suite |
| #375 Add a macOS swift test CI job with per-tree path filters | #442 | CI runs the PrayerKit suite on macOS when `ios/**` or `contracts/**` changes |
| #376 Capture the fresh-install and long-upgraded 1.7.2 database fixtures | #442 | `contracts/db/fresh-install.db` and `long-upgraded.db` (1.0.0 upgraded to 1.7.2) with pinned row counts |
| #377 Read and verify the existing Keychain PIN record natively | #442 | `PinHasher` and `PinRecord` read the MAUI Keychain item and verify PBKDF2-SHA256 hashes both apps produce |
| #378 Open the existing prayer_app.db in place with GRDB and a TicksDate codec | #442 | GRDB 7.11.1 opens the MAUI file in place; `TicksDate` reads .NET ticks stored as INTEGER or digit TEXT |
| #379 Map the seven tables to Codable records and prove round trips against both fixture DBs | #442 | Seven GRDB records round-trip both fixtures byte-exact, and the MAUI suite reads `native-written.db` |
| #380 Add local-only device backup read tests that skip when the backup is absent | #442 | `PP_DEVICE_BACKUP_DB` points both suites at a local backup, opened read-only; absent, the tests skip |
| #381 Pass the spike upgrade-in-place gate for data and PIN on Test iPad | #442 | `ios/scripts/upgrade-gate.sh` installs 1.7.2, seeds it, installs native over it, and checks counts and PIN. `LegacySceneDelegate` restores the 1.7.2 scene session, which left upgraded installs on a black screen |

### Consumer notes

- iOS work lands on `ios-2.0-refactor`. The MAUI app still ships iOS and Android from `dev`.
- **No schema changes** to the app database.

### ⚖️ Post-run audit trail

Judgment-call PRs: none.

- `long-upgraded.db` starts from 1.0.0 with a scene patch, because 1.0.0 does not launch on iOS 27 unpatched.
- The upgrade gate runs by hand on Test iPad; CI does not run it.

## v1.7.2 - Android Play Console fixes and iPad rotation

**Theme:** Android builds now shrink and optimize their code with R8. On Android 15 and later, Material Components no longer reach the deprecated bar-color APIs Play Console flags. Pages keep content clear of the system bars, keyboard, and display cutouts. Tablets request rotation and phones stay portrait.

| Issue | PR | What |
|---|---|---|
| #343 Android: enable R8 for Release builds | #366 | Release sets `AndroidLinkTool` to `r8` and `AndroidR8ObfuscationMode` to `private-members`, so the bundle ships R8 mapping and build-metadata files. Debug stays on D8. `Platforms/Android/proguard.cfg` stops R8 from optimizing away public fields .NET bindings read over JNI, and `scripts/check-r8-jni-members.cs` fails the release lane when a bound member is missing from the bundle's DEX |
| #358 Android: bump Material Components to 1.14 to drop deprecated setStatusBarColor calls | #366 | A direct Android-only reference pins `Xamarin.Google.Android.Material` 1.14.0.6 over MAUI's transitive 1.12.0.5, so `Window.setStatusBarColor` / `setNavigationBarColor` run only below API 35. The Kotlin wrapper packages (`Activity.Ktx` 1.13.0.1, `Collection.Ktx` 1.6.0.1, `Fragment.Ktx` 1.8.9.3, `Lifecycle.*` 2.11.0.1, `SavedState.SavedState.Ktx` 1.5.0.1) are pinned to the binding release of the AndroidX libraries they wrap, so restore has no NU1608 |
| #357 Android: handle edge-to-edge insets on Android 15+ (nav bar, keyboard, cutouts) | #366 | Eight pages set `SafeAreaEdges` with bottom `All` on Android so content clears the navigation bar and the keyboard: PrayerCardPage, PrayerDetailPage, BoxDetailPage, TagDetailPage, AppSettingsPage, ConfirmImportPage, PrayerListPage, and PrayerCardsPage. ConfirmImportPage and RestoreProgressPage gain Android-only values (`Container` on RestoreProgressPage); their iOS value stays `None`. The other pages keep their iOS value. Display cutouts are covered by PrayerTimePage's existing `Container`. The app sets Android's soft input mode to Resize (MAUI defaults it to Pan, which skips keyboard padding), so the keyboard shrinks these pages instead of covering them |
| #261 Idiom-aware orientation: tablets rotate, phones stay portrait | #366 | MainActivity no longer declares android:screenOrientation, which clears Play's Android 16 large-screen finding. At launch and when leaving Prayer Time, the policy requests rotation on tablets (Android smallest width 600dp or more, and iPad) and portrait on phones. Prayer Time still opens in landscape, and leaving it asks UIKit to re-read the allowed orientations so an iPad can rotate back. Tablet landscape uses the phone layouts. On iPad, the PIN and passphrase prompts move above the keyboard so their buttons stay reachable in landscape |
| #360 Android: card tap handler stacks per resume, so a Locked card opens several PIN popups | #366 | The native card and tag-swatch touch handlers attach once per native view (`NativeAttachGuard`), the HandlerChanged subscription is idempotent, concurrent `AuthenticateAsync` callers share one in-flight prompt, and every popup closes through `TryCloseAsync`, which logs the toolkit's `InvalidOperationException` instead of crashing the process |
| #361 Android: one-time and monthly reminders never post when exact alarms are not granted | #368 | Android reminders schedule with a 1-hour `AllowedDelay`. Without `SCHEDULE_EXACT_ALARM` (denied by default on Android 14+) the alarm fires about 2 minutes late, and the plugin's 1-minute default dropped it |
| #363 iPad: PIN and passphrase popups clip or over-grow while lifted above the keyboard | #368 | `KeyboardLiftBehavior` attaches to the Popup and sets the toolkit's `Popup.Margin`, so the panel moves. The lift is the keyboard's overlap with the window. Its keyboard observers live from `Loaded` to `Unloaded`, so closed popups no longer lift later ones |
| #364 Popup close results and gesture guards | #368 | A refused popup close skips its follow-up action and clears the popup's result. `PopupResultGate` ignores a second result tap while the close is in flight. The iOS tag-swatch long-press attaches once per native view |
| #365 E2E: Box detail back press only closes the keyboard on Android | #368 | `Boxes_ProtectAllCards_RadiosSelectAndPersist` closes the Android keyboard before the back press |
| #367 E2E: Cards_DecorativeElements_MarkedNotImportant depends on a collection being expanded | #368 | The accessibility check accepts the collection triangle expanded (▼) or collapsed (▷), so it no longer depends on collection state left by earlier tests |

### Consumer notes (upgrading from 1.7.1)

- Building any head requires workload set 10.0.402: run `dotnet workload restore` from the repo root.
- The orientation policy requests rotation on iPad outside Prayer Time and portrait on iPhone.
- **No schema changes** to the app database.

## v1.7.1 - Android dark-mode dialog crashes

**Theme:** On Android, the collection picker and the default reminder time picker no longer crash in dark mode.

| Issue | PR | What |
|---|---|---|
| #353 Android: Material dialogs crash in dark mode (collection picker, reminder time) | #354 | The app's `Maui.SplashTheme` overrides now inherit `Maui.MainTheme.NoActionBar` and set `maui_splash`, so MAUI swaps to its Material theme and Material dialogs resolve `colorSurface`. The dark-mode status bar stays #212121 |

### Consumer notes (upgrading from 1.7.0)

- Android only. iOS code is unchanged.
- In dark mode, App Settings rows now use the same spacing as light mode.
- **No schema changes** to the app database.

## v1.6.2 - Xcode 27 / iOS 27 SDK toolchain

**Theme:** The app builds and ships with Xcode 27 and the iOS 27 SDK on the .NET for iOS 27.0.10722 workload, keeping minimum iOS 16.0, and the five sheet-style pages now open as iOS page sheets on iPhone and iPad.

### ✨ Xcode 27 toolchain

| Issue | PR | What |
|---|---|---|
| #321 Pin .NET SDK 10.0.401 and workload set 10.0.401.1 for local and CI builds | #329 | `global.json` pins SDK 10.0.401 and `workloadVersion` 10.0.401.1; `dotnet workload restore` installs the set with no `--version` argument, locally and in CI |
| #322 Select the E2E iOS simulator by UDID and default to iPad (A16) on iOS 27.0 | #329 | `run-e2e-mac.sh` resolves one simulator by name + iOS version and drives it by UDID; it stops when none or several match, or when another simulator is booted. Defaults are iPad (A16) on iOS 27.0 |
| #323 Bump Microsoft.Maui.Controls to 10.0.110 | #329 | Controls 10.0.110 in the app and test projects; CommunityToolkit.Maui 14.0.1, Plugin.LocalNotification 14.0.0, and Oscore.Maui.Biometric 2.5.1 are unchanged |
| #324 Run the release iOS job on Xcode 27.0 | #329 | The release iOS job runs on the `xcode-27` image with Xcode 27.0 |
| #328 Present the five IPageSheetModal pages as iOS page sheets | #329 | Confirm import, the tag picker, and the three Prayer Time pages open as page sheets on iPhone and iPad (they presented full-screen before). Swipe down dismisses: a staged import is discarded, tags already added stay, and Prayer Time returns no result. Restore progress stays full-screen |
| #325 Build iOS with Xcode version validation on and pass the iOS 27 regression | #329 | iOS builds run with Xcode version validation on; the Debug simulator build and Release `ios-arm64` IPA build on Xcode 27.0 |
| #326 Update the project docs to the Xcode 27 / iOS 27 SDK toolchain | #329 | `CLAUDE.md` and `.project/` docs name the Xcode 27 / iOS 27 SDK build (minimum iOS 16.0) and the Controls 10.0.110 pin |
| #327 Bump the display version to 1.6.2 | #329 | `ApplicationDisplayVersion` is 1.6.2 |

### Consumer notes (upgrading from 1.6.1)

- Building iOS requires Xcode 27.0. Building any head requires .NET SDK 10.0.4xx and workload set 10.0.401.1: run `dotnet workload restore` from the repo root.
- Minimum iOS stays 16.0 and minimum Android stays API 24.
- E2E runs need exactly one booted simulator; `run-e2e-mac.sh` stops and names any other booted one.
- Android: Controls 10.0.110 applies a toolbar item's `SemanticProperties.Description`, so TalkBack announces the overflow button as "More actions" ("Cancel" in card multi-select), and UITests find it by that description.
- **No schema changes** to the app database.

### ⚖️ Post-run audit trail

Judgment-call PRs: none.

- The E2E gate passed on 2026-10-08 via `run-e2e-mac.sh` at the branch tip: Android on the `pp_api36` emulator (26 passed, 1 skipped) and iOS on the iPad (A16) iOS 27.0 simulator (21 passed, 6 skipped).
- Not verified on an iOS 27 device: the Share Extension and the `ImportTextIntent` shortcut. Not verified: App Store Connect accepting a build from the `xcode-27` image.

## v1.6.1 - Share and import fixes

**Theme:** The app can pick up a shared prayer from the clipboard after an install, and importing into an existing card skips requests already on it while the import card picker now masks protected cards when locked.

### ✨ Share and import

| Issue | PR | What |
|---|---|---|
| #311 Offer a clipboard import of a shared prayer on first launch | #315 | At the onboarding welcome step, when the clipboard holds text (Android) or a web link (iOS), the app asks "Did someone share a prayer with you?"; Import hands the share URL at the start of the clipboard text (cut at the first whitespace) to the share-link handler when it is a request or card link with a payload; anything else shows "No shared prayer found". A handed-off link skips the welcome popup, and the welcome popup gains the line "Got a shared prayer? Tap the link again to open it here." |
| #313 Skip requests already on the target card when importing to an existing card | #315 | Importing to an existing card lists requests whose titles already match (ignoring case, curly versus straight quotes, and repeated or edge whitespace) under "Already on this card"; "+ Add" moves one back to To import. When every request is already on the card, Save is disabled and the page says so |
| #314 Bump the display version to 1.6.1 | #315 | `ApplicationDisplayVersion` is 1.6.1 |

### 🔧 Fixes

| Issue | PR | What |
|---|---|---|
| #312 Mask protected cards in the import card picker while the session is locked | #315 | While the session is locked, the import card picker omits Hidden cards and shows LockedVisible cards as "Protected" with the lock glyph; tapping one authenticates first. In Quick Add the Quick Add card stays listed and preselected, shows as "Protected" when it is protected, and saves without unlocking |

### Consumer notes

- Requires Android 7.0 (API 24) or later, up from Android 5.0 (API 21): Google Play's automatic protection rejects a lower minimum. Existing installs on Android 5 and 6 stay on 1.6.0; new installs on those versions are no longer possible.
- The website's share page copies the share link to the clipboard when a store button is tapped. A share whose link would exceed 1800 characters goes out as a `.prayercard` file, carries no link, and does not survive an install.
- The prompt appears at most once per install (`ShareHandoffPrompted` preference). On Android any clipboard text triggers it, so an unrelated clip spends the one offer.
- Tapping Import reads the clipboard, which shows the OS paste notice (Android 12+) or the Allow Paste alert (iOS 16+).
- If the duplicate check fails, the page alerts and lists every request under To import.
- **No schema changes** to the app database.

### ⚖️ Post-run audit trail

Judgment-call PRs: none.

- No E2E test exercises the #311 import path on any platform; it is covered by unit tests with a mocked clipboard. The E2E gate passed on 2026-10-08 via `run-e2e-mac.sh`: Android on the `pp_api36` emulator (26 passed, 1 skipped) and iOS on the iPad (A16) iOS 26.5 simulator (21 passed, 6 skipped).
- Not verified on a real device: no paste notice before Import on Android 12+ or iOS, and whether the iOS web-link probe matches a copied multi-line share message.
