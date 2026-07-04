# Environment

<!--
Project doc (.project/). Cite as `.project/environment.md#<section>`. Declares what the
project's runtime and production environment looks like — the facts downstream tools ground
their data, test, and caching decisions in. It does NOT provision anything; it records the
model so issues don't drift. Fill every [TBD]; a section left [TBD] is treated as "not
specified." Humans own this file; tools propose, never rewrite. Keep the ## headings stable
— they are citation anchors.
-->

## Environments
Which environments exist (production, staging, test, local) and how they differ.
> Single on-device runtime — no production / staging / server tiers (the app is fully offline). Local dev plus CI (GitHub Actions, ubuntu-latest, unit tests only); a device or Android emulator for Appium E2E. App identity `com.multithreadedllc.prayercards`.

## Data stores
Databases and other persistent stores: the engine(s), and the **topology** — separate prod / staging / test databases, or a shared one. **Test-data isolation:** how tests get a clean, isolated database (a dedicated test DB, a per-worker DB suffix, transactional rollback, truncate-on-start). This is the single biggest drift source if left unstated.
> One local SQLite database (`prayer_app.db`) on-device; no remote or shared DB. Tables: PrayerCard, PrayerRequest (the `Prayer` model maps `[Table("PrayerRequest")]`), PrayerTag, PrayerCardTag, PrayerInteraction; migrations in `DBService.UpdateSchema()`. Test-data isolation: NSubstitute mocks `IDBService`, a static `SetDBService()` runs in each test constructor, and parallelization is disabled (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) because the model static `_dbService` is shared state.

## Caching
Whether caching exists and, if so, the layer and technology (in-memory, Redis, CDN), what is cached, and the invalidation policy. **"None" is a valid, drift-preventing answer** — record it explicitly.
> None beyond a per-service in-memory `_cache` (read → populate → return; write → null the cache → return). No Redis, CDN, or HTTP cache.

## Async & messaging
Background jobs, queues, streams, schedulers — or "none."
> In-process only: a WeakReferenceMessenger (Community Toolkit) message bus for reactive cross-ViewModel sync (e.g. `PrayerChangedMessage`, `BulkChangedMessage`); `IDispatcherTimer` for UI-thread timers (never `System.Timers.Timer`); local reminders via Plugin.LocalNotification. No queues, streams, or server jobs.

## External services & integrations
Third-party services the app depends on: auth / identity, payments, email / SMS, object storage, analytics, other APIs.
> None — privacy-first and fully offline. No auth / identity, payments, email / SMS, object storage, analytics, or backend APIs. Will NOT integrate with church CRM / ChMS. May add strategic online tools only where they deepen the experience without violating the privacy-first principle.

## Runtime & hosting
Where it runs and the runtime/version targets (hosting platform, language-runtime versions, regions). For mandated frameworks and packages, cross-reference `library-manifest.md`.
> On-device only: iOS (min 16.0) and Android (min API 21), built against iOS 26.5 / Android API 36. No server hosting. App ID `com.multithreadedllc.prayercards`. For mandated frameworks and packages, see `library-manifest.md`. **iOS-simulator Debug builds require `-r iossimulator-arm64`** — it selects the simulator slice of the `libHostShortcuts.a` native lib; without it the native link step picks the device slice and fails (issue #150; the `build/HostShortcuts.targets` code fix that would remove the need for the explicit RID is deferred).
