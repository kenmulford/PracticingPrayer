---
name: prayer-app-accessibility
description: >
  Use when adding or modifying accessibility support in PrayerApp — screen reader announcements,
  SemanticProperties, AutomationProperties visibility control, IAccessibilityService, heading
  levels, AutomationId naming, suppress-on-load guards, or dynamic Description bindings.
  Covers TalkBack (Android) and VoiceOver (iOS).
keywords: [SemanticProperties, AutomationProperties, IsInAccessibleTree, IAccessibilityService, screen reader, VoiceOver, TalkBack, Announce, AutomationId, accessibility]
---

# PrayerApp Accessibility

## When to Use

Invoke this skill when:
- Adding `SemanticProperties` or `AutomationId` to any XAML element
- Calling `_accessibilityService.Announce()` or `SemanticScreenReader.Announce()`
- Implementing a debounced filter announcement or suppress-on-load guard
- Adding heading structure, accordion headers, or dynamic description bindings
- Writing dashboard metric tiles or FAQ expanders

---

## Quick Reference

| Need | API |
|------|-----|
| Suppress decorative sub-element | `AutomationProperties.IsInAccessibleTree="False"` |
| Section header accessible label | `SemanticProperties.Description="{Binding Name}"` |
| Section header hint | `SemanticProperties.Hint="Double tap to expand or collapse section"` |
| Dynamic description (computed) | `SemanticProperties.Description="{Binding SomeAccessibleProperty}"` |
| ViewModel announcement | `_accessibilityService.Announce("message")` |
| Code-behind announcement | `SemanticScreenReader.Announce("message")` |
| Debounced filter count | `AnnounceFilterCountDebounced(count)` — 400 ms delay |
| Suppress on initial load | `_suppressAnnounce` / `_suppressFilterAnnounce` guard |
| Interactive element id | `AutomationId="{Page}_{Type}_{Name}"` |

---

## AutomationId Naming Convention

Pattern: `{Page}_{Type}_{Name}`

| Page | Type | Name | Example |
|------|------|------|---------|
| Home | Btn | QuickAdd | `Home_Btn_QuickAdd` |
| Home | Metric | Cards | `Home_Metric_Cards` |
| TagPicker | Entry | Search | `TagPicker_Entry_Search` |
| Cards | Section | Header | `Cards_Section_Header` |
| Detail | Btn | MarkAnswered | `Detail_Btn_MarkAnswered` |
| PrayerTime | Btn | Pause | `PrayerTime_Btn_Pause` |

**Types used:** Btn, Entry, Picker, List, Metric, Lbl, Label, Radio, Switch, Row, Filter, Seg, Card, Section, Banner

---

## SemanticProperties in XAML

### Hint — describes what an element does
```xml
<Button Text="Save"
        SemanticProperties.Hint="Save the current prayer card" />
```

### Description — alternative text for non-text or composite elements
```xml
<!-- Static description on an image -->
<Image Source="star.png"
       SemanticProperties.Description="Favorite indicator" />

<!-- Dynamic computed description (e.g. dashboard tile) -->
<Border SemanticProperties.Description="{Binding ActiveCardsAccessible}" />
```

### HeadingLevel — page and section structure
```xml
<Label Text="Prayer Cards"
       Style="{StaticResource Headline}"
       SemanticProperties.HeadingLevel="Level1" />
```

Heading levels used:
- `Level1` — page titles (Headline style)
- `Level2` — section headings (SectionHeading style)

---

## AutomationProperties.IsInAccessibleTree

Set to `"False"` on any decorative sub-element inside a container that already
exposes a composite description. This prevents TalkBack/VoiceOver from reading
the inner elements individually (90+ usages in XAML).

```xml
<!-- Decorative triangle icon inside an accessible section header -->
<Label Text="{Binding IsExpanded, Converter={StaticResource BoolToTriangle}}"
       AutomationProperties.IsInAccessibleTree="False" />
```

Common locations: `PrayerCardsPage.xaml`, `MainPage.xaml`, `BoxesPage.xaml`,
`SettingsHubPage.xaml`, and named styles in `Styles.xaml`.

**Rule:** if the parent `Border` or container already has `SemanticProperties.Description`,
set `AutomationProperties.IsInAccessibleTree="False"` on every non-interactive child inside it.

---

## IAccessibilityService

Interface: `PrayerApp/Services/IAccessibilityService.cs`
Implementation: `PrayerApp/Services/MauiAccessibilityService.cs`

```csharp
public interface IAccessibilityService
{
    void Announce(string message);       // Screen reader announcement
    void NotifyLayoutChanged();          // Tell screen reader layout updated
}
```

**Rule:** ViewModels always use `_accessibilityService` (injected). Use `SemanticScreenReader`
directly only in code-behind for view-level events (e.g., accordion tap handlers in `PrayerCardsPage.xaml.cs`).

### ViewModel usage examples
```csharp
// PrayerRequestDetailViewModel: mark answered
_accessibilityService.Announce("Prayer marked as answered");

// PrayerTimeViewModel: navigation
_accessibilityService.Announce($"Prayer {ProgressDisplay}: {entry.PrayerTitle}");

// PrayerTimeViewModel: session complete
_accessibilityService.Announce("Prayer session complete");
```

### Code-behind usage (view-level events only)
```csharp
// PrayerCardsPage.xaml.cs — accordion tap handler
SemanticScreenReader.Announce(section.IsExpanded
    ? $"Expanded {section.Name}"
    : $"Collapsed {section.Name}");
```

---

## Accordion Section Headers

Actual pattern from the `GroupHeaderTemplate` in `PrayerCardsPage.xaml`:

```xml
<VerticalStackLayout
    AutomationId="Cards_Section_Header"
    SemanticProperties.Description="{Binding Name}"
    SemanticProperties.Hint="Double tap to expand or collapse section">
    <!-- Decorative triangle — suppress from tree -->
    <Label Text="{Binding IsExpanded, Converter={StaticResource BoolToTriangle}}"
           AutomationProperties.IsInAccessibleTree="False" />
    ...
</VerticalStackLayout>
```

Note: `ExpandCollapseHint` does not exist. The hint is a static string, not a binding.

---

## Debounced Announcements + Suppress-On-Load Guards

### Debounce pattern (both PrayerCardsViewModel and PrayerListViewModel)
```csharp
private CancellationTokenSource? _filterAnnounceCts;

private void AnnounceFilterCountDebounced(int count)
{
    _filterAnnounceCts?.Cancel();
    _filterAnnounceCts?.Dispose();
    _filterAnnounceCts = new CancellationTokenSource();
    var token = _filterAnnounceCts.Token;
    Task.Delay(400, token).ContinueWith(_ =>
    {
        if (!token.IsCancellationRequested)
            _accessibilityService.Announce($"Showing {count} cards");
    }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
}
```

- `PrayerCardsViewModel`: announces `"Showing {count} cards"`
- `PrayerListViewModel`: announces `"Showing {count} prayers"`

**Why 400 ms?** Fast enough to feel responsive, slow enough to avoid overlapping keystrokes.

### Suppress-on-load guards

Both VMs declare a bool that is `true` until the first sync completes, preventing
a triple announcement ("Loading" + "Content loaded" + "Showing N cards") on cold open:

```csharp
// PrayerCardsViewModel.cs
private bool _suppressFilterAnnounce;

// PrayerListViewModel.cs
private bool _suppressAnnounce;
```

Guard usage pattern:
```csharp
if (!_suppressFilterAnnounce)
    AnnounceFilterCountDebounced(filteredCount);
```

Always add a suppress guard when calling a debounced announcement from a path
that runs on initial data load.

---

## Dynamic Description Bindings

Use a computed ViewModel property when the accessible description depends on state.

### PrayerTime pause button (`PrayerTimeViewModel.PauseButtonDescription`, `PrayerTimePage.xaml`)
```csharp
public string PauseButtonDescription => IsPaused ? "Resume auto-advance" : "Pause auto-advance";
```
```xml
<Button AutomationId="PrayerTime_Btn_Pause"
        SemanticProperties.Description="{Binding PauseButtonDescription}"
        Command="{Binding TogglePauseCommand}" />
```

### Home dashboard metrics (`HomeViewModel.cs`, `MainPage.xaml`)
```csharp
public string ActiveCardsAccessible => HasActiveCards
    ? $"Active cards, {ActiveCardCount}. Tap to view prayer cards."
    : "No active cards. Tap to create your first card.";
public string UnansweredAccessible => ...;
public string LastPrayedAccessible => ...;
public string OverdueAccessible => ...;
public string AnsweredOnThisDateAccessible => HasAnsweredOnThisDate
    ? $"{AnsweredOnThisDateHeader}: {string.Join(", ", AnsweredOnThisDate.Select(p => p.Title))}"
    : string.Empty;
```

Each tile binds its `SemanticProperties.Description` to the matching `*Accessible` property.

---

## AccessibleSummary Pattern

ViewModels compose a single string for complex card rows so TalkBack reads one
announcement instead of each label separately.

```csharp
// PrayerRequestDetailViewModel.cs
public string AccessibleSummary
{
    get
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(CardTitle)) parts.Add(CardTitle);
        parts.Add(Title);
        if (IsAnswered && !string.IsNullOrEmpty(AnsweredAtDisplay))
            parts.Add(AnsweredAtDisplay);
        return string.Join(", ", parts);
    }
}
```

Bound in XAML on the row container (`PrayerListPage.xaml` item template):
```xml
<Grid SemanticProperties.Description="{Binding AccessibleSummary}"
      SemanticProperties.Hint="Double tap to view details">
    <!-- Visual sub-elements use IsInAccessibleTree="False" -->
</Grid>
```

---

## Multi-Select Mode Announcements

`PrayerCardsViewModel.cs` multi-select commands:

```csharp
// Entering multi-select (toolbar or long-press)
_accessibilityService.Announce("Selection mode. Tap cards to select them.");

// Each tap toggles + announces current count
_accessibilityService.Announce(SelectedCountText);
// SelectedCountText: "None selected" / "1 selected" / "N selected"

// Cancelling selection
_accessibilityService.Announce("Selection cancelled");

// After move completes (archive target announces "Archived N card(s)" instead)
_accessibilityService.Announce($"Moved {selected.Count} card{(selected.Count == 1 ? "" : "s")} to {result}");
```

---

## PrayerTime Session Announcements

All issued via `_accessibilityService` in `PrayerTimeViewModel.cs`:

| Event | Announcement |
|-------|-------------|
| Navigate to prayer | `$"Prayer {ProgressDisplay}: {entry.PrayerTitle}"` |
| Session complete | `"Prayer session complete"` |
| Mark answered | `$"{entry.PrayerTitle} marked as answered"` |
| Interval changed | `$"Interval set to {IntervalLabel(SelectedIntervalSeconds)}"` |
| Auto-advance started | `"Auto-advance started"` |
| Resumed | `"Resumed"` |
| Paused | `"Paused"` |

---

## FAQ Expander

`FaqItemViewModel` constructor — reads full answer aloud on expand:

```csharp
ToggleCommand = new RelayCommand(() =>
{
    IsExpanded = !IsExpanded;
    if (IsExpanded)
        accessibilityService?.Announce(Answer);
});
```

Pass `IAccessibilityService` as a constructor parameter when creating `FaqItemViewModel` instances.

---

## Common Mistakes

1. **Missing `IsInAccessibleTree="False"` on decorative children.** Any visual-only element
   inside a container that already exposes `SemanticProperties.Description` must be suppressed,
   or screen readers will read it twice.

2. **No suppress-on-load guard on debounced announcements.** Cold-load paths call the same
   filter method as interactive filter changes. Always check `_suppressAnnounce` /
   `_suppressFilterAnnounce` before calling `AnnounceFilterCountDebounced`.

3. **Using `SemanticScreenReader` in a ViewModel.** ViewModels use `_accessibilityService`
   (mockable, testable). `SemanticScreenReader` is for code-behind only (e.g., `PrayerCardsPage.xaml.cs`).

4. **Binding `SemanticProperties.Hint` to a non-existent VM property.** `ExpandCollapseHint`
   does not exist. Section header hints are static strings.

5. **Forgetting to raise `OnPropertyChanged` for `*Accessible` properties.** Each computed
   accessible string must be raised whenever its underlying data changes (see `HomeViewModel`
   pattern for all five dashboard metrics).

6. **Setting `SemanticProperties.Description` on a text `Label`.** The Label's `Text` is already
   announced verbatim by TalkBack/VoiceOver, so Description double-announces — and on Android it
   overwrites `content-desc`, the same attribute Appium reads for `AutomationId` locators,
   silently breaking any test that locates that Label. Reserve `Description` for non-text elements
   (images, icons) and composite containers; never on a `<Label>` that already has a `Text` binding.

---

## Checklist: Making a New Page Accessible

1. Set `AutomationId` on all interactive elements using `{Page}_{Type}_{Name}` convention
2. Add `SemanticProperties.Hint` to buttons and interactive controls
3. Add `SemanticProperties.HeadingLevel` to page titles (Level1) and section headers (Level2)
4. Add `SemanticProperties.Description` (static or bound) to images and non-text containers
5. Set `AutomationProperties.IsInAccessibleTree="False"` on decorative children of described containers
6. Create `AccessibleSummary` (or equivalent computed property) on ViewModel for complex card rows
7. Use `_accessibilityService.Announce()` for state changes not visible in layout
8. Add `_suppressAnnounce` guard if announcement path also runs during initial data load
9. Use debounced announcements for rapid filter changes
10. Test with TalkBack (Android) or VoiceOver (iOS)
