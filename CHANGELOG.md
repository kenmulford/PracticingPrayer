# Changelog

Release notes for Practicing Prayer, newest first.

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

- Requires Android 7.0 (API 24) or later, up from Android 5.0 (API 21): Google Play's automatic protection rejects a lower minimum. Devices on Android 5 and 6 stay on 1.6.0.
- The website's share page copies the share link to the clipboard when a store button is tapped. A share whose link would exceed 1800 characters goes out as a `.prayercard` file, carries no link, and does not survive an install.
- The prompt appears at most once per install (`ShareHandoffPrompted` preference). On Android any clipboard text triggers it, so an unrelated clip spends the one offer.
- Tapping Import reads the clipboard, which shows the OS paste notice (Android 12+) or the Allow Paste alert (iOS 16+).
- If the duplicate check fails, the page alerts and lists every request under To import.
- **No schema changes** to the app database.

### ⚖️ Post-run audit trail

Judgment-call PRs: none.

- No E2E test exercises the #311 import path on any platform; it is covered by unit tests with a mocked clipboard. The E2E gate passed on Android (26 passed, 1 skipped) and iOS (21 passed, 6 skipped).
- Not verified on a real device: no paste notice before Import on Android 12+ or iOS, and whether the iOS web-link probe matches a copied multi-line share message.
