---
name: prayer-app-views
description: >
  Use when creating or modifying PrayerApp pages, XAML layouts, code-behind lifecycle, popups,
  modals, CollectionView patterns, dynamic toolbars, or form layouts. Covers PageSync OnAppearing
  pattern, inline expanded-subtree IsVisible realization (shape (i)), Android native gesture detector,
  CardsOverflowPopup detached resource tree, SafeAreaEdges, compiled bindings, AppThemeBinding,
  BindableLayout, SwipeView (not used in TagsPage), CarouselView, and ScrollTo drain pattern.
cso_keywords:
  - PageSync
  - OnAppearing
  - CollectionView
  - SwipeView
  - modal
  - Popup
  - SafeAreaEdges
  - AppThemeBinding
  - BindableLayout
  - compiled bindings
  - OnCardBorderLoaded
  - Rebind
  - CardsOverflowPopup
  - DrainLayoutPassAsync
  - ScrollTo
  - GroupFooterTemplate
---

# PrayerApp View Patterns

All views live in `PrayerApp/Views/` organized by feature folder.

---

## When to Use

Invoke this skill before:
- Creating or modifying any `.xaml` page or popup
- Wiring up `OnAppearing` lifecycle
- Adding a `CollectionView` (grouped or flat)
- Implementing a dynamic toolbar item
- Showing a CT.Maui Popup
- Debugging cascade layout invalidations or `ScrollTo` crashes on Android

---

## Quick Reference

| Page | Layout type | Key patterns |
|------|-------------|--------------|
| `PrayerCardsPage` | Grouped CollectionView | PageSync, inline expanded subtree (`IsVisible="{Binding IsExpanded}"`, shape (i) #42), Margin via `IsExpanded` DataTrigger, Android CardGestureListener, single overflow ToolbarItem, GroupFooterTemplate HeightRequest="0", keyboard-aware SafeAreaEdges (see below) |
| `TagsPage` | Flat CollectionView + inline action chips | PageSync, inline action Grid (no SwipeView), SafeAreaEdges="Container" |
| `BoxesPage` | Flat CollectionView + inline action chips | PageSync, SafeAreaEdges="Container" |
| `PrayerTimePage` | CarouselView | Two-way Position binding |
| `MainPage` | Grid of metric cards | TapGestureRecognizer commands |
| `CardsOverflowPopup` | CT.Maui Popup | Explicit Colors.xaml merge, PopupOptions { Shape = null } |
| `TagPickerPage` | Modal CollectionView | TaskCompletionSource WaitForDismissAsync, code-behind Tapped handler |
| `ConfirmImportPage` | Modal form (import and Quick Add) | `PushModalWithNavigationBarAsync` so its Cancel/Save ToolbarItems render |

---

## View Inventory

| Folder | Page/Popup | ViewModel | Layout |
|--------|------------|-----------|--------|
| PrayerCard/ | PrayerCardsPage | PrayerCardsViewModel | Grouped CollectionView |
| PrayerCard/ | PrayerCardPage | PrayerCardViewModel | VerticalStackLayout form |
| PrayerCard/ | CardsOverflowPopup | PrayerCardsViewModel | CT.Maui Popup |
| Prayer/ | PrayerListPage | PrayerListViewModel | Filtered CollectionView |
| Prayer/ | PrayerDetailPage | PrayerRequestDetailViewModel | Complex form |
| Prayer/ | PrayersOverflowPopup | PrayerListViewModel | CT.Maui Popup |
| PrayerTime/ | PrayerTimePage | PrayerTimeViewModel | CarouselView |
| PrayerTime/ | PrayerTimeScopePage | PrayerTimeScopeViewModel | Modal tag selector |
| PrayerTime/ | PrayerTimeBoxScopePage | PrayerTimeBoxScopeViewModel | Modal collection selector |
| PrayerTime/ | PrayerTimeCardSelectPage | PrayerTimeCardSelectViewModel | Modal card multi-select |
| Tags/ | TagsPage | TagsViewModel | Flat CollectionView + inline action chips |
| Tags/ | TagDetailPage | TagDetailViewModel | Form + color swatches |
| Tags/ | TagPickerPage | TagPickerViewModel | Modal tag picker |
| Tags/ | ColorPickerPopup | — | CT.Maui Popup color picker |
| Boxes/ | BoxesPage | BoxesViewModel | Flat CollectionView + inline action chips |
| Boxes/ | BoxDetailPage | BoxDetailViewModel | Form |
| Settings/ | SettingsHubPage | — | Tab navigation |
| Settings/ | AppSettingsPage | — | Toggles/pickers |
| Settings/ | BackupPage | — | Backup UI |
| Settings/ | HelpPage | HelpViewModel | FAQ accordion |
| Settings/ | AboutPage | — | Static info |
| Backup/ | RestoreProgressPage | — | Progress UI |
| Onboarding/ | OnboardingBanner | — | Tutorial banner |
| Onboarding/ | OnboardingWelcomePopup | — | CT.Maui Popup |
| Onboarding/ | OnboardingCompletePopup | — | CT.Maui Popup |
| Confidential/ | PinPromptPopup | — | CT.Maui Popup |
| Confidential/ | PassphrasePromptPopup | — | CT.Maui Popup |
| Shared/ | LoadingOverlay | — | Overlay control |
| Shared/ | StyledPicker | — | Picker control (`ContentView`) |
| Root | MainPage | HomeViewModel | Metric cards grid |
| Root | ConfirmImportPage | ConfirmImportViewModel | Modal import / Quick Add form |

---

## XAML Page Template

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:viewModels="clr-namespace:PrayerApp.ViewModels"
             x:Class="PrayerApp.Views.MyFeature.MyPage"
             x:DataType="viewModels:MyViewModel"
             Title="My Page"
             SafeAreaEdges="Container">

    <!-- x:DataType enables compiled bindings — REQUIRED on every page -->
    <!-- SafeAreaEdges="Container" is used on TagsPage, BoxesPage. Pages with an Entry use the Android keyboard-aware value (see SafeAreaEdges section) -->

    <ScrollView>
        <VerticalStackLayout Padding="20" Spacing="16">
            <!-- Content -->
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

---

## Code-Behind OnAppearing — PageSync Pattern

`PageSync` (`Helpers/PageSync.cs`) replaces the old per-page `_loaded` flag and
`LoadAsync`/`RefreshAsync` branch. Use it on every list page whose ViewModel
implements `ISyncableViewModel`.

```csharp
// Helpers/PageSync.cs — shared pipeline
public static class PageSync
{
    public static async Task OnAppearingAsync(ISyncableViewModel vm)
    {
        await App.InitTask;   // Wait for DB seeding
        await vm.SyncAsync(); // Single call: first visit = load, return = refresh
    }
}

// Code-behind usage (TagsPage, BoxesPage, PrayerCardsPage, etc.)
protected override async void OnAppearing()
{
    base.OnAppearing();
    await PageSync.OnAppearingAsync(_vm);
}
```

**Why:** Eliminates duplicate `_loaded` bool on every page and centralizes the
`App.InitTask` guard. `SyncAsync` on the ViewModel decides whether to do a full
load or a delta refresh — the view doesn't need to know.

---

## Compiled Bindings (x:DataType)

Every page and DataTemplate must specify `x:DataType` for compile-time binding checks:

```xml
<!-- Page level -->
<ContentPage x:DataType="viewModels:PrayerCardsViewModel">

<!-- DataTemplate level (resets context) -->
<DataTemplate x:DataType="viewModels:PrayerCardViewModel">
    <Border Style="{StaticResource PrayerCardBorder}">
        <Label Text="{Binding Title}" />
    </Border>
</DataTemplate>
```

---

## Layout Patterns

### Form Layout (Label-Above-Field)
```xml
<VerticalStackLayout Spacing="4">
    <Label Text="Title" Style="{StaticResource FormLabel}" />
    <Entry Text="{Binding Title}" Placeholder="Enter title" />
</VerticalStackLayout>
```
**Rule:** Always label above field, both full-width. Never side-by-side.

### Grouped CollectionView (PrayerCardsPage)

Includes `GroupFooterTemplate` with `HeightRequest="0"` collapse trick (BUG-56) and
per-section empty-state label shown only when a section is expanded and empty.

```xml
<CollectionView IsGrouped="True"
                ItemsSource="{Binding BoxSections}"
                ItemSizingStrategy="MeasureAllItems"
                SelectionMode="None">
    <CollectionView.GroupHeaderTemplate>
        <DataTemplate x:DataType="viewModels:BoxSectionViewModel">
            <!-- Section header with OnSectionHeaderTapped code-behind handler -->
        </DataTemplate>
    </CollectionView.GroupHeaderTemplate>
    <!-- BUG-56: HeightRequest="0" collapses footer for collapsed sections,
         preventing extra spacing between collapsed headers. A DataTrigger
         sets HeightRequest="-1" when IsExpanded=True. -->
    <CollectionView.GroupFooterTemplate>
        <DataTemplate x:DataType="viewModels:BoxSectionViewModel">
            <VerticalStackLayout HeightRequest="0" Spacing="0">
                <VerticalStackLayout.Triggers>
                    <DataTrigger TargetType="VerticalStackLayout"
                                 Binding="{Binding IsExpanded}" Value="True">
                        <Setter Property="HeightRequest" Value="-1" />
                    </DataTrigger>
                </VerticalStackLayout.Triggers>
                <!-- Empty-section hint: MultiTrigger on IsExpanded + CardCount=0 -->
                <Label Text="No cards yet — move or create cards to add them here"
                       IsVisible="False" HeightRequest="0" Style="{StaticResource MutedText}">
                    <Label.Triggers>
                        <MultiTrigger TargetType="Label">
                            <MultiTrigger.Conditions>
                                <BindingCondition Binding="{Binding IsExpanded}" Value="True" />
                                <BindingCondition Binding="{Binding CardCount}" Value="0" />
                            </MultiTrigger.Conditions>
                            <Setter Property="IsVisible" Value="True" />
                            <Setter Property="HeightRequest" Value="-1" />
                        </MultiTrigger>
                    </Label.Triggers>
                </Label>
            </VerticalStackLayout>
        </DataTemplate>
    </CollectionView.GroupFooterTemplate>
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="viewModels:PrayerCardViewModel">
            <!-- Card item -->
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

**Note:** PrayerCardsPage has no `Title` attribute and uses `SafeAreaEdges="{OnPlatform Android='Container, Container, Container, All', Default=Container}"` (not a named Title bar).

### Flat CollectionView with Inline Action Chips (TagsPage, BoxesPage)

TagsPage and BoxesPage do NOT use `SwipeView`. Actions appear as inline chip Grids
revealed when a row is selected. No file under Views/ uses `SwipeView`.

```xml
<CollectionView ItemsSource="{Binding Tags}" SelectionMode="None">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="viewModels:TagItemViewModel">
            <VerticalStackLayout Spacing="0">
                <!-- Tag row with TapGestureRecognizer Command="{Binding SelectCommand}" -->
                <Grid Padding="20,14" ColumnDefinitions="14, *, Auto, Auto" ColumnSpacing="12">
                    <Grid.GestureRecognizers>
                        <TapGestureRecognizer Command="{Binding SelectCommand}" />
                    </Grid.GestureRecognizers>
                    <!-- Color dot, Name label, System badge, Chevron -->
                </Grid>
                <!-- Action chips — visible only when IsSelected (ShowActions) -->
                <HorizontalStackLayout IsVisible="{Binding ShowActions}" Spacing="8" Padding="20,0,20,10">
                    <Border Style="{StaticResource ActionChip}">
                        <Border.GestureRecognizers>
                            <TapGestureRecognizer Command="{Binding EditCommand}" />
                        </Border.GestureRecognizers>
                        <!-- Edit chip content -->
                    </Border>
                </HorizontalStackLayout>
                <BoxView Style="{StaticResource DividerLine}" />
            </VerticalStackLayout>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### Status Filter Buttons with DataTrigger
```xml
<Grid ColumnDefinitions="*, *, *" ColumnSpacing="4">
    <Button Text="Active" Command="{Binding SetStatusCommand}" CommandParameter="Active">
        <Button.Triggers>
            <DataTrigger TargetType="Button" Binding="{Binding IsActiveSelected}" Value="True">
                <Setter Property="BackgroundColor"
                        Value="{AppThemeBinding Light={StaticResource Primary},
                                                Dark={StaticResource PrimaryBtnDark}}" />
                <Setter Property="TextColor" Value="White" />
            </DataTrigger>
        </Button.Triggers>
    </Button>
</Grid>
```

### Horizontal Tag Filter Chips (shared `TagFilterChipTemplate`)

PrayerCardsPage and PrayerListPage both render the chip strip as a `CollectionView` using the
shared `TagFilterChipTemplate` in `Resources/Styles/Styles.xaml`, so the two strips stay identical:
```xml
<CollectionView ItemTemplate="{StaticResource TagFilterChipTemplate}"
                ItemsSource="{Binding AvailableTags}"
                SelectionMode="None" />

<!-- Styles.xaml -->
<DataTemplate x:Key="TagFilterChipTemplate" x:DataType="viewModels:TagFilterChipViewModel">
    <Border Padding="10,4" StrokeThickness="1.5">
        <Border.GestureRecognizers>
            <TapGestureRecognizer Command="{Binding ToggleCommand}" />
        </Border.GestureRecognizers>
        <Label Text="{Binding Tag.Name}" FontSize="{StaticResource FontCaption}" />
    </Border>
</DataTemplate>
```
`BindableLayout` is used for short non-scrolling lists (e.g. `SelectedTags` on PrayerDetailPage, the scope pickers).

### CarouselView (Prayer Time)
```xml
<CarouselView ItemsSource="{Binding Entries}"
              Position="{Binding CurrentIndex, Mode=TwoWay}"
              IsVisible="{Binding HasCompleted, Converter={StaticResource InverseBool}}">
    <!-- Two-way Position binding syncs swipe with ViewModel -->
</CarouselView>
```

---

## Dynamic Toolbar — PrayerCardsPage Pattern

PrayerCardsPage uses a **single static `ToolbarItem`** whose `Text` and `IconImageSource`
are mutated in `ApplyMultiSelectToolbarState` — not via `SetBinding`. There are no
separate edit/save ToolbarItems with `SetBinding` on this page.

```xml
<!-- XAML: one static item, Clicked event -->
<ToolbarItem Text="More" AutomationId="More"
             IconImageSource="{AppThemeBinding Light=ellipsis_vertical_solid_full.png,
                                               Dark=ellipsis_vertical_solid_full_dark.png}"
             Clicked="OnOverflowTapped" Order="Primary" Priority="0" />
```

```csharp
// Code-behind: mutate Text/Icon in place when multi-select mode changes
private void ApplyMultiSelectToolbarState(PrayerCardsViewModel vm)
{
    var item = ToolbarItems.FirstOrDefault();
    if (item is null) return;
    var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
    // Semantics first: Android refreshes the a11y description only on a Text/Icon change.
    if (vm.IsMultiSelectMode)
    {
        SemanticProperties.SetDescription(item, "Cancel");
        SemanticProperties.SetHint(item, "Exit multi-select mode");
        item.Text = "Cancel";
        item.IconImageSource = isDark ? "xmark_solid_full_dark.png" : "xmark_solid_full.png";
    }
    else
    {
        SemanticProperties.SetDescription(item, "More actions");
        SemanticProperties.SetHint(item, "Opens a menu with Add Card, Manage Collections, and Select");
        item.Text = "More";
        item.IconImageSource = isDark
            ? "ellipsis_vertical_solid_full_dark.png"
            : "ellipsis_vertical_solid_full.png";
    }
}
```

**Note:** `AutomationId` is never reassigned — MAUI's `BindableProperty` enforces
set-once and throws on mutation. Only `Text`, `IconImageSource`, and
`SemanticProperties` change between modes.

For detail pages with separate Save and "Save +" items bound via `SetBinding` to `SaveCommand` /
`SaveAndNewCommand`, use the pattern in `PrayerCardPage` / `PrayerDetailPage`.

---

## Cards-page expanded subtree — inline IsVisible realization (shape (i), #42)

**Updated 2026-06-27 (#42).** The cards page renders each card's expanded content
(action chips + prayers + Add-prayer) by **inlining the expanded subtree directly
in the cell `DataTemplate`**, gated by `IsVisible="{Binding IsExpanded}"` — eager
realization, hidden (Android visibility GONE) when collapsed. There is **no lazy
realization**: the prior `OnCardBorderLoaded` → `Rebind` → `RealizeExpandedSubtree`
/ `ExpandedSubtreeHost` / `_savedCardRealizedTcs` machinery (and the earlier
`AnimateCardMargin` Rebind cascade) is **retired**.

**Why retired.** The lazy `ExpandedSubtreeHost` realization didn't reliably surface
for off-screen cells — the root cause of the system-card auto-expand bug (#42) and
the #117 chip-realization test failures. Inlining makes the subtree always present,
so setting `ExpandedCardId` reveals the content regardless of cell position. The
"eager realization scroll cliff" (the old PERF-10 concern) was **re-measured
on-device** (Android emulator, 82-card stress, 2026-06-27): eager scroll is modestly
heavier than lazy (~4%→6% janky frames; tail 99th 93→200ms) but acceptable for
typical libraries, and cold-launch is unchanged.

**Current cards-page realization pattern:**
- The card `Margin` (collapsed vs expanded) is a **`DataTrigger` on `IsExpanded`** in
  XAML — not a code-behind margin animation.
- The expanded subtree is a `VerticalStackLayout IsVisible="{Binding IsExpanded}"`
  inlined in the cell template; the action-chips `Grid` keeps its own
  `IsVisible="{Binding ShowActionChips}"` (= `IsExpanded && !IsSystem`).
- `OnCardBorderLoaded` survives **only** to install the iOS (`#if !ANDROID`)
  long-press `TouchBehavior` — it no longer realizes a subtree or rebinds.
- `ScrollToSavedCardAsync` scrolls immediately after `DrainLayoutPassAsync` (the
  Android adapter-snapshot guard — still required); there is no realization signal
  to await.

> **Do not reintroduce** the lazy `ExpandedSubtreeHost` / `RealizeExpandedSubtree`
> pattern — it caused #42/#117 and was deliberately retired with on-device data.

---

## SafeAreaEdges and the Android keyboard (#357)

MAUI 10 `ContentPage` defaults to `SafeAreaEdges="None"` (edge-to-edge), and Android 15+ enforces edge-to-edge, so every page declares a value on its root.

| Page kind | Value | Why |
|---|---|---|
| Page with an `Entry`/`Editor`/`SearchBar` | `{OnPlatform Android='Container, Container, Container, All', Default=Container}` | Bottom `All` pads by the keyboard on Android; iOS keeps `Container` (E2E guards it) |
| List page without input | `Container` | Clears status and navigation bars |
| PrayerTimePage | `Container` | Covers the landscape display cutout |

- Keyboard padding works only because `App.xaml` sets `android:Application.WindowSoftInputModeAdjust="Resize"`. MAUI overwrites the manifest's `AdjustResize` with this property (default `Pan`), and under `Pan` its safe-area code skips keyboard padding. Do not remove it.
- Set the value on the page, not a `ScrollView` (no effect there) or an inner layout.
- `PrayerApp.Tests/Views/SafeAreaEdgesContractTests.cs` locks each page's value and the App.xaml attribute; add a row when a page gains input.

---

## Android Native Gesture Detector (BUG-60)

On Android, MAUI `TapGestureRecognizer` and native `GestureDetector` conflict on the
card header `Grid`. Solution: attach a native `GestureDetector` via `OnCardHeaderLoaded`
and clear the XAML `TapGestureRecognizer` at the native level.

```csharp
private void OnCardHeaderLoaded(object? sender, EventArgs e)
{
#if ANDROID
    if (sender is not Grid grid) return;
    if (grid.Handler is not null)
        AttachNativeCardGestures(grid);
    else
    {
        grid.HandlerChanged -= OnCardHeaderHandlerChanged; // -= first: Loaded can re-fire
        grid.HandlerChanged += OnCardHeaderHandlerChanged;
    }
#endif
}

#if ANDROID
private readonly NativeAttachGuard _cardGestureGuard = new();

private void AttachNativeCardGestures(Grid grid)
{
    if (grid.Handler?.PlatformView is not Android.Views.View nativeView) return;
    if (BindingContext is not PrayerCardsViewModel vm) return;
    if (!_cardGestureGuard.TryMark(nativeView)) return; // one Touch handler per native view
    grid.GestureRecognizers.Clear(); // Remove conflicting MAUI recognizer
    var listener = new CardGestureListener(grid, vm);
    var detector = new GestureDetector(nativeView.Context, listener);
    nativeView.Touch += (s, args) => { args.Handled = detector.OnTouchEvent(args.Event!); };
}
#endif
```

The `CardGestureListener` (`SimpleOnGestureListener` subclass) handles
`OnSingleTapUp` (tap to expand/toggle selection) and `OnLongPress` (enter multi-select).

`Loaded` re-fires on every re-attach (resume, scroll recycle), so both guards are required: without them each re-attach stacks another `Touch` handler and one tap fires N times (#360: N PIN popups on a Locked card). TagDetailPage's swatch uses the same `NativeAttachGuard` (`PrayerApp/Helpers/NativeAttachGuard.cs`).

---

## CardsOverflowPopup — CT.Maui Detached Resource Tree

CT.Maui v14 Popups run in a **detached visual tree** that does not inherit
`App.Resources`. Every `StaticResource` lookup for `Colors.xaml` tokens returns null,
causing all `AppThemeBinding` to silently fall back to defaults (stuck "light").

**Fix:** Explicitly merge `Colors.xaml` into the Popup's own `Resources`. Also use
`PopupOptions { Shape = null }` to suppress the default white `RoundRectangle` frame —
the inner `Border` owns the themed surface.

```xml
<!-- CardsOverflowPopup.xaml -->
<toolkit:Popup BackgroundColor="Transparent">
    <toolkit:Popup.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../Resources/Styles/Colors.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </toolkit:Popup.Resources>
    <Border BackgroundColor="{AppThemeBinding Light={StaticResource CardLight}, Dark={StaticResource CardDark}}">
        <!-- Content -->
    </Border>
</toolkit:Popup>
```

```csharp
// Code-behind: suppress default frame, show popup
var options = new PopupOptions { Shape = null };
await this.ShowPopupAsync(new CardsOverflowPopup(vm), options, CancellationToken.None);
```

`PrayersOverflowPopup` uses the same merge and `Shape = null`. The onboarding,
`ColorPickerPopup`, and Confidential prompt popups do not merge `Colors.xaml`.

---

## ScrollTo — Drain Layout Pass First (Android)

Calling `CollectionView.ScrollTo` while the Android adapter snapshot is stale throws
`IllegalArgumentException: Invalid target position`. Yield two dispatcher ticks before
scrolling via `Dispatcher.DrainLayoutPassAsync()` (`Helpers/DispatcherExtensions.cs`).

```csharp
// PrayerCardsPage.xaml.cs
private async Task ScrollToSavedCardAsync(PrayerCardsViewModel vm, PrayerCardViewModel card)
{
    await Dispatcher.DrainLayoutPassAsync(); // Two dispatcher ticks — drains in-flight layout
    try
    {
        var section = vm.BoxSections.FirstOrDefault(s => s.Contains(card));
        if (section != null)
            cardCollection.ScrollTo(card, section, ScrollToPosition.Center, animate: true);
        else
            cardCollection.ScrollTo(card, position: ScrollToPosition.Center, animate: true);

        SemanticScreenReader.Announce($"New card: {card.Title}");
    }
    catch (Exception ex)
    {
        Diagnostics.ResolveLog()?.Log("PrayerCardsPage.ScrollToSavedCardAsync", ex);
    }
}
// The highlight fade runs separately in FadeHighlightAfterDelayAsync.
```

Also use `DrainLayoutPassAsync` before `Entry.Focus()` after a Shell push (BUG-70 family).

---

## Modal Navigation

### Page-Sheet Modal
```csharp
await Shell.Current.Navigation.PushModalAsync(
    _services.GetRequiredService<PrayerTimeScopePage>());

// Modal with Cancel/Save ToolbarItems: wrap in a NavigationPage
await _services.GetRequiredService<INavigationService>()
    .PushModalWithNavigationBarAsync(confirmImportPage);
```

### CT.Maui Popup
```csharp
await this.ShowPopupAsync(new OnboardingCompletePopup(),
    new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false },
    CancellationToken.None);
```

### Modal with Data Return (Tag Picker Pattern)

ViewModels cannot reference View types (breaks test project compilation). Use an
event from the ViewModel that code-behind subscribes to for page creation.

```csharp
// ViewModel: event + TaskCompletionSource
public event Func<TagPickerViewModel, Task>? TagPickerRequested;

private async Task OpenTagPickerAsync()
{
    var pickerVm = new TagPickerViewModel(/* deps */);
    if (TagPickerRequested is not null)
        await TagPickerRequested.Invoke(pickerVm);
    await pickerVm.WaitForDismissAsync(); // blocks until Done tapped
    SyncTagsFromPicker(pickerVm);
}

// Code-behind: subscribe to event and create the page
vm.TagPickerRequested += async pickerVm =>
    await Shell.Current.Navigation.PushModalAsync(new TagPickerPage(pickerVm));
```

`PushModalAsync` returns when the modal **appears**, not when dismissed. Always use
`WaitForDismissAsync()` to await user completion.

---

## Event Handler Patterns

### Section Header Tap (Grouped CollectionView)
```csharp
private void OnSectionHeaderTapped(object? sender, TappedEventArgs e)
{
    if (sender is Grid grid && grid.BindingContext is BoxSectionViewModel section)
    {
        section.IsExpanded = !section.IsExpanded;
        SemanticScreenReader.Announce(section.IsExpanded
            ? $"Expanded {section.Name}" : $"Collapsed {section.Name}");
        if (BindingContext is PrayerCardsViewModel vm)
            vm.SaveSectionExpansionState();
    }
}
```

### Deferred Action on Selection (avoid CollectionChanged during callback)
```csharp
private void OnSuggestionSelected(object? sender, SelectionChangedEventArgs e)
{
    if (sender is CollectionView cv && e.CurrentSelection.FirstOrDefault() is PrayerTag tag)
    {
        var tagId = tag.Id;
        cv.SelectedItem = null;
        Dispatcher.DispatchAsync(() =>
        {
            if (BindingContext is MyViewModel vm)
                vm.AddTagCommand.Execute(tagId);
        });
    }
}
```

### iOS Swipe-Back Disable (edit pages)
```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    #if IOS
    Platforms.iOS.Helpers.SwipeBackHelper.DisableSwipeBack(this);
    #endif
}
```

---

## App-Level Resources (`App.xaml`)

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Resources/Styles/Colors.xaml" />
            <ResourceDictionary Source="Resources/Styles/Styles.xaml" />
        </ResourceDictionary.MergedDictionaries>
        <converters:BoolToTextDecorationConverter x:Key="BoolToTextDecoration" />
        <converters:BoolToMutedColorConverter x:Key="BoolToMutedColor" />
        <converters:InverseBoolConverter x:Key="InverseBool" />
        <converters:StringToBoolConverter x:Key="StringToBool" />
        <converters:BoolToChevronConverter x:Key="BoolToChevron" />
        <converters:BoolToTriangleConverter x:Key="BoolToTriangle" />
        <converters:EnumToBoolConverter x:Key="EnumToBool" />
    </ResourceDictionary>
</Application.Resources>
```

---

## Common Mistakes

| Mistake | Correct approach |
|---------|-----------------|
| Using old `_loaded` flag + `LoadAsync`/`RefreshAsync` branch | Use `await PageSync.OnAppearingAsync(vm)` — single call replaces both |
| Assuming TagsPage uses SwipeView | TagsPage uses inline action chip Grids inside a plain CollectionView |
| Setting `Title` on PrayerCardsPage | PrayerCardsPage has no `Title`; it relies on `SafeAreaEdges` instead |
| Omitting `Colors.xaml` merge in a CT.Maui Popup | Popup runs in a detached visual tree; `AppThemeBinding` silently falls back to light-mode defaults without explicit merge |
| Omitting `PopupOptions { Shape = null }` | CT.Maui v14 draws a white `RoundRectangle` frame by default; `Shape = null` removes it so the inner themed `Border` controls the surface |
| Calling `CollectionView.ScrollTo` immediately after `ItemsSource` change on Android | Call `await Dispatcher.DrainLayoutPassAsync()` first to let the adapter commit its snapshot |
| Using `x:DataType="{x:Null}"` to suppress XC0045/XC0023 | Use a code-behind `Tapped` handler and pattern-match `BindingContext`; see `TagPickerPage.xaml.cs OnSuggestionTapped` |
| Adding `AppThemeBinding` inside a keyed Style in a `BindableLayout` DataTemplate | Use inline color bindings for `BoxView`/dividers inside `BindableLayout` DataTemplates — keyed styles don't reliably propagate |
