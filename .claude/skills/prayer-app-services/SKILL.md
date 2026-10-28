---
name: prayer-app-services
description: Use when creating or modifying PrayerApp services, resolving cross-service dependencies, understanding cache patterns, IMessenger message-bus publishing, or auditing DI registration. Covers service constructors, public API return types, in-flight task coalescing, publishMessage cascade pattern, and three undocumented platform services (IShareService, IOrientationService, IColorPickerService). Keywords: IMessenger, WeakReferenceMessenger, PrayerChangedMessage, BulkChangedMessage, in-flight coalescing, publishMessage, IShareService, IOrientationService, IColorPickerService.
---

# PrayerApp Service Layer

All services live in `PrayerApp/Services/`, registered as singletons in `MauiProgram.cs`.

---

## When to Use

- Creating or modifying a service or its interface
- Resolving a cross-service dependency (which service depends on what)
- Understanding cache invalidation or the in-flight coalescer in `PrayerService`
- Publishing or subscribing to entity-change messages via `IMessenger`
- Auditing DI registration order or constructor signatures
- Using `IShareService`, `IOrientationService`, or `IColorPickerService`

---

## Quick Reference

| Service | Constructor deps | Return-type quirks |
|---------|------------------|--------------------|
| `CardService` | `IMessenger` only (no IDBService) | `SaveCardAsync(card, publishMessage = true)` → `Task<PrayerCard>` |
| `PrayerService` | `IDBService`, `IMessenger` | `SavePrayerAsync(prayer, publishMessage = true)` → `Task<Prayer>`; in-flight coalescer on `GetAllPrayersAsync` |
| `TagService` | `IDBService`, `IMessenger` | `GetRequestIdsByTagIdsAsync` → `Task<IReadOnlyList<int>>` (not `HashSet`) |
| `BoxService` | `IDBService`, `IPrayerService`, `ICardService`, `IMessenger` | `SaveBoxAsync` → `Task<CardBox>` |
| `BackupService` | `IDBService`, `ICardService`, `IPrayerService`, `ITagService`, `IBoxService`, `INotificationService`, `IPassphrasePrompt`, `IConfidentialAccessService`, `IMessenger` | `ExportAsync`/`ImportAsync` → `Task<bool>` |
| `DeepLinkService` | `INavigationService`, `IShareService`, `IImportPayloadService`, `Func<Page>` (ConfirmImportPage factory) | Registered via factory lambda |
| `PrayerInteractionService` | *(no args)* | — |
| `IShareService` / `IOrientationService` / `IColorPickerService` | Platform-specific impls | See §Platform Services |

---

## Cache Pattern

Every service with read operations follows the same null-means-stale pattern:

```csharp
public class MyService : IMyService
{
    private readonly IDBService _dbService;
    private readonly IMessenger _messenger;
    private IReadOnlyList<MyModel>? _cache;

    public MyService(IDBService dbService, IMessenger messenger)
    {
        _dbService = dbService;
        _messenger = messenger;
    }

    public async Task<IReadOnlyList<MyModel>> GetAllAsync()
    {
        if (_cache is not null) return _cache;
        _cache = (await MyModel.LoadAllAsync()).AsReadOnly();
        return _cache;
    }

    public async Task<MyModel> SaveAsync(MyModel item)
    {
        await item.SaveAsync();
        _cache = null;  // Invalidate — next read repopulates
        _messenger.Send(new MyModelChangedMessage(item.Id, ChangeKind.Updated));
        return item;
    }

    public void InvalidateCache() => _cache = null;
}
```

**Rules:**
- Cache field is nullable (`IReadOnlyList<T>?`), null means stale
- After any mutation (save/delete), set `_cache = null`, then publish via `_messenger`
- Return `IReadOnlyList<T>` (not `List<T>`) to prevent external mutation
- Expose `InvalidateCache()` for cross-service invalidation
- Works because services are singletons — only one cache instance per app lifetime

### PrayerService In-Flight Coalescer

`GetAllPrayersAsync` uses an extra `_allLoadTask` to prevent duplicate DB queries when multiple concurrent callers arrive while the cache is cold:

```csharp
private Task<List<Prayer>>? _allLoadTask;

public async Task<IReadOnlyList<Prayer>> GetAllPrayersAsync()
{
    if (_allCache is not null) return _allCache;
    var task = _allLoadTask ??= Prayer.LoadAllAsync();
    try
    {
        var list = await task;
        _allCache = list;
        return list;
    }
    finally
    {
        if (_allLoadTask == task) _allLoadTask = null;
    }
}
```

All concurrent callers await the same `_allLoadTask` instead of each issuing a separate DB read. `InvalidateCache()` nulls `_allLoadTask` along with `_allCache`.

---

## IMessenger Message-Bus Pattern

Services use `CommunityToolkit.Mvvm.Messaging.IMessenger` (registered as `WeakReferenceMessenger.Default`) to signal entity changes. ViewModels subscribe; services publish.

### Message types (`PrayerApp/Messages/EntityChangedMessage.cs`)

| Message | Published by | When |
|---------|-------------|------|
| `PrayerChangedMessage(PrayerId, CardId, Kind)` | `IPrayerService` | Save or Delete |
| `PrayerCardChangedMessage(CardId, Kind)` | `ICardService` | Save, Delete, or AssignBox |
| `CardBoxChangedMessage(BoxId, Kind)` | `IBoxService` | Save or Delete of the box itself |
| `TagChangedMessage(TagId, Kind)` | `ITagService` | Save or Delete of the tag itself |
| `BulkChangedMessage()` | `IBoxService`, `IBackupService`, `ITagService`, `ConfirmImportViewModel` | Any operation mutating many entities at once |
| `SessionRelockedMessage()` | `App.xaml.cs` | After `IConfidentialAccessService.RelockSession` on `Window.Deactivated` |

**`BulkChangedMessage` rule:** Send when a single logical operation mutates multiple entity types (backup restore, deep-link import, box cascade delete, tag color reassign). Subscribers respond with a full re-sync. **Never** fire granular per-entity messages alongside `BulkChangedMessage` — that causes N+1 resyncs.

### `publishMessage: false` cascade pattern

`SavePrayerAsync`, `DeletePrayerAsync`, `SaveCardAsync`, and `DeleteCardAsync` accept an optional `bool publishMessage = true`. Set it to `false` when calling from a cascade (e.g. `BoxService.DeleteBoxAsync`) so individual per-entity messages don't fan out alongside the single `BulkChangedMessage` that the cascade already sends:

```csharp
// BoxService.DeleteBoxAsync — cascade path
foreach (var prayer in prayers)
    await _prayerService.DeletePrayerAsync(prayer, publishMessage: false);
await _cardService.DeleteCardAsync(card, publishMessage: false);
// ... then send ONE BulkChangedMessage after everything is deleted
_messenger.Send(new BulkChangedMessage());
```

---

## DI Registration (`MauiProgram.CreateMauiApp`)

```csharp
// Cross-cutting messenger — published by services, subscribed by ViewModels
builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

builder.Services.AddSingleton<IDBService>(s => new DBService(dbPath));
builder.Services.AddSingleton<ICardService, CardService>();
builder.Services.AddSingleton<ITagService, TagService>();
builder.Services.AddSingleton<IPrayerService, PrayerService>();
builder.Services.AddSingleton<IPrayerInteractionService, PrayerInteractionService>();
builder.Services.AddSingleton<IBoxService, BoxService>();
builder.Services.AddSingleton<IPrayerSelectionService, PrayerSelectionService>();
builder.Services.AddSingleton<ILocalNotificationCenter, LocalNotificationCenterWrapper>();
builder.Services.AddSingleton<INotificationService>(sp =>
    new NotificationService(
        sp.GetRequiredService<ILocalNotificationCenter>(),
        () => PrayerApp.Services.Settings.AllowNotifications));
builder.Services.AddSingleton<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<IDiagnosticLog>(s => new DiagnosticLog(FileSystem.AppDataDirectory));
builder.Services.AddSingleton<IBackupService, BackupService>();
builder.Services.AddSingleton<IUserColorService, UserColorService>();
builder.Services.AddSingleton<ISettings, SettingsService>();
builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
builder.Services.AddSingleton<IAccessibilityService, MauiAccessibilityService>();
builder.Services.AddSingleton<IShareService, ShareService>();
builder.Services.AddSingleton<IBiometricAuthenticator, OscoreBiometricAuthenticator>();
builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
builder.Services.AddSingleton<IPinPrompt, MauiPinPrompt>();
builder.Services.AddSingleton<IConfidentialAccessService, ConfidentialAccessService>();
builder.Services.AddSingleton<IPassphrasePrompt, MauiPassphrasePrompt>();
builder.Services.AddSingleton<IDeepLinkService>(sp => new DeepLinkService(
    sp.GetRequiredService<INavigationService>(),
    sp.GetRequiredService<IShareService>(),
    sp.GetRequiredService<IImportPayloadService>(),
    () => sp.GetRequiredService<PrayerApp.Views.ConfirmImportPage>()));
builder.Services.AddSingleton(sp => new ShareHandoffService(/* ISettings, IClipboardLinkDetector, INavigationService, IDeepLinkService, IOnboardingService, clipboard reader */));
builder.Services.AddSingleton<IImportPayloadService, ImportPayloadService>();
builder.Services.AddSingleton<ITextSelectionParser, TextSelectionParser>();

// Platform-specific — registered conditionally per platform
#if ANDROID
builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.Android.OrientationService>();
builder.Services.AddSingleton<IClipboardLinkDetector, PrayerApp.Platforms.Android.ClipboardLinkDetector>();
builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.Android.ColorPickerService>();
#elif IOS
builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.iOS.OrientationService>();
builder.Services.AddSingleton<IClipboardLinkDetector, PrayerApp.Platforms.iOS.ClipboardLinkDetector>();
builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.iOS.ColorPickerService>();
builder.Services.AddSingleton<IAppGroupContainerProvider, PrayerApp.Platforms.iOS.NsFileManagerAppGroupContainerProvider>();
builder.Services.AddSingleton(sp => new AppGroupImportOrchestrator(/* IAppGroupContainerProvider, IImportPayloadService, INavigationService, ConfirmImportPage factory */));
#endif
```

---

## Cross-Service Dependency Graph

```
BoxService      ──→ IDBService, IPrayerService, ICardService, IMessenger
BackupService   ──→ IDBService, ICardService, IPrayerService, ITagService,
                    IBoxService, INotificationService, IPassphrasePrompt,
                    IConfidentialAccessService, IMessenger
DeepLinkService ──→ INavigationService, IShareService, IImportPayloadService,
                    Func<Page> (ConfirmImportPage factory)
CardService     ──→ IMessenger only  (no IDBService — reads via PrayerCard model)
PrayerService   ──→ IDBService, IMessenger
TagService      ──→ IDBService, IMessenger
PrayerInteractionService ──→ (no injected deps)
NotificationService ──→ ILocalNotificationCenter, Settings.AllowNotifications (func)
OnboardingService ──→ ISettings
ConfidentialAccessService ──→ IBiometricAuthenticator, ISecureStore, IPinPrompt
UserColorService ──→ IDBService
```

---

## Complete Service API Reference

### IPrayerService (`PrayerApp/Services/IPrayerService.cs`)

```csharp
Task<IReadOnlyList<Prayer>> GetPrayersByCardAsync(int prayerCardId);
Task<IReadOnlyList<Prayer>> GetAllPrayersAsync();           // in-flight coalescer
Task<IReadOnlyList<Prayer>> GetAllActivePrayersAsync();     // Filters IsAnswered!=true
Task<IReadOnlyList<Prayer>> GetOverduePrayersAsync(int dayThreshold = 30);
Task<IReadOnlyList<Prayer>> GetAnsweredOnThisDateAsync();   // Same month+day, prior years
Task<int> GetActivePrayerCountByCardAsync(int cardId);      // Routes through _allCache
Task<DateTime?> GetLastInteractionDateAsync();
Task<int> GetInteractionCountByPrayerAsync(int prayerId);
Task<Prayer> SavePrayerAsync(Prayer prayer, bool publishMessage = true);  // Returns saved entity
Task DeletePrayerAsync(Prayer prayer, bool publishMessage = true);
void InvalidateCache();
```

**Cache:** Two caches — `_cardCache` (`Dictionary<int, List<Prayer>>`) and `_allCache` (`List<Prayer>`). Both nulled on mutation. `GetActivePrayerCountByCardAsync` deliberately routes through `_allCache` so N per-card calls collapse to one DB read.

**Cascading delete** (inside `DeletePrayerAsync`):
```csharp
await _dbService.DeleteInteractionsByPrayerIdAsync(prayer.Id);
await _dbService.DeleteJunctionRowsByRequestIdAsync(prayer.Id);
await prayer.DeleteAsync();
```

### ICardService (`PrayerApp/Services/ICardService.cs`)

```csharp
Task<IReadOnlyList<PrayerCard>> GetCardsAsync();
Task<PrayerCard> GetOrCreateQuickAddCardAsync();
Task<PrayerCard> GetOrCreateSharedCardAsync();
Task<PrayerCard> SaveCardAsync(PrayerCard card, bool publishMessage = true);  // Returns saved entity
Task DeleteCardAsync(PrayerCard card, bool publishMessage = true);
Task AssignBoxAsync(PrayerCard card, int boxId);            // boxId=0 = Unboxed
void InvalidateCache();
```

**Note:** `CardService` injects only `IMessenger` — it reads via the `PrayerCard` Active Record model directly, not via `IDBService`.

**Constants:** `QuickAddTitle = "Quick Add"`, `SharedWithMeTitle = "Shared with me"`

### IBoxService (`PrayerApp/Services/IBoxService.cs`)

```csharp
Task<IReadOnlyList<CardBox>> GetBoxesAsync();              // Sorted: SortOrder → Name
Task<CardBox?> GetSystemBoxAsync(string systemKey);
Task<CardBox> SaveBoxAsync(CardBox box);                   // Guards system boxes from rename
Task DeleteBoxAsync(int boxId, bool deleteCards);          // Cascade or unassign
Task SeedSystemBoxesAsync();
Task<bool> HasConfidentialCardsAsync();
void InvalidateCache();
```

**Cascade delete** — `publishMessage: false` on inner calls; single `BulkChangedMessage` at the end:
```csharp
await _prayerService.DeletePrayerAsync(prayer, publishMessage: false);
await _cardService.DeleteCardAsync(card, publishMessage: false);
// After all cards/prayers deleted:
_messenger.Send(new BulkChangedMessage());
```

### ITagService (`PrayerApp/Services/ITagService.cs`)

```csharp
Task<IReadOnlyList<PrayerTag>> GetTagsAsync();             // Sorted: IsSystem desc, Name asc
Task<IReadOnlyList<PrayerTag>> GetTagsByRequestIdAsync(int prayerRequestId);
Task<int> AddTagToRequestAsync(int prayerRequestId, int prayerTagId);
Task<int> RemoveTagFromRequestAsync(int prayerRequestId, int prayerTagId);
Task<IReadOnlyList<int>> GetRequestIdsByTagIdsAsync(IEnumerable<int> tagIds);  // NOT HashSet
Task<PrayerTag> SaveTagAsync(PrayerTag tag);
Task DeleteTagAsync(int tagId);                            // Guards system tags
Task ReassignColorAsync(string oldColorHex, string newColorHex);
Task ClearAllAssignmentsForTagAsync(int tagId);
Task SeedSystemTagsAsync();
Task<PrayerTag?> GetSystemTagAsync(string name);           // Case-insensitive
void InvalidateCache();
```

**System tag constants:** `RecentlyNotifiedTagName = "Recently Notified"`, `RecentlyNotifiedTagColor = "#505050"`

### IPrayerInteractionService (`PrayerApp/Services/IPrayerInteractionService.cs`)

```csharp
Task LogInteractionAsync(int prayerId);  // Writes PrayerInteraction(InteractionType="Prayed")
```

`PrayerInteractionService` has no constructor args — it writes directly via the `PrayerInteraction` Active Record model.

### IUserColorService (`PrayerApp/Services/IUserColorService.cs`)

```csharp
Task<IReadOnlyList<UserColor>> GetColorsAsync();           // Ordered by CreatedAt
Task<UserColor> SaveColorAsync(string hexValue);           // Uppercase + dedup
Task DeleteColorAsync(int id);                             // Guards default colors
Task SeedDefaultsAsync();
string GetFirstDefaultHex();                               // Returns "#B84040"
```

**Default palette:** `#B84040`, `#B35A20`, `#7A4020`, `#1E7870`, `#2E5A9A`, `#663C8C`, `#8C3860`, `#505050`

### INotificationService (`PrayerApp/Services/INotificationService.cs`)

```csharp
Task<bool> RequestPermissionAsync();
Task<bool> AreNotificationsEnabledAsync();
Task ClearAllAsync();
Task ScheduleAsync(Prayer prayer);
Task CancelAsync(int notificationId, PrayerFrequency frequency = PrayerFrequency.OneTime);
Task RenewMonthlyNotificationsAsync(IEnumerable<Prayer> activePrayers);
Task ReconcileNotificationsAsync(IEnumerable<Prayer> activePrayers);
```

### IOnboardingService

- Tutorial sequence `OnboardingStep.Welcome` through `PrayerTimeHighlight`, persisted via `ISettings.OnboardingStep` / `OnboardingComplete`
- Properties: `CurrentStep`, `IsActive`, `IsDeepLinkSession`, `WelcomeShownThisSession`
- Events: `StepChanged`
- Methods: `Advance()`, `Skip()`, `Reset()`, `MarkWelcomeShown()`, `MarkDeepLinkSession()`

### IBackupService

```csharp
Task<bool> ExportAsync();   // Close DB → ZIP → share via OS share sheet
Task<bool> ImportAsync();   // Import from ZIP → replace DB → BulkChangedMessage
```

---

## Platform Services

Three interfaces have platform-specific implementations registered via `#if ANDROID / IOS`:

### IShareService (`PrayerApp/Services/IShareService.cs`)

```csharp
Task ShareTextAsync(string title, string text);
Task ShareFileAsync(string title, string filePath, string mimeType);
```

Wraps the OS-native share sheet. Used by `DeepLinkService` (text share and `.prayercard` file share). `BackupService` calls MAUI `Share.RequestAsync` directly.

### IOrientationService (`PrayerApp/Services/IOrientationService.cs`)

```csharp
void LockLandscape();
void LockPortrait();
void Unlock();
void ApplyDefault();   // idiom policy: tablets rotate, phones portrait
```

`ApplyDefault()` follows `OrientationPolicy.AllowsRotation(DeviceIdiom)` (`PrayerApp/Services/OrientationPolicy.cs`, unit-tested): `Tablet` unlocks, every other idiom locks portrait. MainPage.OnAppearing and PrayerTimePage.OnDisappearing call it; PrayerTimePage.OnAppearing calls `LockLandscape()`. Never lock portrait for every idiom: Play flags a manifest `screenOrientation`, and Android 16 ignores it on screens 600dp and wider anyway (#261). Android applies the policy in `MainActivity.OnCreate` before `base.OnCreate`; iOS sets it in the `OrientationService` initializer and asks UIKit to re-read allowed orientations on change.

### IColorPickerService (`PrayerApp/Services/IColorPickerService.cs`)

```csharp
Task<string?> PickColorAsync();  // Returns hex e.g. "#B84040", or null if cancelled
```

iOS: native `UIColorPickerViewController`. Android: hex-entry popup.

---

## System Entity Guards

| Service | Guard | Behavior |
|---------|-------|----------|
| `PrayerCardViewModel` | System cards | `DeleteAsync`, `ArchiveAsync`, `ShareAsync` return early when `IsSystem == true` (`CardService` does not guard) |
| `BoxService` | System boxes | Cannot rename/delete boxes where `IsSystem == true` |
| `TagService` | System tags | Cannot delete tags where `IsSystem == true` |
| `UserColorService` | Default colors | Cannot delete colors where `IsDefault == true` |

---

## Common Mistakes

| Mistake | Correct |
|---------|---------|
| `SavePrayerAsync` returns `Task` | Returns `Task<Prayer>` |
| `SaveCardAsync` returns `Task` | Returns `Task<PrayerCard>` |
| `GetRequestIdsByTagIdsAsync` returns `HashSet<int>` | Returns `Task<IReadOnlyList<int>>` |
| `CardService` injects `IDBService` | Injects `IMessenger` only |
| New service omits `IMessenger` | All mutating services inject and use `IMessenger` |
| Cascade sends per-entity messages + `BulkChangedMessage` | Use `publishMessage: false` on inner calls; send only `BulkChangedMessage` at the end |
| Simple null-check cache in `PrayerService.GetAllPrayersAsync` | Uses `_allLoadTask ??= ...` in-flight coalescer |

---

## Checklist: Adding a New Service

1. Create `PrayerApp/Services/INewService.cs` (interface)
2. Create `PrayerApp/Services/NewService.cs` — inject `IMessenger`; implement cache pattern
3. Register in `MauiProgram.cs`: `builder.Services.AddSingleton<INewService, NewService>();`
4. Publish the appropriate `*ChangedMessage` or `BulkChangedMessage` on every mutation
5. Add `<Compile Include>` entries in `PrayerApp.Tests/PrayerApp.Tests.csproj`
6. Write service tests in `PrayerApp.Tests/Services/NewServiceTests.cs`
