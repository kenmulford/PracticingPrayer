# Design philosophy

<!--
Part of your project docs (.project/). Tools read and cite this file as
`.project/design-philosophy.md#<section>`. Fill every [TBD]. A section left as
[TBD] is treated as "not specified" — tools fall back to inferred repo
convention rather than ground on a placeholder. Humans own this file; tools may
*propose* changes but never rewrite it. Keep the ## headings stable — they are
citation anchors. Add new sections by appending, not renaming.
-->

## Architectural stance
What kind of system is this, and what does it fundamentally optimize for?
> Layered MVVM .NET MAUI client for personal prayer & worship — prayer cards, prayer requests, and prayer-time sessions. Optimizes for privacy, stability, and UX above all: fully offline, no accounts, no backend, no social features. Core loop is prayer cards → prayer requests → prayer time; tags, colors, and organization serve personalization, not the core.

## Layering & boundaries
The layers and the allowed dependency directions — what may depend on what, and what must never.
> Views (XAML) → ViewModels (CommunityToolkit.Mvvm) → Services (singletons, cached) → Models (Active Record, SQLite) → IDBService/DBService. Strict downward dependency; a layer never reaches around its neighbor. ViewModels resolve services via service locator (`IPlatformApplication.Current!.Services.GetRequiredService<>()`), not constructor injection — MAUI XAML requires parameterless constructors. Models carry persistence via a static `IDBService` set in `MauiProgram.cs`.

## What we optimize for
Ranked priorities, and the explicit non-goals that follow from them.
> Ranked: 1) security & privacy, 2) stability, 3) UX. Non-goals that follow: no cloud sync / accounts / backend, no social features, no church CRM/ChMS integration (others don't need this data), no premature generalization. Tags / colors / organization are personalization, never core.

## One-way doors
Decisions that require human sign-off *before* they're made — irreversible or expensive-to-reverse choices.
> Require human sign-off before they're made: adding any third-party dependency (PAUSE — see `library-manifest.md`); SQLite schema / migration changes (`DBService.UpdateSchema()`); anything introducing a network, account, or backend dependency (violates privacy-first); the display-version bump at a release milestone. Build SDKs are non-negotiable: MAUI .NET 10, Xcode 27 / iOS 27 SDK (minimum iOS 16.0), Android API 36.

## Error & failure philosophy
How the system handles and surfaces failure: fail-open vs fail-closed, the user-facing error policy, logging expectations.
> Fail safe for user data — never lose a prayer or card. Surface user-facing errors via `DisplayAlertAsync`; being offline-only, there is no network-error class. Build-time version stamping fails loudly on a shallow clone rather than shipping a wrong version. Diagnostic instrumentation (e.g. PerfLog) is commented out, not deleted, so a future debug pass re-enables it with one revert.

## Testing philosophy
What we test, at what level, and what "verified" means before a change is done.
> TDD-first for new behavior (red→green); test-after only for pure refactors under green. Unit-test all non-MAUI logic (ViewModels, Services, Models) in `PrayerApp.Tests` (net10.0, links source files, NSubstitute mocks `IDBService`). One Appium E2E per user-visible flow in `PrayerApp.UITests` (`run-uitests.ps1`). A bug fix starts with a failing test. "Verified" = unit suite green plus, for UI-touching changes, the E2E gate.
