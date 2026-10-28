---
name: prayer-app-viewmodels
description: >
  Use when creating or modifying ViewModels in PrayerApp. Covers the universal
  SyncAsync/ISyncableViewModel/PageSync load architecture, SingleFlightGate
  coalescing, IsBusy save-indicator pattern, messenger-driven reactive sync,
  IQueryAttributable, IEditGuard, observable collection management, and the
  full ViewModel reference table. Replaces all LoadAsync/RefreshAsync/_loaded
  patterns — those are gone.
keywords:
  - SyncAsync
  - ISyncableViewModel
  - PageSync
  - SingleFlightGate
  - IsBusy
  - IMessenger
  - SafeFireAndForget
  - SetProperty
  - AsyncRelayCommand
  - IQueryAttributable
  - IEditGuard
---

# PrayerApp ViewModel Patterns

All ViewModels live in `PrayerApp/ViewModels/` and inherit from
`CommunityToolkit.Mvvm.ComponentModel.ObservableObject`, except `BoxSectionViewModel`
(an `ObservableCollection`) and the plain classes `HelpViewModel` and `TagChipViewModel`.

---

## When to Use This Skill

- Creating a new ViewModel (list page or detail editor)
- Adding `SyncAsync`, commands, or messenger subscriptions
- Wiring up `IsBusy` save protection or `IEditGuard` unsaved-changes prompts
- Updating the ViewModel reference table after adding a new VM

---

## Quick Reference — ViewModel Inventory

| ViewModel | Interfaces | Key Responsibility |
|---|---|---|
| `HomeViewModel` | `ISyncableViewModel` | Dashboard metrics (overdue, card count, prayer count) |
| `PrayerCardsViewModel` | `IQueryAttributable`, `ISyncableViewModel` | Card list with box grouping, filter, search, multi-select |
| `PrayerCardViewModel` | `IQueryAttributable`, `IEditGuard` | Single card editor; nested inside `BoxSectionViewModel` groups |
| `PrayerListViewModel` | `IQueryAttributable`, `ISyncableViewModel` | Prayer list with status/tag filtering |
| `PrayerRequestDetailViewModel` | `IQueryAttributable`, `IEditGuard` | Prayer detail editor with `SaveAndNewCommand` |
| `PrayerTimeViewModel` | `IQueryAttributable` | Prayer carousel with auto-advance timer |
| `PrayerTimeScopeViewModel` | — | Tag selection modal for scoped prayer sessions |
| `PrayerTimeBoxScopeViewModel` | — | Box selection modal for box-scoped prayer sessions |
| `PrayerTimeCardSelectViewModel` | — | "Choose cards" modal; launches Prayer Time with `scope=selection` |
| `BoxesViewModel` | `ISyncableViewModel` | Box/folder list management |
| `BoxDetailViewModel` | `IQueryAttributable`, `IEditGuard` | Box editor with `IsBusy` save guard |
| `BoxItemViewModel` | — | Nested VM inside `BoxesViewModel`; represents one box row |
| `BoxSectionViewModel` | `ObservableCollection<PrayerCardViewModel>` | Collapsible card group on the Cards page |
| `TagsViewModel` | `ISyncableViewModel` | Tag list management |
| `TagDetailViewModel` | `IQueryAttributable`, `IEditGuard` | Tag editor |
| `TagChipViewModel` | — | Tag chip display (selected tags on prayer detail) |
| `TagPickerViewModel` | — | Modal tag picker with comma auto-save and creation |
| `TagFilterChipViewModel` | — | Filter chip with selection state for card/prayer lists |
| `ConfirmImportViewModel` | `IDisposable` | Import confirmation and Quick Add manual entry (`InitializeManualEntry`) |
| `HelpViewModel` | — | Static FAQ content |
| `FaqItemViewModel` | — | FAQ accordion item |

---

## Universal Load Architecture — ISyncableViewModel / PageSync / SingleFlightGate

**This is the canonical load pattern for all list pages.** `LoadAsync`, `RefreshAsync`,
and `_loaded` flags are gone. Do not reintroduce them.

### ISyncableViewModel contract (`ViewModels/ISyncableViewModel.cs`)

```csharp
public interface ISyncableViewModel
{
    bool IsLoading { get; }
    Task SyncAsync();
}
```

Implementations must be idempotent — diff against current state rather than
clear-and-repopulate — so first-call and Nth-call paths are identical.

### PageSync helper (`Helpers/PageSync.cs`)

```csharp
public static class PageSync
{
    public static async Task OnAppearingAsync(ISyncableViewModel vm)
    {
        await App.InitTask;   // Wait for DB readiness
        await vm.SyncAsync();
    }
}
```

Call from every list page's `OnAppearing`:

```csharp
protected override async void OnAppearing()
{
    base.OnAppearing();
    await PageSync.OnAppearingAsync((ISyncableViewModel)BindingContext);
}
```

### SingleFlightGate coalescing (`Helpers/SingleFlightGate.cs`)

Collapses bursts of concurrent `SyncAsync` triggers (e.g., messenger broadcast +
`PageSync.OnAppearingAsync` + sibling rebroadcasts) into one in-flight execution
plus at most one coalesced follow-up. Every list VM holds one:

```csharp
private readonly SingleFlightGate _syncGate = new();

public async Task SyncAsync()
{
    IsLoading = true;
    try { await _syncGate.RunAsync(SyncCoreAsync); }
    finally { IsLoading = false; }
}

private async Task SyncCoreAsync()
{
    // Fetch fresh data, diff, mutate collection in-place (add/remove/update)
}
```

`IsLoading` toggles drive a page-level `LoadingOverlay` and announce to accessibility.

---

## Messenger-Driven Reactive Sync

List VMs register `IMessenger` handlers in their primary constructor so that CRUD
operations on other pages trigger a re-sync without navigation. Use
`SafeFireAndForget()` to avoid blocking the message dispatch:

```csharp
// From the HomeViewModel constructor (excerpt)
_messenger.Register<HomeViewModel, PrayerCardChangedMessage>(this,
    (vm, _) => vm.SyncAsync().SafeFireAndForget());
_messenger.Register<HomeViewModel, PrayerChangedMessage>(this,
    (vm, _) => vm.SyncAsync().SafeFireAndForget());
_messenger.Register<HomeViewModel, BulkChangedMessage>(this,
    (vm, _) => vm.SyncAsync().SafeFireAndForget());
```

```csharp
// From the PrayerCardsViewModel constructor (excerpt)
_messenger.Register<PrayerCardsViewModel, PrayerCardChangedMessage>(this,
    (vm, _) => vm.SyncAsync(null, skipExpandedPrayerReload: true).SafeFireAndForget());
_messenger.Register<PrayerCardsViewModel, PrayerChangedMessage>(this,
    (vm, msg) => vm.OnPrayerChangedAsync(msg).SafeFireAndForget());
_messenger.Register<PrayerCardsViewModel, BulkChangedMessage>(this,
    (vm, _) => vm.SyncAsync().SafeFireAndForget());
_messenger.Register<PrayerCardsViewModel, SessionRelockedMessage>(this,
    (vm, _) => vm.OnSessionRelocked());
```

Weak references auto-clean on GC — no manual unsubscription needed.

---

## ViewModel Constructor Template

ViewModels are registered `AddTransient` in `MauiProgram.cs`; pages take their VM
by constructor injection and set `BindingContext = vm`. Each VM also keeps a
parameterless (or reduced) constructor that chains to the primary via
`IPlatformApplication.Current!.Services.GetRequiredService<>()`:

```csharp
// Primary constructor — receives mocks in tests
public NewListViewModel(IMyService myService, INavigationService navigationService,
    IAccessibilityService accessibilityService, IMessenger messenger)
{
    _myService = myService ?? throw new ArgumentNullException(nameof(myService));
    _navigationService = navigationService;
    _accessibilityService = accessibilityService;
    _messenger = messenger;

    AddCommand = new AsyncRelayCommand(AddAsync);

    _messenger.Register<NewListViewModel, MyEntityChangedMessage>(this,
        (vm, _) => vm.SyncAsync().SafeFireAndForget());
}

// Parameterless constructor — used by XAML; delegates to primary
public NewListViewModel() : this(
    IPlatformApplication.Current!.Services.GetRequiredService<IMyService>(),
    IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
    IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
    IPlatformApplication.Current!.Services.GetRequiredService<IMessenger>())
{ }
```

See the `BoxesViewModel` constructors for the canonical multi-service example (its secondary constructor takes `IBoxService` and resolves the rest).

---

## IsBusy Save-Indicator Pattern

Detail-edit VMs (`BoxDetailViewModel`, `PrayerCardViewModel`,
`PrayerRequestDetailViewModel`) gate their save commands on `IsBusy` to prevent
double-tap duplication and drive a page-level `ActivityIndicator`.

```csharp
// From BoxDetailViewModel.IsBusy
private bool _isBusy;
public bool IsBusy
{
    get => _isBusy;
    private set
    {
        if (SetProperty(ref _isBusy, value))
            (SaveCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}

// Commands declared with canExecute guard
SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
```

When a VM has two save commands (`PrayerRequestDetailViewModel`, `PrayerCardViewModel`), both notify
on the same `IsBusy` toggle:

```csharp
// From PrayerRequestDetailViewModel.IsBusy
if (SetProperty(ref _isBusy, value))
{
    (SaveCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
    (SaveAndNewCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
}
SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
SaveAndNewCommand = new AsyncRelayCommand(SaveAndNewAsync, () => !IsBusy);
```

---

## IQueryAttributable — Navigation Parameters

ViewModels that receive navigation parameters implement `IQueryAttributable`:

```csharp
public partial class MyViewModel : ObservableObject, IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string idStr
            && int.TryParse(idStr, out var id))
        {
            LoadEntityAsync(id).SafeFireAndForget();
        }
        else if (query.ContainsKey("new"))
        {
            IsNew = true;
        }
    }
}
```

**Navigation with parameters:**
```csharp
await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?id={prayerId}");
await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?new=true&parentCardId={cardId}");
await _navigationService.GoToAsync($"..?saved={prayer.Id}&parentCardId={prayer.PrayerCardId}");
```

---

## IEditGuard — Unsaved Changes

ViewModels with forms implement `IEditGuard` for back-navigation protection:

```csharp
public interface IEditGuard
{
    bool IsDirty { get; }
    Task<bool> CanLeaveAsync();
}
```

```csharp
public partial class MyDetailViewModel : ObservableObject, IQueryAttributable, IEditGuard
{
    private string _originalTitle = string.Empty;

    public bool IsDirty => Title != _originalTitle;

    public async Task<bool> CanLeaveAsync()
    {
        if (!IsDirty) return true;
        return await _navigationService.DisplayConfirmAsync(
            "Unsaved Changes", "Discard changes?", "Discard", "Cancel");
    }

    private void CaptureOriginals()
    {
        _originalTitle = Title ?? string.Empty;
    }
    // Call CaptureOriginals() after load and after save
}
```

`AppShell.OnShellNavigating` checks if the current page's ViewModel implements
`IEditGuard`. If `IsDirty`, it calls `CanLeaveAsync()` and cancels navigation if the
user declines.

---

## Observable Collection Management

### Idempotent Diff Pattern (list VMs)

List VMs mutate their `ObservableCollection` in-place across syncs so item-level
state (expansion, selection) survives refresh. Do not clear-and-repopulate:

```csharp
// From BoxesViewModel.SyncCoreAsync (Boxes/Tags pattern)
var freshIds = fresh.Select(x => x.Id).ToHashSet();

// 1. Remove deleted
var toRemove = Collection.Where(vm => !freshIds.Contains(vm.Id)).ToList();
foreach (var vm in toRemove) Collection.Remove(vm);

// 2. Add new
var currentIds = Collection.Select(vm => vm.Id).ToHashSet();
foreach (var item in fresh.Where(x => !currentIds.Contains(x.Id)))
    Collection.Add(new ItemViewModel(item, ...));

// 3. Update existing
foreach (var item in fresh)
    Collection.FirstOrDefault(vm => vm.Id == item.Id)?.Update(item);
```

### Replace-Not-Mutate (grouped/sectioned collections)

`BoxSections` on the Cards page is replaced entirely on each rebuild to avoid iOS
`UICollectionView` desync when the group structure itself changes:

```csharp
// GOOD — replaces the collection reference
BoxSections = new ObservableCollection<BoxSectionViewModel>(newSections);

// BAD — mutating a grouped source mid-layout can crash iOS UICollectionView
foreach (var section in newSections) BoxSections.Add(section);
```

### BoxSectionViewModel — Collapsible Groups

`BoxSectionViewModel` inherits `ObservableCollection<PrayerCardViewModel>` so MAUI's
grouped `CollectionView` can use it directly. Collapse/expand manipulate the visible
collection; a backing list preserves the full card set.

---

## Property Patterns

> **Convention:** This codebase uses explicit `SetProperty(ref _field, value)` for every property. The `[ObservableProperty]` source generator is **not used anywhere** (0 usages, verified 2026-10-28). Do not introduce it — even though `CommunityToolkit.Mvvm` is referenced, only `ObservableObject` is consumed.

### SetProperty with Dependent Properties
```csharp
private int _overdueCount;
public int OverdueCount
{
    get => _overdueCount;
    private set
    {
        if (SetProperty(ref _overdueCount, value))
        {
            OnPropertyChanged(nameof(HasOverdue));
            OnPropertyChanged(nameof(OverdueHeadline));
        }
    }
}

public bool HasOverdue => OverdueCount > 0;
public string OverdueHeadline => OverdueCount switch
{
    0 => "All requests have been recently prayed for.",
    1 => "1 Overdue",
    _ => $"{OverdueCount} Overdue"
};
```

### Null-Safe String Properties
```csharp
private string _title = string.Empty;
public string Title
{
    get => _title;
    set => SetProperty(ref _title, value ?? string.Empty);
}
```

---

## Command Patterns

> **Convention:** Commands use `new (Async)RelayCommand(...)` constructors. The `[RelayCommand]` source generator is **not used anywhere** (0 usages, verified 2026-10-28). Do not introduce it.

```csharp
// Async — most common
SaveCommand = new AsyncRelayCommand(SaveAsync);
DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsSystem); // canExecute

// Async with IsBusy guard (detail editors)
SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);

// Synchronous
ToggleFavoriteCommand = new RelayCommand(() => IsFavorite = !IsFavorite);

// Parameterized
SetStatusCommand = new RelayCommand<string>(s => StatusFilter = ParseStatus(s));
```

**Testing commands:**
```csharp
await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
sut.ToggleFavoriteCommand.Execute(null);
```

---

## Async Patterns

### SafeFireAndForget
```csharp
LoadEntityAsync(id).SafeFireAndForget();
```
Extension in `Helpers/TaskExtensions.cs`. Prevents unhandled task exceptions from
fire-and-forget calls (messenger handlers, `OnAppearing` delegates).

### Debounced Announcements
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

---

## Common Mistakes

| Mistake | Correct Pattern |
|---|---|
| Implementing `LoadAsync`/`RefreshAsync` with a `_loaded` flag | Implement `ISyncableViewModel.SyncAsync()` + use `PageSync.OnAppearingAsync` |
| Forgetting `SafeFireAndForget` on messenger handlers | Always: `vm.SyncAsync().SafeFireAndForget()` — never `await` inside a `Register` lambda |
| Omitting `ISyncableViewModel` for a new list page | Every list page VM must implement `ISyncableViewModel`; pages call `PageSync.OnAppearingAsync` |
| Mutating a grouped collection during iOS CollectionView layout | Replace the collection reference entirely: `BoxSections = new ObservableCollection<>(...)` |
| Clear-and-repopulate on sync | Diff in-place (add/remove/update) to preserve item-level state across refreshes |
| Missing `NotifyCanExecuteChanged` on `IsBusy` set | Call `(SaveCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged()` in `IsBusy` setter |
| Constructor injection only (no parameterless ctor) | Keep a parameterless (or reduced) constructor that delegates to the primary via service locator, matching every existing VM |
| Using `[ObservableProperty]` / `[RelayCommand]` source generators | This codebase uses explicit `SetProperty(ref ...)` and `new (Async)RelayCommand(...)` exclusively (0 source-generator usages across all VMs). Despite the package being referenced, only `ObservableObject` is consumed. Do not introduce generators piecemeal. |
| Binding a visible element to a **get-only computed** property whose value comes from an async load (e.g. `public string X => $"…{_loadedField}";`) | The get-only expression has no backing field, so nothing fires `PropertyChanged` after the `await` completes — on-device the bound element silently renders **empty** (evaluated before the data arrived; no notification to re-evaluate). Use a **backed** property `get => _x; private set => SetProperty(ref _x, value);` and set it explicitly at the end of the async load. (Empirical — the exact MAUI compiled-binding mechanism was not proven; treat as a safe default.) |

---

## Checklist: Adding a New List-Page ViewModel

1. Create `PrayerApp/ViewModels/NewViewModel.cs` inheriting `ObservableObject`
2. Implement `ISyncableViewModel` (expose `IsLoading` + `SyncAsync`)
3. Add a `SingleFlightGate _syncGate` field; wrap `SyncCoreAsync` through it
4. Add primary constructor with all service parameters + `ArgumentNullException` guards
5. Add parameterless constructor delegating via `IPlatformApplication.Current!.Services.GetRequiredService<>()`
6. Register `IMessenger` handlers in the primary constructor using `SafeFireAndForget`
7. If navigated to with params, implement `IQueryAttributable`
8. Call `PageSync.OnAppearingAsync(vm)` from the page's `OnAppearing`
9. Add `<Compile Include>` in `PrayerApp.Tests/PrayerApp.Tests.csproj`
10. Write tests in `PrayerApp.Tests/ViewModels/NewViewModelTests.cs`
11. Create corresponding View (see prayer-app-views skill)

## Checklist: Adding a New Detail-Editor ViewModel

1. Inherit `ObservableObject`; implement `IQueryAttributable` and `IEditGuard`
2. Add `IsBusy` property with `NotifyCanExecuteChanged` in setter
3. Create `SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy)`
4. If new-item mode needs "Save and New": add `SaveAndNewCommand` gated on same `IsBusy`
5. Add dirty-tracking `_original*` fields; implement `IsDirty` and `CanLeaveAsync`
6. Call `CaptureOriginals()` after load and after save
7. Add primary + parameterless constructors per template above
8. Add `<Compile Include>` in test project; write tests
