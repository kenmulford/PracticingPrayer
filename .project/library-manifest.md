# Library manifest

<!--
Project doc (.project/). Cite as `.project/library-manifest.md#<section>`. The
implementer's "new dependency = PAUSE" gate reads this; the coherence-reviewer
flags a new library that duplicates one listed here. Keep it current. Keep ##
headings stable — they are citation anchors.
-->

## Runtime & frameworks
The platform/runtime and primary frameworks, with versions. (Mirror these into milestone-driver `nonNegotiables` where they're hard constraints.)
> .NET MAUI 10 (TFMs `net10.0-android`, `net10.0-ios`); built against the iOS 27 SDK / Android API 36; minimum supported iOS 16.0 / Android API 24. Hard constraint (milestone-driver `nonNegotiables`): MAUI .NET 10 + Community Toolkit.

## Approved libraries (by purpose)
One approved choice per purpose, so a redundant alternative is easy to spot.

| Purpose | Library | Notes |
|---|---|---|
| MVVM | CommunityToolkit.Mvvm 8.4.1 | `ObservableObject` only — explicit `SetProperty` + `new (Async)RelayCommand`; no source generators |
| MAUI controls & behaviors | CommunityToolkit.Maui 14.0.1 (+ .Core) | Requires `Microsoft.Maui.Controls >= 10.0.41` (pinned 10.0.110) — do not downgrade |
| Local database | sqlite-net-pcl 1.9.172 + SQLitePCLRaw.bundle_e_sqlite3 2.1.10 | Active Record model pattern |
| Local notifications | Plugin.LocalNotification 14.0.0 | Per-platform reminders |
| HTTP factory | Microsoft.Extensions.Http 10.0.5 | Only for strategic privacy-safe online tools; offline by default |
| Unit testing | xUnit 2.9.2 + NSubstitute 5.3.0 | Parallelization disabled (shared static `_dbService`) |

## Adding a dependency (the gate)
A new dependency is a PAUSE, not an autonomous call. Record what it buys, its license / OSS status, and why nothing approved suffices; a human approves before it's added.
> A new dependency is a PAUSE, not an autonomous call (milestone-driver implementer rule). Record what it buys, its license / OSS status, and why nothing approved suffices; open a GitHub issue labeled `needs decision` and get human approval before adding. Privacy-first: reject anything that phones home.

## Avoid / banned
Libraries explicitly not to use, and why.
> No analytics / telemetry SDKs, no account / auth / backend SDKs, no social-network SDKs (OS-native share sheets are fine — not a social feature), no cloud-sync libraries. Avoid obsolete MAUI APIs: `DisplayAlert` (use `DisplayAlertAsync`), `System.Timers.Timer` (use `IDispatcherTimer`), `Shell.PushModalAsync` (use `Shell.Current.Navigation.PushModalAsync`).
