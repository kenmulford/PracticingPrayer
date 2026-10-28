---
name: prayer-app-unit-testing
description: Use when writing, fixing, or extending xUnit tests in PrayerApp.Tests — adding Compile Include entries, wiring IMessenger/WeakReferenceMessenger fixtures, setting up SetDBService for Active Record models, constructing ViewModels or Services under test, running dotnet test (xUnit 2 on VSTest), or following TDD-first workflow for new behavior.
---

# PrayerApp Unit Testing

Test project: `PrayerApp.Tests/` targeting `net10.0` with xUnit 2.9.2 (VSTest runner via `xunit.runner.visualstudio`), NSubstitute 5.3.0, and CommunityToolkit.Mvvm 8.4.0.

**TDD-first workflow:** For any new behavior, invoke `superpowers:test-driven-development` before writing implementation code. Test-after is only acceptable for pure refactors with green coverage.

---

## When to Use

- Adding or fixing tests for a Service, ViewModel, or Model
- Wiring up a new test fixture (constructor setup, mocks, messenger)
- Adding `<Compile Include>` for a new testable class
- Running `dotnet test`, including filtered runs
- Verifying messenger publish assertions

---

## Quick Reference

| Test class type | Key fixture elements |
|---|---|
| Service (e.g. `PrayerServiceTests`) | `IDBService` mock, `Model.SetDBService`, fresh `WeakReferenceMessenger`, captured message list, `new ServiceClass(_db, _messenger)` |
| ViewModel (e.g. `PrayerCardViewModelTests`) | All service mocks (including `ISettings`, `IConfidentialAccessService`), `Model.SetDBService` × N, `CardBox.SetDBService`, `CreateSut()` factory |
| Model (Active Record) | `IDBService` mock, `Model.SetDBService(_db)`, direct `model.SaveAsync()` / `LoadAsync()` calls |

---

## Project Configuration

### Why Linked Source Files

The main project targets `net10.0-android;net10.0-ios` — these TFMs cannot run on desktop. The test project targets plain `net10.0` and links app source files via `<Compile Include>` instead of a project reference to `PrayerApp`. It does take a `<ProjectReference>` to `PrayerApp.Shared`.

### Adding a New Testable Class

Add bare `<Compile Include>` entries to `PrayerApp.Tests.csproj` — no `Link=` attribute:

```xml
<Compile Include="../PrayerApp/Models/NewModel.cs" />
<Compile Include="../PrayerApp/Services/INewService.cs" />
<Compile Include="../PrayerApp/Services/NewService.cs" />
<Compile Include="../PrayerApp/ViewModels/NewViewModel.cs" />
```

### What Is Included vs Excluded

**Included** (no MAUI platform dependencies):
- All Models: `Prayer`, `PrayerCard`, `CardBox`, `PrayerTag`, `PrayerCardTag`, `PrayerInteraction`, `UserColor`, `ParseResult`
- Enums: `PrayerFrequency`, `OnboardingStep`, `CardProtectionMode`
- Helpers: `TaskExtensions`, `NotificationHelper`, `TagColorPalette`, `BoxStrings`, `SingleFlightGate`, `PerfLog`, `Diagnostics`, `NativeAttachGuard`, `PopupCloseExtensions`, `PopupResultGate`, `TextNormalization`
- Behaviors: `KeyboardAvoidanceBehavior`, `KeyboardLiftBehavior`, `ChipStripHeightBehavior`, `AccessibilityScaleBehavior`
- Messages: `EntityChangedMessage`
- All Service interfaces and the MAUI-free implementations: `CardService`, `PrayerService`, `TagService`, `PrayerInteractionService`, `UserColorService`, `BoxService`, `DeepLinkService`, `NotificationService`, `OnboardingService`, `ConfidentialAccessService`, and the import/share services
- All testable ViewModels (20 total): includes `BoxSectionViewModel`, `BoxesViewModel`, `BoxDetailViewModel`, `PrayerTimeBoxScopeViewModel`, `TagFilterChipViewModel`, `TagChipViewModel`

**Excluded** (require MAUI runtime):
- `DBService.cs` — native SQLite runtime
- `Settings.cs` — MAUI Preferences
- `LocalNotificationCenterWrapper.cs` — Plugin.LocalNotification
- `OrientationService` — platform-specific
- All Views (Shell/Application dependencies), except the pure layout files `CardPanelLayout`, `IPageSheetModal`, `PageSheetPresentation`
- `BackupService.cs` (FileSystem/Share/Shell)

---

## Test Parallelization

```csharp
// PrayerApp.Tests/TestCollectionDefinition.cs
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
```

**Why:** Model classes use a static `_dbService` field set via `SetDBService()`. Parallel tests race on this shared state.

---

## IMessenger / WeakReferenceMessenger Pattern

The dominant fixture pattern across 11+ test files. **Always create a fresh `WeakReferenceMessenger` per fixture — never use `.Default`**, which leaks state across tests.

Services that publish messages (`PrayerService`, `BoxService`, etc.) take `IMessenger` as a constructor argument. Capture published messages in a `List<T>` registered during fixture construction.

```csharp
private readonly IMessenger _messenger = new WeakReferenceMessenger(); // fresh per fixture
private readonly object _recipient = new();
private readonly List<PrayerChangedMessage> _prayerMessages = new();

public PrayerServiceTests()
{
    _db = Substitute.For<IDBService>();
    Prayer.SetDBService(_db);
    _messenger.Register<object, PrayerChangedMessage>(_recipient, (_, m) => _prayerMessages.Add(m));
    _service = new PrayerService(_db, _messenger);  // two-arg constructor
}
```

Assert on the captured list to verify messenger publishes:

```csharp
[Fact]
public async Task SavePrayerAsync_New_PublishesCreated()
{
    var prayer = new Prayer { PrayerCardId = 3, Title = "Aunt" };
    await _service.SavePrayerAsync(prayer);

    Assert.Single(_prayerMessages);
    Assert.Equal(ChangeKind.Created, _prayerMessages[0].Kind);
}
```

---

## ViewModel Test Template

ViewModels and services are the main test targets (22 ViewModel test files, 23 service test files). Use a `CreateSut()` factory method.

```csharp
public class PrayerCardViewModelTests
{
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly IOnboardingService _onboardingService = Substitute.For<IOnboardingService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IConfidentialAccessService _confidentialAccessService = Substitute.For<IConfidentialAccessService>();
    private readonly IDBService _db = Substitute.For<IDBService>();

    public PrayerCardViewModelTests()
    {
        PrayerCard.SetDBService(_db);   // Active Record — required for each model used
        Prayer.SetDBService(_db);
        CardBox.SetDBService(_db);      // required when CardBox is involved
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());
    }

    private PrayerCardViewModel CreateSut() =>
        new(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, _boxService,
            _settings, _confidentialAccessService); // 8 args

    [Fact]
    public async Task SaveCommand_NewCard_InsertsAndNavigatesBack()
    {
        var sut = CreateSut();
        sut.Title = "New Card";
        _db.InsertAsync(Arg.Any<PrayerCard>()).Returns(1);

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _db.Received(1).InsertAsync(Arg.Any<PrayerCard>());
        await _navigationService.Received(1).GoToAsync(Arg.Any<string>());
    }
}
```

---

## Service Test Template

```csharp
public class PrayerServiceTests
{
    private readonly IDBService _db;
    private readonly IMessenger _messenger = new WeakReferenceMessenger();
    private readonly object _recipient = new();
    private readonly List<PrayerChangedMessage> _prayerMessages = new();
    private readonly PrayerService _service;

    public PrayerServiceTests()
    {
        _db = Substitute.For<IDBService>();
        Prayer.SetDBService(_db);
        _messenger.Register<object, PrayerChangedMessage>(_recipient, (_, m) => _prayerMessages.Add(m));
        _service = new PrayerService(_db, _messenger);
    }

    [Fact]
    public async Task GetAllPrayersAsync_SecondCall_UsesCacheNotDatabase()
    {
        _db.GetAllAsync<Prayer>().Returns(Task.FromResult(new List<Prayer>()));

        await _service.GetAllPrayersAsync();
        await _service.GetAllPrayersAsync();

        await _db.Received(1).GetAllAsync<Prayer>(); // cache served second call
    }
}
```

---

## NSubstitute Patterns

```csharp
// Returns
service.GetAllAsync().Returns(new List<MyModel>());
_db.InsertAsync(Arg.Any<Prayer>()).Returns(1);  // new ID

// Argument matching
Arg.Any<Prayer>()
Arg.Is<Prayer>(p => p.Title == "Test")

// Received (verify calls)
await _db.Received(1).InsertAsync(Arg.Any<Prayer>());
await _db.DidNotReceive().DeleteAsync(Arg.Any<PrayerCard>());

// AsyncRelayCommand execution
await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
sut.ToggleFavoriteCommand.Execute(null); // ICommand.Execute does not await an AsyncRelayCommand
```

---

## Test Naming and Organization

Pattern: `[MethodUnderTest]_[Scenario]_[ExpectedResult]`

```
GetAllPrayersAsync_FirstCall_QueriesDatabase
SaveCommand_SavesAndNavigatesBack
DeleteCommand_SystemCard_NoOp
CanLeaveAsync_Dirty_ShowsConfirm
```

Group tests with comment banners:

```csharp
// ── GetAllAsync ──────────────────────────

[Fact]
public async Task GetAllAsync_EmptyDb_ReturnsEmpty() { }

// ── SaveAsync ────────────────────────────

[Fact]
public async Task SaveAsync_InvalidatesCache() { }
```

---

## Running Tests

The project runs xUnit 2 on VSTest, so classic flags work. Run from the repo root:

```
dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj
dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj --filter "FullyQualifiedName~PrayerServiceTests"
```

---

## Known Gap

`CancellationToken` test coverage is zero — VMs introduced in slices 6a/6b accept tokens but no tests verify cancellation paths. Flag as a known gap before adding cancellation-dependent behavior.

---

## Common Mistakes

| Mistake | Fix |
|---|---|
| `Link="Models\NewModel.cs"` in `<Compile Include>` | Remove `Link=` — bare `Include` only |
| `new PrayerService(_db)` | Constructor is `(IDBService, IMessenger)` — pass both |
| `new PrayerCardViewModel(6 args)` | Constructor takes 8 args; add `_settings` and `_confidentialAccessService` |
| Missing `CardBox.SetDBService(_db)` in VM fixture | Add alongside `PrayerCard.SetDBService` and `Prayer.SetDBService` |
| `WeakReferenceMessenger.Default` in fixture | Use `new WeakReferenceMessenger()` — fresh per fixture to prevent cross-test leakage |
| Asserting messenger via `_service.Received()` | Services don't expose messenger — capture messages in a registered `List<T>` |

---

## Checklist: Adding Tests for a New Class

1. Add bare `<Compile Include>` entries in `PrayerApp.Tests.csproj` (no `Link=`)
2. Create `PrayerApp.Tests/Services/NewServiceTests.cs` or `ViewModels/NewViewModelTests.cs`
3. Wire `IDBService` mock + `Model.SetDBService(_db)` for every model the class touches
4. If the class takes `IMessenger`, create a fresh `WeakReferenceMessenger` and register capture lists
5. Use `[Fact]` for each test; follow naming convention
6. For services: test cache invalidation and messenger publishes
7. For ViewModels: test command execution, navigation, and dirty-state tracking
8. Run: `dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj`
