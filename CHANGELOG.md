# Changelog

Release notes for Practicing Prayer, newest first.

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
