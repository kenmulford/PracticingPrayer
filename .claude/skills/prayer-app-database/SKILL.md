---
name: prayer-app-database
description: >
  Use when working with PrayerApp's database layer: schema migrations, IDBService interface,
  seeding, startup recovery, junction table queries, SQL aggregates, WAL checkpoint, PERF-1
  instrumentation, and PRAGMA column-existence guards. Key files: DBService.cs, IDBService.cs,
  MauiProgram.cs. Keywords: migration, UpdateSchema, BUG-58, BUG-21, CardBox, WAL, PRAGMA,
  ConfigureAwait, PerfLog, SeedDataAsync, SeedAsync, junction table, sqlite-net-base.
cso_keywords:
  - PERF-1
  - BUG-58
  - BUG-21
  - F-24
  - WAL
  - wal_checkpoint
  - migration
  - UpdateSchema
  - EnsurePrayerCardColumnsAsync
  - EnsureCardBoxMigrationAsync
  - SeedDataAsync
  - SeedAsync
  - PRAGMA
  - ConfigureAwait
  - PerfLog
  - junction table
  - PrayerCardTag
  - UnassignBoxFromCardsAsync
  - sqlite-net-base
---

# PrayerApp Database Layer

## When to Use

Invoke this skill before any work touching `DBService.cs`, `IDBService.cs`, schema migrations, seeding, WAL handling, or DB performance tracing. The patterns here are the canonical source of truth — do not re-derive from raw file exploration.

---

## Quick Reference

| Topic | Location |
|---|---|
| Interface (all public APIs) | `PrayerApp/Services/IDBService.cs` |
| Implementation | `PrayerApp/Services/DBService.cs` |
| DI registration + seeding | `PrayerApp/MauiProgram.cs` |
| Perf tracing helper | `PrayerApp/Helpers/PerfLog.cs` |
| SQLite packages | `sqlite-net-base` 1.9.172 + `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3; provider registered by `SQLitePCL.Batteries_V2.Init()` in `MauiProgram.CreateMauiApp` |

---

## Key Files

- `PrayerApp/Services/IDBService.cs` — public contract; `UpdateSchema` is **public** here
- `PrayerApp/Services/DBService.cs` — SQLite implementation
- `PrayerApp/MauiProgram.cs` — DI registration, `SetDBService` calls, `SeedAsync`

---

## DBService Constructor & Initialization

```csharp
public DBService(string dbPath)
{
    RunStartupRecovery(dbPath);   // Sync — handles force-close recovery before any connection
    _db = new SQLiteAsyncConnection(dbPath);
    _initTask = UpdateSchema();   // Async table creation/migration; Task caches result
}

private async Task EnsureInitializedAsync() => await _initTask;
```

Every public method calls `EnsureInitializedAsync()` before touching `_db`. **Startup recovery** handles stale `prayer_app_restore.db` and `prayer_app_backup.tmp` files left by interrupted backup/restore operations.

---

## Schema Migration Pattern

`UpdateSchema()` is **public** on `IDBService` — it is part of the interface, not a private detail.

```csharp
// 1. CreateTableAsync is idempotent — safe to call every startup
await _db.CreateTableAsync<PrayerCard>();

// 2. ALTER TABLE with try-catch — column may already exist
try { await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN IsFavorite INTEGER DEFAULT 0"); }
catch (SQLiteException) { /* Column already exists — safe to ignore */ }

// 3. Data backfill after column addition
await _db.ExecuteAsync("UPDATE PrayerCard SET IsSystem = 1 WHERE Title = ? AND IsSystem = 0", title);

// 4. Cleanup orphaned data
await _db.ExecuteAsync("DELETE FROM PrayerInteraction WHERE PrayerId NOT IN (SELECT Id FROM PrayerRequest)");
```

**Rules:**
- Wrap every `ALTER TABLE` in `try-catch` — migrations run every startup
- `INTEGER DEFAULT 0` for booleans (SQLite has no bool type); `TEXT` for strings
- Create tables before altering them; create dependency columns before queries that need them
- Never remove old migrations — they must be idempotent

### Actual Migration Sequence (DBService.UpdateSchema)

1. Create base tables: `PrayerCard`, `Prayer` (`PrayerRequest` table), `PrayerTag`, `PrayerCardTag`, `PrayerInteraction`, `UserColor`
2. `EnsurePrayerCardColumnsAsync()` — adds `IsFavorite` and `ProtectionMode` on `PrayerCard`, `AnsweredAt` on `PrayerRequest`, `PrayerRequestId` on `PrayerCardTag`; runs BUG-21 card-to-request tag migration
3. Add `IsSystem` to `PrayerCard` (must precede `EnsureCardBoxMigrationAsync` which queries `WHERE IsSystem = 1`)
4. Add `IsImported` to `PrayerCard` and `PrayerRequest` (F-10; must precede `EnsureCardBoxMigrationAsync`)
5. **BUG-58 backfill** — `UPDATE PrayerCard SET IsSystem = 1 WHERE Title = ? AND IsSystem = 0` for Quick Add and Shared With Me system cards
6. Create `CardBox` table + `EnsureCardBoxMigrationAsync()` (F-24: seeds System/Archived boxes, adds `BoxId`/`SystemKey`/`PreArchiveBoxId` columns, migrates system/imported cards)
7. Add `ProtectAllCards` and `CardProtectionMode` to `CardBox` (Confidential Cards #250; must follow `CardBox` creation)
8. `DROP TABLE IF EXISTS PrayerRequestTag` — legacy table removal
9. Orphan cleanup: delete `PrayerInteraction` rows with no matching prayer; delete `PrayerCardTag` rows with `PrayerRequestId > 0` pointing to deleted prayers
10. Add `IsDefault` to `UserColor`
11. Add `IsSystem` to `PrayerTag`
12. Add notification columns (`NotifyHour`, `NotifyMinute`, `NotifyDayOfWeek`, `NotifyDayOfMonth`) to `PrayerRequest`, then `NotifyDateMigration.ApplyAsync` backfills `NotifyDate` for OneTime/Yearly rows (the `NotifyDate` column comes from `CreateTableAsync<Prayer>`)
13. Backfill `UserColor.IsDefault = 1` for the 8 seed hex values

### Adding a New Migration

Append to the end of `UpdateSchema()`:

```csharp
// F-XX: Description of change
try { await _db.ExecuteAsync("ALTER TABLE TableName ADD COLUMN NewColumn TYPE DEFAULT value"); }
catch (SQLiteException) { }
```

---

## IDBService Interface — Complete API

### Generic CRUD
```csharp
Task<List<T>> GetAllAsync<T>() where T : new();
Task<T> GetByIdAsync<T>(int id) where T : new();
Task<int> InsertAsync<T>(T item);
Task<int> UpdateAsync<T>(T item);
Task<int> DeleteAsync<T>(T item);
Task<int> DropTableAsync<T>() where T : new();
```

### PrayerCardTag (Junction) Queries
```csharp
Task<List<PrayerCardTag>> GetByRequestIdAsync(int prayerRequestId);   // Tags on a prayer
Task<List<PrayerCardTag>> GetByTagIdAsync(int prayerTagId);           // Prayers with a tag
Task<List<PrayerCardTag>> GetByTagIdsAsync(IEnumerable<int> tagIds);  // Prayers with any of tags
Task<int> DeleteByTagIdAsync(int prayerTagId);                        // Remove all for a tag
Task<int> DeleteJunctionRowsByRequestIdAsync(int prayerRequestId);    // Cleanup on prayer delete
```

### Per-Parent Queries
```csharp
Task<List<Prayer>> GetPrayersByCardIdAsync(int prayerCardId);
Task<List<PrayerInteraction>> GetInteractionsByPrayerIdAsync(int prayerId);
```

### PrayerInteraction Aggregates
```csharp
Task<List<LatestInteractionResult>> GetLatestInteractionByPrayerAsync();  // GROUP BY PrayerId
Task<DateTime?> GetMaxInteractionDateAsync();                              // Most recent across all
Task<int> DeleteInteractionsByPrayerIdAsync(int prayerId);                 // Cascade delete
Task<int> CountInteractionsByPrayerIdAsync(int prayerId);
```

### CardBox Queries
```csharp
Task<List<PrayerCard>> GetCardsByBoxIdAsync(int boxId);
Task UnassignBoxFromCardsAsync(int boxId);  // Returns Task (not Task<int>) — sets BoxId=0
```

### Lifecycle
```csharp
Task UpdateSchema();          // Public — part of IDBService interface
Task SeedDataAsync();         // Public — called only in DEBUG + FirstRun (see Seed Data Pattern)
Task CloseAsync();            // WAL checkpoint + close
Task ReinitializeAsync(string path);  // Reopen at new path (backup/restore)
```

---

## PERF-1 Instrumentation (PerfLog)

`GetAllAsync`, `InsertAsync`, `UpdateAsync`, and `GetPrayersByCardIdAsync` keep a `Stopwatch` + `.ConfigureAwait(false)`; their `PerfLog.Log` calls are commented out, so uncomment them to trace:

```csharp
public async Task<List<T>> GetAllAsync<T>() where T : new()
{
    // PrayerApp.Helpers.PerfLog.Log($"DBService.GetAllAsync<{typeof(T).Name}>.entry");
    await EnsureInitializedAsync();
    // ...
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await _db.Table<T>().ToListAsync().ConfigureAwait(false);
    sw.Stop();
    // PrayerApp.Helpers.PerfLog.Log($"DBService.GetAllAsync<{typeof(T).Name}>.after ToListAsync (count={result.Count}, taskMs={sw.ElapsedMilliseconds})");
    return result;
}
```

- `ConfigureAwait(false)` lets the continuation run on the thread pool. `taskMs` = pure DB time. The delta between `taskMs` log and the caller's next log line is UI-thread queuing cost.
- `PerfLog` is in `PrayerApp/Helpers/PerfLog.cs` — a shared stopwatch started at first access.
- Filter Logcat: `adb logcat | grep PERF:`

---

## WAL Checkpoint Gotcha

`CloseAsync` uses `ExecuteScalarAsync<int>` — **not** `ExecuteAsync` — for `PRAGMA wal_checkpoint(TRUNCATE)`:

```csharp
// CORRECT: ExecuteScalarAsync handles the result set PRAGMA returns
await _db.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE)");
await _db.CloseAsync();

// WRONG: ExecuteAsync calls ExecuteNonQuery, which throws SQLiteException("not an error")
// when PRAGMA returns a result row (busy, log, checkpointed columns).
```

This is a sqlite-net gotcha, not a bug — the exception message "not an error" is misleading.

---

## PRAGMA table_info Column-Existence Guard

`MigrateCardTagsToRequestTagsAsync` guards against a missing column before querying it:

```csharp
private class SQLiteColumnInfo
{
    public string name { get; set; } = string.Empty;
}

var rows = await _db.QueryAsync<SQLiteColumnInfo>("PRAGMA table_info(PrayerCardTag)");
var columnExists = rows.Any(r => r.name == "PrayerRequestId");
if (!columnExists) return;
```

Use this pattern whenever an ALTER TABLE might have silently failed and a subsequent query depends on that column.

---

## SQL-Level Aggregate Queries

```csharp
public class LatestInteractionResult
{
    public int PrayerId { get; set; }
    public DateTime LatestInteractionAt { get; set; }
}

// Group max per prayer (used by PrayerService for overdue calculation)
"SELECT PrayerId, MAX(InteractionAt) AS LatestInteractionAt FROM PrayerInteraction GROUP BY PrayerId"

// Overall max (used for display/badge logic)
"SELECT MAX(InteractionAt) FROM PrayerInteraction"
```

---

## Junction Table Pattern (PrayerCardTag)

```csharp
// Get tags on a prayer (sqlite-net LINQ)
await _db.Table<PrayerCardTag>()
    .Where(pct => pct.PrayerRequestId == prayerRequestId)
    .ToListAsync();

// Bulk delete by tag ID (raw SQL — more efficient)
await _db.ExecuteAsync("DELETE FROM PrayerCardTag WHERE PrayerTagId = ?", prayerTagId);

// Bulk delete by prayer ID (cascade cleanup on prayer delete)
await _db.ExecuteAsync("DELETE FROM PrayerCardTag WHERE PrayerRequestId = ?", prayerRequestId);

// GetByTagIdsAsync: sqlite-net doesn't support Contains in LINQ — filter in memory
var idSet = tagIds.ToHashSet();
var all = await _db.Table<PrayerCardTag>().ToListAsync();
return all.Where(pct => idSet.Contains(pct.PrayerTagId)).ToList();
```

---

## Seed Data Pattern

`SeedDataAsync()` is on `IDBService` and is called exclusively from `MauiProgram.SeedAsync` inside `#if DEBUG` guarded by `Settings.FirstRun`:

```csharp
// MauiProgram.SeedAsync, after notification reconciliation
#if DEBUG
if (PrayerApp.Services.Settings.FirstRun)
{
    var dbService = services.GetRequiredService<IDBService>();
    await dbService.SeedDataAsync();
    PrayerApp.Services.Settings.FirstRun = false;
}
#endif
```

`SeedDataAsync` is **not** called in Release builds. The full startup seed sequence runs unconditionally via `SeedAsync`:

```
1. Trim diagnostic log
2. UserColorService.SeedDefaultsAsync()       — 8 default palette colors
3. TagService.SeedSystemTagsAsync()            — "Recently Notified" system tag
4. BoxService.SeedSystemBoxesAsync()           — System + Archived boxes
5. Sync ArchivedFolderId to Settings
6. CardService.GetOrCreateQuickAddCardAsync()  — Quick Add system card
7. BUG-58 safety-net: move any IsSystem cards still at BoxId=0 to System box
8. Load active prayers
9. Tag recently-notified prayers
10. NotificationService.ReconcileNotificationsAsync()
```

---

## Tag Migration (BUG-21)

`MigrateCardTagsToRequestTagsAsync()` — promotes legacy card-level tag rows to prayer-level. Runs inside `EnsurePrayerCardColumnsAsync` with a PRAGMA guard (see above):

```
For each legacy row (PrayerRequestId == 0):
  1. Find all prayers on the card
  2. Create new PrayerCardTag row per prayer with PrayerRequestId set (skip if duplicate)
  3. Delete the legacy row
```

---

## CardBox Migration (F-24)

`EnsureCardBoxMigrationAsync()` — idempotent, runs every startup:

```
1. Seed System box (SortOrder=900) and Archived box (SortOrder=999) if not present
2. Persist ArchivedFolderId to Settings
3. Add BoxId, SystemKey, and PreArchiveBoxId columns to PrayerCard
4. Move system/imported cards from BoxId=0 → System box
5. Backfill SystemKey on known system cards ("quick_add", "shared_with_me")
```

---

## Common Mistakes

| Mistake | Correct Approach |
|---|---|
| Using `ExecuteAsync` for WAL checkpoint PRAGMA | Use `ExecuteScalarAsync<int>` — PRAGMA returns a result set |
| Omitting `ConfigureAwait(false)` on hot DB calls | Add it to `ToListAsync`/`InsertAsync`/`UpdateAsync` continuations for thread-pool benefit |
| Treating `UpdateSchema` as a private detail | It is **public** on `IDBService` — part of the interface contract |
| Calling `SeedDataAsync` in production paths | It is DEBUG-only + FirstRun-gated in `MauiProgram.SeedAsync` |
| Adding `ALTER TABLE` without try-catch | Migrations run every startup; the column may already exist |
| Querying a column without a PRAGMA existence guard | If ALTER TABLE silently failed, the query will crash on startup |

---

## Checklists

### Adding a New Table

1. Create model class (see `prayer-app-models` skill)
2. Add `await _db.CreateTableAsync<NewModel>();` in `UpdateSchema()`
3. If custom queries needed, add to `IDBService` and implement in `DBService`
4. Add `NewModel.SetDBService(myDBService);` in `MauiProgram.cs`
5. If seed data needed, add seed method to relevant service and call from `SeedAsync`

### Adding a Column to an Existing Table

1. Add property to model class with appropriate default
2. Add `ALTER TABLE` migration in `UpdateSchema()` with `try-catch` guard
3. If query depends on the column, add a `PRAGMA table_info` existence guard
4. If backfill needed, add data migration after the `ALTER TABLE`
