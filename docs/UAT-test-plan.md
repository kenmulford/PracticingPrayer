# UAT Test Plan — Practicing Prayer

**Version:** 1.7.2
**Platforms:** iOS 27.0 on simulators `Dev iPhone` (iPhone 17 Pro Max) and `Dev iPad` (iPad Pro 13-inch (M5)); Android API 36 on emulator `pp_api36`. Minimum supported: iOS 16.0, Android API 24.
**Tester:** Manual
**Duration:** ~45-60 minutes for full pass

> Mark each item P (pass), F (fail + description), or S (skip + reason).
> Test on both platforms unless marked iOS-only or Android-only.

---

## 1. First Launch / Onboarding

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1.1 | Fresh install — app launches | Splash screen shows "Practicing Prayer" branding (not a square icon), then Home tab loads | |
| 1.2 | Welcome popup appears | "Welcome to Practicing Prayer" popup with "Get Started" and "Skip tour" buttons | |
| 1.3 | Tap "Get Started" | Navigates to Prayer Cards tab. Onboarding banner says "Tap 'Add Card' to create your first prayer card" | |
| 1.4 | Create first card | Tap "Add Card" → Card detail page. Type a name, tap Save. Returns to cards list. Banner updates to "Great! Now tap the name of your new card to expand it; then tap '+ Add prayer'" | |
| 1.5 | Add first prayer to card | Expand card → tap "+ Add prayer" → Prayer detail. Fill title, tap Save. Returns to card with prayer visible. | |
| 1.6 | Completion popup | After saving first prayer, "You're all set!" popup appears. Tap Done. | |
| 1.7 | Skip onboarding mid-flow | Fresh install → Welcome popup → "Skip tour". Verify no popup reappears, app is usable. | |
| 1.8 | Skip while editing | Start onboarding → create card → start adding prayer → dismiss onboarding banner. Verify unsaved work is NOT lost. | |

---

## 2. Home Tab

| # | Test | Expected | Result |
|---|------|----------|--------|
| 2.1 | Home loads with data | Shows Active Cards, Unanswered prayers, Last Prayed, and Overdue metrics. With nothing overdue, the Overdue tile reads "All requests have been recently prayed for." | |
| 2.2 | Quick Add button | Tap → modal appears with title entry. Type a title, tap Save. Toast "Prayer saved", modal dismisses. | |
| 2.3 | Quick Add → Cards tab | After Quick Add save, switch to Prayer Cards tab. "Quick Add" card count incremented. Prayer visible when expanded. | |
| 2.4 | Prayer Time — All Requests | Tap "Prayer Time" → action sheet lists "All Requests" and "Choose cards…", plus "By Tags" when tags exist and "By Collection" when a collection holds cards. Tap "All Requests". Session starts in landscape (App Settings → Landscape Mode on, the default). | |
| 2.5 | Prayer Time — By Tags | Tap "Prayer Time" → "By Tags". Tag scope page appears. Select tags, tap Start. | |
| 2.6 | Overdue card tap | If overdue prayers exist, tap "View in Prayers →" on the Overdue tile → navigates to Prayers tab with overdue filter. | |

---

## 3. Prayer Cards Tab

| # | Test | Expected | Result |
|---|------|----------|--------|
| 3.1 | Cards list loads | All cards visible with badge counts. Quick Add card at top. | |
| 3.2 | Search cards | Type in search bar → list filters by card title. Clear → all cards return. | |
| 3.3 | Search keyboard dismiss | Tap search → enter → keyboard dismisses. Tap background → keyboard dismisses. | |
| 3.4 | Create new card | Tap "More" → "Add Card" → detail page → type name → Save. Returns to list. New card auto-expands with gold highlight for ~2.5 seconds. | |
| 3.5 | Single-open accordion | Expand card A → expand card B. Card A auto-collapses. Only one card open at a time. | |
| 3.6 | Expand card → view prayers | Tap card name to expand. Prayers listed. Badge count matches visible prayer count. | |
| 3.7 | Add prayer to card | Expand card → tap "+ Add prayer ›" → prayer detail. Fill title, Save. Returns to card with prayer visible. Count updated. | |
| 3.8 | Save + (multiple prayers) | Add prayer to card → "Save +" in toolbar → form resets, toast shows `Saved "{title}"`. Add another. Back out. Both prayers visible on card. | |
| 3.9 | Edit prayer from card | Expand card → tap prayer → view mode. Tap "Edit" → edit mode. Change title → Save. Returns to card with updated title. | |
| 3.10 | Move prayer to different card | Edit prayer → change "Belongs to Card" picker → Save. Prayer removed from old card, appears in new card. Both counts update. | |
| 3.11 | Delete prayer from card | Expand card → tap prayer → "Edit" → "Delete" → confirm. Prayer removed from card, count decremented. | |
| 3.12 | Delete card | Expand card → tap the "Delete" chip (or "Edit" → "Delete" on card detail). Confirmation shows accurate prayer count. Confirm → card removed from list. | |
| 3.13 | Delete card (collapsed) | Delete a card WITHOUT expanding it first. Confirmation should still show correct prayer count (not 0). | |
| 3.14 | Favorite card | Expand card → tap the ★ Favorite chip. Star appears on the card. Card sorts to top. | |
| 3.15 | System card protection | "Quick Add" card cannot be deleted (Delete button hidden). | |

---

## 4. Prayers Tab

| # | Test | Expected | Result |
|---|------|----------|--------|
| 4.1 | Prayer list loads | All prayers visible with card title, status indicators. | |
| 4.2 | Search prayers | Type in search bar → filters by prayer title. | |
| 4.3 | Filter: Active/Answered/All | Tap each filter button. List updates. Count announced for screen readers. | |
| 4.4 | Filter by tag | If tags exist, tag chips appear. Tap a tag → list filters to prayers with that tag. | |
| 4.5 | Add new prayer | Tap "More" → "Add" → prayer detail in edit mode. Fill title, select card, Save. Returns to list with new prayer. | |
| 4.6 | View prayer (read-only) | Tap a prayer → view mode. Title, details, card, tags, reminders all displayed. | |
| 4.7 | Edit prayer | View mode → tap "Edit" → edit mode. Change details → Save. | |
| 4.8 | Mark answered | View mode → tap "Mark Answered" → prayer moves to Answered status. Strikethrough on title. | |
| 4.9 | Delete prayer | Edit mode → Delete button → confirmation → prayer removed from list. | |
| 4.10 | Cross-tab freshness | Add prayer via Quick Add on Home → switch to Prayers tab. New prayer appears without restart. | |

---

## 5. Prayer Detail — Unsaved Changes Guard

| # | Test | Expected | Result |
|---|------|----------|--------|
| 5.1 | Edit title → back button | Change title text → tap back. "Discard changes?" dialog appears. Tap "Cancel" → stays on page. Tap "Discard" → navigates back. | |
| 5.2 | Edit title → tap different tab | Change title → tap Home tab. "Discard changes?" dialog appears. | |
| 5.3 | Change only notification time → back | Enable reminders, change time only (not title/details) → back. "Discard changes?" dialog appears. | |
| 5.4 | Change only day of week → back | Set weekly frequency, change day → back. "Discard changes?" dialog appears. | |
| 5.5 | Save then back | Edit → Save → back. NO discard prompt (changes already saved). | |
| 5.6 | New prayer, no changes → back | Navigate to new prayer form, don't type anything → back. NO discard prompt. | |

---

## 6. Reminders / Notifications

| # | Test | Expected | Result |
|---|------|----------|--------|
| 6.1 | Enable reminders | Edit prayer → toggle Reminders on. Frequency and Reminder Time appear; Reminder Date appears for OneTime and Yearly, Day of Week for Weekly, Day of Month for Monthly. | |
| 6.2 | Frequency picker populated | Tap Frequency → options appear (OneTime, Daily, Weekly, Monthly, Yearly). | |
| 6.3 | Time picker readable (iOS) | Time picker shows "9:00 AM" (not garbled characters like "ΗΘδ"). | |
| 6.4 | Save with reminders → notification fires | Set a daily reminder for 1 minute from now. Save. Wait. Notification appears. | |
| 6.5 | Tap notification → Prayer Time | Tap the notification. App opens to Prayer Time with the notified prayer. | |
| 6.6 | Disable reminders | Edit prayer → toggle Reminders off → Save. No more notifications for this prayer. | |

---

## 7. Tags

| # | Test | Expected | Result |
|---|------|----------|--------|
| 7.1 | Tags list loads | All tags visible with color swatches. | |
| 7.2 | Create tag | Tags tab → tap "Add" → tag detail. Type name, select color, Save. Tag appears in list. | |
| 7.3 | Edit tag | Tap the tag's "Edit" chip → detail page. Change name/color → Save. | |
| 7.4 | Delete tag | Tap the tag's "Delete" chip. Tag removed. Prayers that had this tag lose the assignment. | |
| 7.5 | Custom color picker | Tag detail → tap "+" circle → color picker popup. Drag hue/saturation. Hex entry works. "Add" saves custom color. | |
| 7.6 | View prayers by tag | Tag detail → "View Prayers with this Tag" button → Prayers tab filtered to that tag. | |
| 7.7 | Add tag to prayer | Prayer detail edit mode → type in tag search → suggestions appear → tap to add. Chip appears. | |
| 7.8 | Remove tag from prayer | Prayer detail → tap "×" on tag chip. Tag removed. | |
| 7.9 | Color swatch long-press delete | Tag detail → long-press a custom color swatch → deleted from palette. | |

---

## 8. Prayer Time

| # | Test | Expected | Result |
|---|------|----------|--------|
| 8.1 | Session starts in landscape | Screen rotates to landscape. First prayer displayed. Progress "1 of N". | |
| 8.2 | Swipe/arrow navigation | Swipe left or tap → next prayer. Swipe right or tap ← previous. | |
| 8.3 | Auto-mode | Tap timer display → cycles through 30s/60s/120s intervals. Tap pause to stop. | |
| 8.4 | Session completion | Advance past last prayer → "You've prayed through all your requests!" message with a "Finish" button. | |
| 8.5 | "I'm done" button | Tap → exits prayer time, returns to portrait, navigates back. | |
| 8.6 | Tag-scoped session | Home → Prayer Time → By Tags → select 1+ tags → Start. Only tagged prayers shown. | |
| 8.7 | Background/resume | During prayer time, background the app. Return. Timer pauses/resumes correctly. | |

---

## 9. Settings Hub

| # | Test | Expected | Result |
|---|------|----------|--------|
| 9.1 | Hub page loads | 5 rows: App Settings, Manage Collections, Backup & Restore, About, Help. Each tappable with chevron. | |
| 9.2 | App Settings | Tap → Allow Notifications toggle, Default Reminder Time, "Overdue after (days)", Prayer Time Landscape Mode toggle. Changes persist after leaving and returning. | |
| 9.3 | Backup & Restore — Export | Tap "Back Up Now" → share sheet with .pcrd file. | |
| 9.4 | Backup & Restore — Restore | Tap "Restore from Backup" → pick .pcrd file → confirmation dialog → progress spinner → "Restore complete" toast. All data immediately reflects restored content. | |
| 9.5 | About — Send Diagnostic Info | Visible only if a diagnostic log exists. Tap → share sheet. | |
| 9.6 | About page | Shows "Practicing Prayer", version + build number, app description. Privacy Policy and Visit Website links open browser. | |
| 9.7 | Help page | 13 FAQ items. Tap question → answer expands. Tap again → collapses. Multiple can be open. | |
| 9.8 | Back navigation | Each sub-page → back button returns to hub. | |

---

## 10. Dark Mode

| # | Test | Expected | Result |
|---|------|----------|--------|
| 10.1 | Switch to dark mode | System Settings → dark mode. App renders with dark backgrounds, light text. | |
| 10.2 | All pages readable | Navigate through every tab and sub-page. No white-on-white or black-on-black text. | |
| 10.3 | Cards border visible | Prayer card borders visible in both light and dark mode. | |
| 10.4 | Settings hub readable | All 5 rows have proper contrast in dark mode. | |
| 10.5 | Prayer Time gradient | Bottom fade gradient matches page background in both modes. | |

---

## 11. Accessibility (VoiceOver / TalkBack)

| # | Test | Expected | Result |
|---|------|----------|--------|
| 11.1 | Home — heading navigation | VoiceOver rotor → headings. Page title appears as Level1. | |
| 11.2 | Cards — expand announcement | Expand a card. Screen reader announces "Expanded {card name}". | |
| 11.3 | Cards — search announcement | Type in search. After brief pause, "Showing N cards" announced. | |
| 11.4 | Settings hub — row hints | Each row announces "Double tap to open". | |
| 11.5 | Prayer detail — Save+ | "Save +" toolbar item reads "Save and add another prayer". | |
| 11.6 | Tag detail — color swatches | Swatch announces hex color. "Double tap to select, long press to delete". | |
| 11.7 | Help FAQ — expand | Tap question. Answer text announced by screen reader. | |
| 11.8 | Prayer Time — completion | "You've prayed through all your requests!" is a heading landmark. | |
| 11.9 | Decorative elements hidden | Divider lines, chevrons, gradient overlays not read by screen reader. | |

---

## 12. Edge Cases

| # | Test | Expected | Result |
|---|------|----------|--------|
| 12.1 | 0 cards | Delete all cards. Cards page shows empty state (just onboarding banner or blank). | |
| 12.2 | 0 prayers on a card | Expand a card with no prayers. "No prayers" or empty accordion body. | |
| 12.3 | Very long prayer title | Create prayer with 200+ character title. Text truncates properly, doesn't break layout. | |
| 12.4 | Rapid tab switching | Switch between all 5 tabs quickly. No crash, no stale data. | |
| 12.5 | Double-tap Save+ | Tap "Save +" rapidly. Only one prayer saved (guard prevents double-save). | |
| 12.6 | Background during restore | Start restore → background app → return. Restore completes or shows error gracefully. | |
| 12.7 | Invalid backup file | Settings → Restore → pick a non-.pcrd file. "Invalid Backup" error, no crash. | |

---

## 13. iOS-Specific

| # | Test | Expected | Result |
|---|------|----------|--------|
| 13.1 | Home screen icon | Shows "Prayer" (not truncated). | |
| 13.2 | Safe area (notch/Dynamic Island) | Content doesn't render under the notch on any page. | |
| 13.3 | Keyboard dismiss | All input pages: tap outside field → keyboard dismisses. | |
| 13.4 | TimePicker/DatePicker font | Shows "9:00 AM" not garbled symbols. Uses system font. | |
| 13.5 | Swipe-to-dismiss modal | QuickAdd modal → swipe down to dismiss. No crash. | |

---

## 14. Android-Specific

| # | Test | Expected | Result |
|---|------|----------|--------|
| 14.1 | Hardware back button | On every page: back button navigates correctly. On edit pages: shows discard prompt if dirty. | |
| 14.2 | Notification channel | First notification → "Prayer Reminders" channel visible in system settings. | |
| 14.3 | Edge-to-edge display | Content respects safe areas on notched Android devices. | |

---

## Sign-off

| Platform | Tester | Date | Build | Pass/Fail |
|----------|--------|------|-------|-----------|
| iOS | | | | |
| Android | | | | |
