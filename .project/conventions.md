# Conventions

<!--
Project doc (.project/). Cite as `.project/conventions.md#<section>`. This is the file the
implementer and coherence-reviewer lean on hardest — "reuse conventions" and
"does this fit the app?" both resolve here. Prefer pointing at a canonical
exemplar in the codebase (path:line) over prose. Keep ## headings stable — they
are citation anchors.
-->

## Naming
Files, types, functions, tests, branches.
> PascalCase types; `*ViewModel` / `*Page` / `*Service` suffixes; SQLite table names via `[Table("…")]`. Tests mirror the unit name + `Tests`. Branches: `feature/*`, `fix/*`, `release/*` off `dev`; `hotfix/*` off `master` (see CONTRIBUTING.md). Source files are UTF-8 without a BOM.

## File & folder layout
Where things go, and the shape of a feature.
> `PrayerApp/` holds `Views/`, `ViewModels/`, `Services/`, `Models/`, `Behaviors/`, `Converters/`, `Helpers/`, `Messages/`, `Resources/Styles/`, `Platforms/`. Views are grouped by feature (`Views/Prayer`, `Views/PrayerCard`, `Views/PrayerTime`, `Views/Tags`, `Views/Boxes`, `Views/Confidential`, `Views/Settings`, `Views/Onboarding`, `Views/Backup`, plus shared controls in `Views/Shared`). Unit tests in `PrayerApp.Tests/` (source linked via `<Compile Include>`); Appium E2E in `PrayerApp.UITests/`.

## Test patterns
Where tests live, how they're named, fixtures/factories, and what a good test looks like.
> `PrayerApp.Tests` targets `net10.0` and LINKS source files from the main project (not a project reference — the main project targets mobile only); add a matching `<Compile Include>` for each new non-MAUI service or model. NSubstitute mocks `IDBService`; `SetDBService()` runs in each test constructor. xUnit, parallelization disabled. TDD red→green for new behavior. Run: `dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj`.

## Canonical exemplars (mirror these)
The reference implementations to copy when building something similar. Point at real code.

| For… | Mirror | Notes |
|---|---|---|
| A new page + ViewModel | `PrayerApp/Views/Prayer/PrayerListPage.xaml` (+ `.xaml.cs` + ViewModel) | PageSync OnAppearing load; ISyncableViewModel; compiled bindings (`x:DataType`) |
| A grouped / card CollectionView | `PrayerApp/Views/PrayerCard/PrayerCardsPage.xaml` | CollectionView grouping; `PrayerCardBorder`; empty / loading states |
| A service (cache + message bus) | `PrayerApp/Services/` (singleton services) | `_cache` nulled on write; WeakReferenceMessenger publish cascade |
| A settings / form page | `PrayerApp/Views/Settings/AppSettingsPage.xaml` | label-above-field, full-width forms |

## Commits & PRs
Message format and PR expectations.
> Squash-merge, one commit per PR — the PR title/description become the landed commit message (write them release-quality). `feature/*` → PR `--base dev`; the `Unit Tests` CI check must pass. `master` and `dev` are branch-protected (no direct push). `dev` is the default branch, so `Closes #N` auto-closes the issue on merge to `dev`. No Claude co-authoring on public-facing commits, PRs, or issues. See CONTRIBUTING.md.

## Versioning
Does the project follow semantic versioning? If so, **where the version lives** (e.g. `pyproject.toml`, `package.json`, `*.csproj`, a `VERSION` file) and the **bump cadence** (per feature / milestone). When semver is on, `milestone-driver` applies the bump per PR and `milestone-feeder` names milestones as versions so the driver can derive the target.
> SemVer. The display version lives in `Directory.Build.props` (`ApplicationDisplayVersion`) and is bumped manually at release milestones; the build number auto-stamps from `git rev-list --count HEAD` (a shallow clone fails loudly) into the binary (`ApplicationVersion`) and is **not** part of the tag. Release record is canonical in GitHub: tag the marketing version with a `v` prefix (e.g. `v1.7.2`; tags before `v1.7.1` are unprefixed) on `master`, publish a GitHub Release, and close the milestone. Note: milestone-driver's auto-bump targets `.claude-plugin/plugin.json`, which this app does not use — version bumps here are manual.
