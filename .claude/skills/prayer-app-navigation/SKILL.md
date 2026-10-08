---
name: prayer-app-navigation
description: Use when adding pages, routes, query params, modals, tabs, or Shell navigation to PrayerApp. Covers AppShell tab structure, route constants, push/modal navigation, IEditGuard, IPageSheetModal, PageSync/ISyncableViewModel, WeakReferenceMessenger live refresh, and deep-link handling. Keywords: PushModalAsync, IPageSheetModal, PageSync, ISyncableViewModel, DeepLinkService, PopToRoot, IEditGuard, Routes, Shell, GoToAsync.
---

# PrayerApp Navigation

PrayerApp uses .NET MAUI Shell with 5 tabs, push navigation for detail pages, and modals for overlays and scoped sessions. All route names are defined in `Routes.cs`; all registrations live in `AppShell.xaml.cs`.

---

## When to Use

- Adding a new page (tab, push-navigable, or modal)
- Wiring query parameters to a ViewModel
- Implementing unsaved-changes guard (`IEditGuard`)
- Using modal sheets correctly on iOS/iPad (`IPageSheetModal`)
- Synchronizing list pages on appear or after a cross-page mutation

---

## Quick Reference: All Routes

| Constant | Value | Kind |
|---|---|---|
| `Routes.PrayerCardsTab` | `//CardsPage` | Tab (absolute) |
| `Routes.PrayersTab` | `//PrayersPage` | Tab (absolute) |
| `Routes.PrayerCardPage` | `"PrayerCardPage"` | Push |
| `Routes.PrayerDetailPage` | `"PrayerDetailPage"` | Push |
| `Routes.PrayerTimePage` | `"PrayerTimePage"` | Push (scope via `?scope=`) |
| `Routes.TagDetailPage` | `"TagDetailPage"` | Push |
| `Routes.BoxesPage` | `"BoxesPage"` | Push |
| `Routes.BoxDetailPage` | `"BoxDetailPage"` | Push |
| `Routes.AppSettingsPage` | `"AppSettingsPage"` | Push |
| `Routes.BackupPage` | `"BackupPage"` | Push |
| `Routes.AboutPage` | `"AboutPage"` | Push |
| `Routes.HelpPage` | `"HelpPage"` | Push |
| `Routes.ScopeAll` | `"all"` | Query param value |
| `Routes.ScopeTags` | `"tags"` | Query param value |
| `Routes.ScopeBox` | `"box"` | Query param value |

**Modals (no route constant — resolved via DI):** `QuickAddPage`, `TagPickerPage`, `PrayerTimeScopePage`, `PrayerTimeBoxScopePage`, `RestoreProgressPage`

---

## Shell Tab Structure (`AppShell.xaml`)

```xml
<TabBar>
    <ShellContent Title="Home"         Icon="house_solid_full.png"
                  ContentTemplate="{DataTemplate views:MainPage}"
                  Route="MainPage" />
    <ShellContent Title="Prayer Cards" Icon="list_solid_full.png"
                  ContentTemplate="{DataTemplate views:PrayerCard.PrayerCardsPage}"
                  Route="CardsPage" />
    <ShellContent Title="Prayers"      Icon="hands_praying_solid_full.png"
                  ContentTemplate="{DataTemplate views:Prayer.PrayerListPage}"
                  Route="PrayersPage" />
    <ShellContent Title="Tags"         Icon="tag_solid_full.png"
                  ContentTemplate="{DataTemplate tagViews:TagsPage}"
                  Route="TagsPage" />
    <ShellContent Title="Settings"     Icon="gear_solid_full.png"
                  ContentTemplate="{DataTemplate settingsViews:SettingsHubPage}"
                  Route="Settings" />
</TabBar>
```

Tab 4 is **Tags** (`Route="TagsPage"`). `PrayerTimePage` is a push-registered route, not a tab.

---

## Route Registration (`AppShell.xaml.cs`)

All push-navigable pages are registered in the constructor:

```csharp
Routing.RegisterRoute(nameof(PrayerCardPage),    typeof(PrayerCardPage));
Routing.RegisterRoute(nameof(PrayerDetailPage),  typeof(PrayerDetailPage));
Routing.RegisterRoute(nameof(PrayerTimePage),    typeof(PrayerTimePage));
Routing.RegisterRoute(nameof(TagDetailPage),     typeof(TagDetailPage));
Routing.RegisterRoute(nameof(BoxesPage),         typeof(BoxesPage));
Routing.RegisterRoute(nameof(BoxDetailPage),     typeof(BoxDetailPage));
Routing.RegisterRoute(nameof(AppSettingsPage),   typeof(AppSettingsPage));
Routing.RegisterRoute(nameof(BackupPage),        typeof(BackupPage));
Routing.RegisterRoute(nameof(AboutPage),         typeof(AboutPage));
Routing.RegisterRoute(nameof(HelpPage),          typeof(HelpPage));
```

---

## Navigation Patterns

### Tab (absolute)
```csharp
await _navigationService.GoToAsync(Routes.PrayerCardsTab);   // "//CardsPage"
await _navigationService.GoToAsync(Routes.PrayersTab);       // "//PrayersPage"
```

### Push (relative)
```csharp
await _navigationService.GoToAsync(Routes.PrayerCardPage);
await _navigationService.GoToAsync($"{Routes.PrayerTimePage}?scope={Routes.ScopeAll}");
await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?id={prayerId}");
await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?new=true&parentCardId={cardId}");
```

### Back navigation
```csharp
await _navigationService.GoToAsync("..");       // Back one level
await _navigationService.GoToAsync("../..");    // Back two levels
```

### Back with query parameters (notify parent)
```csharp
await _navigationService.GoToAsync($"..?saved={prayer.Id}&parentCardId={prayer.PrayerCardId}");
await _navigationService.GoToAsync($"..?deleted={cardId}");
await _navigationService.GoToAsync($"..?prayerSaved={id}&parentCardId={cardId}&oldCardId={oldId}");
```

### Modal navigation
```csharp
// Push — from code-behind, resolved via DI
await Shell.Current.Navigation.PushModalAsync(
    _services.GetRequiredService<QuickAddPage>());

// Pop — from ViewModel via INavigationService
await _navigationService.PopModalAsync();
```

`PushModalAsync` returns when the modal **appears**, not when dismissed. To await dismissal and read back data, use the `TaskCompletionSource` pattern (see `TagPickerViewModel.WaitForDismissAsync()`).

---

## IPageSheetModal (iOS card-style sheets)

Marker interface in `Views/IPageSheetModal.cs`. Implement it on any `ContentPage` pushed via `PushModalAsync` that should render as a page sheet on iPhone and iPad.

```csharp
public partial class QuickAddPage : ContentPage, IPageSheetModal { }
```

Pages using this pattern: `QuickAddPage`, `TagPickerPage`, `PrayerTimeScopePage`, `PrayerTimeBoxScopePage`.

`PageSheetPresentation.Apply` runs on `Application.ModalPushing`, subscribed in `App()`.

---

## Query Parameter Handling (`IQueryAttributable`)

ViewModels implement `IQueryAttributable` to receive navigation parameters:

```csharp
void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
{
    if (query.ContainsKey("new"))
    {
        IsNew = true;
        if (query.TryGetValue("parentCardId", out var cardIdObj))
            PrayerCardId = int.Parse(cardIdObj.ToString()!);
    }
    else if (query.TryGetValue("id", out var idObj))
    {
        LoadAsync(int.Parse(idObj.ToString()!)).SafeFireAndForget();
    }
    else if (query.ContainsKey("saved"))
    {
        RefreshAsync().SafeFireAndForget();
    }
    else if (query.ContainsKey("deleted"))
    {
        RemoveItemById(int.Parse(query["deleted"].ToString()!));
    }
}
```

**Common parameter names:** `id`, `new`, `parentCardId`, `saved`, `deleted`, `prayerSaved`, `oldCardId`, `scope`

---

## IEditGuard (Unsaved-Changes Guard)

ViewModels with editable forms implement `IEditGuard`. `AppShell.OnShellNavigating` fires on Pop, PopToRoot, ShellItemChanged, ShellSectionChanged, and Unknown (includes iOS swipe-back):

```csharp
if (CurrentPage?.BindingContext is IEditGuard guard && guard.IsDirty)
{
    var deferral = args.GetDeferral();
    try
    {
        if (!await guard.CanLeaveAsync())
            args.Cancel();
    }
    finally
    {
        deferral.Complete();
    }
}
```

The guard uses `GetDeferral()` to keep navigation pending while the async confirmation dialog runs. If `CanLeaveAsync()` returns `false`, navigation is cancelled and the page stays. There is no re-navigate call — the deferral pattern handles timing.

---

## Cross-Tab Consistency: PageSync + ISyncableViewModel

List-page ViewModels implement `ISyncableViewModel` (`ViewModels/ISyncableViewModel.cs`):

```csharp
public interface ISyncableViewModel
{
    bool IsLoading { get; }
    Task SyncAsync();   // Idempotent — diff against current state, never clear-and-repopulate
}
```

Pages call `PageSync.OnAppearingAsync(vm)` from `OnAppearing` (`Helpers/PageSync.cs`):

```csharp
protected override async void OnAppearing()
{
    base.OnAppearing();
    if (BindingContext is ISyncableViewModel vm)
        await PageSync.OnAppearingAsync(vm);  // awaits App.InitTask, then vm.SyncAsync()
}
```

For live cross-tab refresh without waiting for `OnAppearing`, ViewModel constructors subscribe to `WeakReferenceMessenger` broadcasts from the service layer (wired in `MauiProgram.cs`):

```csharp
WeakReferenceMessenger.Default.Register<PrayerCardChangedMessage>(this, (_, msg) =>
    SyncAsync().SafeFireAndForget());
```

Message types (all in `Messages/EntityChangedMessage.cs`): `PrayerCardChangedMessage`, `PrayerChangedMessage`, `TagChangedMessage`, `CardBoxChangedMessage`, `BulkChangedMessage` (used after bulk ops like restore or import — triggers full re-sync, not granular).

---

## Deep Linking (`IDeepLinkService`)

`IDeepLinkService` (`Services/IDeepLinkService.cs`) handles incoming share URLs and file imports:

```csharp
public interface IDeepLinkService
{
    Task ShareRequestAsync(Prayer prayer);
    Task ShareCardAsync(PrayerCard card, IEnumerable<Prayer> prayers);
    Task HandleAsync(string uri);       // Incoming deep-link URI
    Task HandleFileAsync(Stream fileStream);
}
```

`HandleDeepLink` is called from `MauiProgram.cs` on app activation. The concrete `DeepLinkService` navigates to the target page after importing shared data.

---

## INavigationService Abstraction

```csharp
public interface INavigationService
{
    Task GoToAsync(string route);
    Task PopModalAsync();
    Task<bool> DisplayConfirmAsync(string title, string message, string accept, string cancel);
}
```

`ShellNavigationService` wraps `Shell.Current.GoToAsync()`. ViewModels resolve it via service locator (`IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>()`).

---

## Common Mistakes

| Mistake | Correct pattern |
|---|---|
| Tab 4 route as `"PrayerTimePage"` | Tab 4 is `Route="TagsPage"` (Tags). `PrayerTimePage` is a push route. |
| Tab 5 route as `"SettingsPage"` | Actual route is `Route="Settings"` (no "Page" suffix). |
| `IEditGuard`: `args.Cancel()` then re-navigate | Use `GetDeferral()` + `deferral.Complete()`. Cancel only when `CanLeaveAsync()` returns `false`. No re-navigate. |
| Modal without `IPageSheetModal` on iOS | Implement `IPageSheetModal` on the page class; `PageSheetPresentation` (on `Application.ModalPushing`) does the rest. |
| `PopToRoot` not guarded | `OnShellNavigating` includes `PopToRoot` — dirty pages are guarded on tab reselection too. |
| Using `nameof(Page)` in route strings inside VMs | VMs can't reference View types (test project won't compile). Use `Routes.*` constants instead. |
| Old `LoadAsync`/`RefreshAsync` split in `OnAppearing` | Use `PageSync.OnAppearingAsync(vm)` with an `ISyncableViewModel` instead. |

---

## Checklist: Adding a New Navigable Page

1. Create page XAML + code-behind (see `prayer-app-views` skill)
2. Create ViewModel (see `prayer-app-viewmodels` skill)
3. If push-navigable: add constant to `Routes.cs`, register in `AppShell.xaml.cs`
4. If tab: add `<ShellContent>` to `AppShell.xaml`
5. If modal on iOS/iPad: implement `IPageSheetModal` on the page class
6. If list page: implement `ISyncableViewModel`, call `PageSync.OnAppearingAsync(vm)` from `OnAppearing`, subscribe to relevant `EntityChangedMessage` types in the VM constructor
7. If form page: implement `IEditGuard` on the ViewModel
8. Register the page in DI if constructor-injected: `builder.Services.AddTransient<NewPage>()`
