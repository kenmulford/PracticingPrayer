# Practicing Prayer — Architecture

> Architecture, conventions, and known gotchas for the Practicing Prayer codebase.

---

## Vision

- Digital tool for personal prayer/worship — prayer cards, requests, prayer time sessions
- Privacy-first: fully offline, no accounts, no backend, no social features
- Will NOT integrate with Church CRM/ChMS systems — others don't need access to this data
- Core loop: **prayer cards → prayer requests → prayer time**. Everything else supports this.
- Tags, colors, and organization features serve user personalization — not the core
- Audience: niche app store listing for anyone seeking a prayer journaling tool
- May integrate with strategic online tools that deepen the experience without violating privacy-first principle
- Priorities: security, stability, UX above all

---

## Tech Stack

| Package                  | Version | Notes                                                            |
| ------------------------ | ------- | ---------------------------------------------------------------- |
| .NET MAUI                | 10      | `net10.0-android`, `net10.0-ios`                                 |
| CommunityToolkit.Mvvm    | 8.4.1   | `ObservableObject` only — explicit `SetProperty` + `new (Async)RelayCommand`; no source generators |
| CommunityToolkit.Maui    | 14.0.1  | Requires `Microsoft.Maui.Controls >= 10.0.41` — do not downgrade |
| sqlite-net-base          | 1.9.172 | Active Record model pattern; with SQLitePCLRaw.bundle_e_sqlite3 3.0.3 |
| Plugin.LocalNotification | 14.0.0  | Local push notifications                                         |
| Oscore.Maui.Biometric    | 2.5.1   | Biometric unlock for Confidential Cards                          |
| xUnit                    | 2.9.2   | Unit tests                                                       |
| NSubstitute              | 5.3.0   | Test mocking                                                     |

---

## Architecture

```
Views (XAML)
  └── ViewModels (CommunityToolkit.Mvvm)
        └── Services (singletons, cached)
              └── Models (Active Record, SQLite)
                    └── IDBService / DBService
```

- **Models** carry persistence via a static `IDBService` reference set in `MauiProgram.cs`. Each model has `SaveAsync()`, `DeleteAsync()`, `LoadAsync(id)`, `LoadAllAsync()`, plus type-specific queries.
- **Services** are registered as singletons. Cache pattern: read → populate `_cache` → return; write → null `_cache` → return.
- **ViewModels** take services by constructor injection (registered `AddTransient` in `MauiProgram.cs`) and keep a parameterless constructor that chains to it via `IPlatformApplication.Current!.Services.GetRequiredService<>()`, so unit tests inject fakes and XAML can still construct them.
- **Messaging**: services publish the records in `Messages/EntityChangedMessage.cs` through the injected `IMessenger`; ViewModels re-sync on them.
- **Navigation**: Shell with 5 tabs (Home, Cards, Prayers, Tags, Settings). ViewModels navigate and show dialogs through `INavigationService` (`ShellNavigationService`), whose `PushModalOnUiThreadAsync` / `PushModalWithNavigationBarAsync` wrap `Shell.Current.Navigation.PushModalAsync`. Route params via `IQueryAttributable.ApplyQueryAttributes()`.

---

## Database Schema

| Table               | Key columns                                                                                                     |
| ------------------- | --------------------------------------------------------------------------------------------------------------- |
| `PrayerCard`        | Id, Title, CanNotify, PrayerFrequency, IsAnswered, IsFavorite, IsSystem, IsImported, ProtectionMode, BoxId, PreArchiveBoxId, SystemKey, CreatedAt, UpdatedAt |
| `PrayerRequest`     | Id, PrayerCardId (FK), Title, Details, CanNotify, PrayerFrequency, NotifyHour, NotifyMinute, NotifyDayOfWeek, NotifyDayOfMonth, NotifyDate, IsImported, IsAnswered, AnsweredAt, CreatedAt, UpdatedAt |
| `PrayerTag`         | Id, Name, Color (hex, nullable), IsSystem, CreatedAt, UpdatedAt                                                 |
| `PrayerCardTag`     | Id, PrayerCardId (FK), PrayerTagId (FK), PrayerRequestId (FK), CreatedAt                                        |
| `PrayerInteraction` | Id, PrayerId (FK), InteractionType, InteractionAt, CreatedAt, UpdatedAt                                         |
| `CardBox`           | Id, Name, IsSystem, SystemKey, SortOrder, ProtectAllCards, CardProtectionMode, CreatedAt, UpdatedAt             |
| `UserColor`         | Id, HexValue, IsDefault, CreatedAt                                                                              |

- The `Prayer` model uses `[Table("PrayerRequest")]` — the SQLite table is `PrayerRequest`, not `Prayer`
- Schema migrations live in `DBService.UpdateSchema()`

---

## Conventions

### Git

- Feature branches off `dev`, PRs target `dev`, `master` updated at release milestones
- Branch naming: `feature/<short-name>` (e.g., `feature/f7-home-personalization`)

### Styling

- Full light and dark mode support via `AppThemeBinding` — never hardcode hex in XAML or C#
- Use `StaticResource` color tokens from `Resources/Styles/Colors.xaml`
- Card borders: `Style="{StaticResource PrayerCardBorder}"` on every card-wrapping `Border`
- Named styles for reusable patterns in `Resources/Styles/Styles.xaml`
- Any time a new style is defined in code, always verify the key exists in Styles.xaml or Colors.xaml.

### Forms

- Label above field, both full-width (`VerticalStackLayout` with `FormLabel` above input)
- Never use side-by-side label+field layout

---

## Testing

- `PrayerApp.Tests/` targets `net10.0` — links source files from main project (not project reference, since main targets mobile only)
- NSubstitute mocks `IDBService`; static `SetDBService()` called per-test constructor
- Parallelization disabled (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) — model static `_dbService` is shared state
- When adding a new non-MAUI service/model, add a matching `<Compile Include>` in `PrayerApp.Tests.csproj`
- CI: `.github/workflows/ci.yml` runs `Unit Tests` and `Android Build` on push to `master`/`dev`/`feature/**`/`fix/**`/`release/**`/`hotfix/**` and on PRs to `master`/`dev`; `.github/workflows/release.yml` ships to Play and TestFlight
- Dependabot (`.github/dependabot.yml`) opens weekly grouped minor/patch PRs against `dev` for NuGet, Bundler and GitHub Actions
- Running the Appium UI test suite is documented in [docs/RUNNING_UITESTS.md](docs/RUNNING_UITESTS.md)

---

## Known Gotchas

| Gotcha                             | Detail                                                                       |
| ---------------------------------- | ---------------------------------------------------------------------------- |
| `Shell.PushModalAsync` removed     | Use `Shell.Current.Navigation.PushModalAsync(page)`                          |
| `DisplayAlert` deprecated          | Use `DisplayAlertAsync` instead                                              |
| Timers must use `IDispatcherTimer` | Not `System.Timers.Timer` — MAUI requires UI thread timers                   |
| Package version lock               | `CommunityToolkit.Maui 14.0.1` requires `Microsoft.Maui.Controls >= 10.0.41` |
| Prayer table name mismatch         | `Prayer` model → `[Table("PrayerRequest")]` SQLite table                     |
| SQLite page size warning           | XA0141 re: Android 16 16KB pages — third-party issue, no action needed       |
| PAT scope for CI                   | Pushing `.github/workflows/` requires `workflow` scope on GitHub PAT         |
| BindableLayout + AppThemeBinding   | `AppThemeBinding` inside a keyed Style doesn't reliably propagate to all BindableLayout children. Use inline color bindings for BoxView/dividers inside BindableLayout DataTemplates. Exception to the "no inline colors" rule. |
| Cross-context bindings (XC0045/XC0023) | Never use `x:DataType="{x:Null}"` — trades one warning for another across MAUI versions. Use code-behind `Tapped` handler + pattern-match `BindingContext` instead. See `TagPickerPage.xaml.cs OnSuggestionTapped`. |

---

## Key Files

- `docs/plans/` — active screenshot runbooks (completed plans archived to `docs/archive/`)
- `docs/plans/app-store-screenshots.md` — iOS screenshot runbook (devices, seed data, capture commands)
- `docs/plans/UX-22-android-screenshots.md` — Android screenshot runbook (emulators, seed data, adb commands)
- `screenshots/` (gitignored, local only, because captures are build artifacts): app store screenshots and `android/prayer_app_seed.db`
- `docs/research/` — investigation notes
- `docs/RUNNING_UITESTS.md` — Appium UI test suite setup and running guide
- `docs/archive/` — completed plan/spec docs (historical reference)
