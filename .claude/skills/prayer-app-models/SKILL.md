---
name: prayer-app-models
description: >
  Use when creating or modifying data models, adding columns, understanding FK
  relationships, looking up property defaults, or adding a new model to
  PrayerApp. Covers Active Record pattern, SQLite attributes, system entity
  constants, and the checklist for wiring a new model into the project.
---

# PrayerApp Data Models

All models live in `PrayerApp/Models/` and use the **Active Record** pattern via sqlite-net (`sqlite-net-base` package).

---

## When to Use This Skill

- Adding a column or property to an existing model (schema, Active Record, migrations)
- Creating a new model (full wiring checklist)
- Looking up property defaults, nullable types, or FK relationships
- Understanding `SetDBService` wiring order in `MauiProgram.cs`
- Investigating system-entity constants (`TitleQuickAdd`, `SystemKeyQuickAdd`, etc.)

---

## Quick Reference — Models and Table Names

| Class | SQLite Table | Notes |
|---|---|---|
| `Prayer` | `PrayerRequest` | **Name mismatch — always use `[Table("PrayerRequest")]`** |
| `PrayerCard` | `PrayerCard` | |
| `CardBox` | `CardBox` | |
| `PrayerTag` | `PrayerTag` | |
| `PrayerCardTag` | `PrayerCardTag` | Junction table — no `UpdatedAt` |
| `PrayerInteraction` | `PrayerInteraction` | |
| `UserColor` | `UserColor` | No Active Record — managed via `IUserColorService` |

> The `Prayer` → `PrayerRequest` table name mismatch is intentional/legacy. The C# class is `Prayer`; the SQLite table is `PrayerRequest`. Always use `[Table("PrayerRequest")]`.

---

## Active Record Pattern Template

Every model (except `UserColor`) follows this exact structure. Copy and adapt:

```csharp
using SQLite;

namespace PrayerApp.Models;

[Table("TableName")]
public class MyModel
{
    private static IDBService? _dbService;
    private string _title = string.Empty;   // explicit backing field — do NOT use C# 13 'field' keyword

    [PrimaryKey, AutoIncrement]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title"), MaxLength(100)]
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    #region Static Methods
    public static void SetDBService(IDBService dbService) { _dbService = dbService; }
    #endregion

    #region Actions

    public async Task SaveAsync()
    {
        if (_dbService == null)
            throw new InvalidOperationException("DBService not set. Call MyModel.SetDBService at app startup.");
        if (Id == 0)
            await _dbService.InsertAsync(this);
        else
        {
            UpdatedAt = DateTime.Now;
            await _dbService.UpdateAsync(this);
        }
    }

    public async Task DeleteAsync()
    {
        if (_dbService == null)
            throw new InvalidOperationException("DBService not set. Call MyModel.SetDBService at app startup.");
        await _dbService.DeleteAsync(this);
    }

    public static async Task<MyModel> LoadAsync(int id)
    {
        if (_dbService == null)
            throw new InvalidOperationException("DBService not set. Call MyModel.SetDBService at app startup.");
        return await _dbService.GetByIdAsync<MyModel>(id);
    }

    public static async Task<List<MyModel>> LoadAllAsync()
    {
        if (_dbService == null)
            throw new InvalidOperationException("DBService not set. Call MyModel.SetDBService at app startup.");
        return await _dbService.GetAllAsync<MyModel>();
    }

    #endregion
}
```

**Key rules:**
- `Id == 0` → insert; `Id > 0` → update
- `UpdatedAt` is set on update only — insert uses the property initializer `= DateTime.Now`
- Null-safe strings use an **explicit private backing field** (`private string _title = string.Empty;`) — the C# 13 `field` keyword is NOT used anywhere in this codebase
- Every Active Record method guards with `if (_dbService == null) throw new InvalidOperationException(...)` before touching the DB
- `PrayerCardTag` is an exception: its `SaveAsync` update branch does **not** set `UpdatedAt` (the junction table has no `UpdatedAt` column)

---

## SetDBService Wiring (`MauiProgram.CreateMauiApp`)

`SetDBService` is called once at startup in `MauiProgram.cs` after the app is built. Call order:

```csharp
var myDBService = app.Services.GetRequiredService<IDBService>();
PrayerCard.SetDBService(myDBService);
PrayerTag.SetDBService(myDBService);
PrayerCardTag.SetDBService(myDBService);
Prayer.SetDBService(myDBService);
PrayerInteraction.SetDBService(myDBService);
CardBox.SetDBService(myDBService);
```

`DBService.UpdateSchema()` (schema creation + migrations) runs lazily via `_initTask` assigned in the `DBService` constructor. It completes before any query runs, so models can be called immediately after wiring.

---

## Complete Model Reference

### Prayer (`PrayerApp/Models/Prayer.cs`)

**SQLite Table:** `PrayerRequest` (via `[Table("PrayerRequest")]` — name mismatch is intentional/legacy)

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| PrayerCardId | int | [Indexed] | | FK to PrayerCard |
| Title | string | [MaxLength(100)] | `string.Empty` | Explicit backing field |
| Details | string? | [MaxLength(1000)] | null | Nullable — no backing field |
| CanNotify | bool | | false | |
| PrayerFrequency | PrayerFrequency | | **Weekly** | Not OneTime |
| NotifyHour | int | | 9 | 0–23 |
| NotifyMinute | int | | 0 | 0–59 |
| NotifyDayOfWeek | int | | -1 | -1=use creation day, 0=Sun..6=Sat |
| NotifyDayOfMonth | int | | -1 | -1=use creation day, 1–31 |
| NotifyDate | DateTime? | | null | OneTime/Yearly reminder day (date only); null = legacy CreatedAt path; backfilled by `NotifyDateMigration` |
| IsImported | bool | | false | F-10: shared/deep-linked content |
| IsAnswered | bool | | false | |
| AnsweredAt | DateTime? | | null | |
| CreatedAt | DateTime | | DateTime.Now | |
| UpdatedAt | DateTime | | DateTime.Now | |

**Custom query:** `LoadByCardIdAsync(int prayerCardId)` — delegates to `_dbService.GetPrayersByCardIdAsync()`

---

### PrayerCard (`PrayerApp/Models/PrayerCard.cs`)

**SQLite Table:** `PrayerCard`

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| Title | string | [MaxLength(100)] | `string.Empty` | Explicit backing field |
| CanNotify | bool | | false | |
| PrayerFrequency | PrayerFrequency | | **Weekly** | Not OneTime |
| IsAnswered | bool | | false | |
| IsFavorite | bool | | false | |
| IsSystem | bool | | false | Protected from user edit/delete |
| IsImported | bool | | false | F-10: shared content |
| ProtectionMode | CardProtectionMode | | `None` | Confidential Cards: `None = 0`, `LockedVisible = 1`, `Hidden = 2` |
| BoxId | int | | 0 | FK to CardBox; 0 = Unboxed |
| PreArchiveBoxId | int? | | null | BoxId before archiving, restored on unarchive (BUG-121) |
| SystemKey | string? | [MaxLength(20)] | null | Identifies system cards |
| CreatedAt | DateTime | | DateTime.Now | |
| UpdatedAt | DateTime | | DateTime.Now | |

**System card constants (used in `DBService.cs`, `CardService.cs`, `DeepLinkService.cs`):**
- `SystemKeyQuickAdd = "quick_add"` / `TitleQuickAdd = "Quick Add"`
- `SystemKeySharedWithMe = "shared_with_me"` / `TitleSharedWithMe = "Shared with me"`

---

### CardBox (`PrayerApp/Models/CardBox.cs`)

**SQLite Table:** `CardBox`

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| Name | string | [MaxLength(50), Unique] | `string.Empty` | Null-safe |
| IsSystem | bool | | false | Protected from rename/delete |
| SystemKey | string? | [MaxLength(20)] | null | |
| SortOrder | int | | 0 | Controls display order |
| ProtectAllCards | bool | | false | Cascades `CardProtectionMode` to cards with no explicit mode |
| CardProtectionMode | CardProtectionMode | | `None` | Mode cascaded when `ProtectAllCards` is true |
| CreatedAt | DateTime | | DateTime.Now | |
| UpdatedAt | DateTime | | DateTime.Now | |

**System box constants:**
- `SystemKeySystem = "system"` — SortOrder=900, holds Quick Add + Shared With Me
- `SystemKeyArchived = "archived"` — SortOrder=999

---

### PrayerTag (`PrayerApp/Models/PrayerTag.cs`)

**SQLite Table:** `PrayerTag`

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| Name | string | [MaxLength(100), Unique] | `"Unnamed Tag"` | |
| Color | string? | [MaxLength(9)] | null | Hex e.g. `"#FF5733"` |
| IsSystem | bool | | false | Protected from delete |
| CreatedAt | DateTime | | DateTime.Now | |
| UpdatedAt | DateTime | | DateTime.Now | |

---

### PrayerCardTag (`PrayerApp/Models/PrayerCardTag.cs`) — Junction Table

**SQLite Table:** `PrayerCardTag`

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| PrayerCardId | int | [Indexed] | | **Deprecated** — kept for legacy |
| PrayerTagId | int | [Indexed] | | FK to PrayerTag |
| PrayerRequestId | int | [Indexed] | | FK to Prayer (**current usage**) |
| CreatedAt | DateTime | | DateTime.Now | |

**No `UpdatedAt` column.** The `SaveAsync()` update branch calls `_dbService.UpdateAsync(this)` without setting `UpdatedAt` — correct behavior for this junction table.

**Schema evolution (BUG-21):** Tags moved from card-level to prayer-level. Legacy rows have `PrayerRequestId == 0`. Migration in `DBService.MigrateCardTagsToRequestTagsAsync()`.

**Custom query:** `LoadByTagIdAsync(int prayerTagId)` — all tag assignments for a tag.

---

### PrayerInteraction (`PrayerApp/Models/PrayerInteraction.cs`)

**SQLite Table:** `PrayerInteraction`

| Property | Type | Attributes | Default | Notes |
|---|---|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] | | |
| PrayerId | int | [Indexed] | | FK to Prayer |
| InteractionType | string | [MaxLength(50)] | `"Prayed"` | |
| InteractionAt | DateTime | | DateTime.Now | When the interaction occurred |
| CreatedAt | DateTime | | DateTime.Now | |
| UpdatedAt | DateTime | | DateTime.Now | |

---

### UserColor (`PrayerApp/Models/UserColor.cs`)

**SQLite Table:** `UserColor`

| Property | Type | Notes |
|---|---|---|
| Id | int | [PrimaryKey, AutoIncrement] |
| HexValue | string | Uppercase hex |
| IsDefault | bool | 8 seed colors; protected from delete |
| CreatedAt | DateTime | |

**No Active Record.** `UserColor` is managed entirely via `IUserColorService` — it has no `SetDBService`, `SaveAsync`, or `DeleteAsync`.

---

## Enums

### PrayerFrequency (`PrayerApp/Models/PrayerFrequency.cs`)
```csharp
public enum PrayerFrequency { OneTime = 0, Daily = 1, Weekly = 2, Monthly = 3, Yearly = 4 }
```
Default on both `Prayer` and `PrayerCard` is **`PrayerFrequency.Weekly`** — not `OneTime`.

### OnboardingStep (`PrayerApp/Models/OnboardingStep.cs`)
```csharp
public enum OnboardingStep
{
    None, Welcome, CreateCard, NameCard, AddRequest, NameRequest,
    PrayerTimeHighlight, Complete,
    // Legacy migration values
    PrayerTime = 100, PrayerTimeActive = 101, ShareIntro = 200, SharePrayer = 201
}
```

---

## FK Relationship Map

```
CardBox (1) ──── (*) PrayerCard (1) ──── (*) Prayer (1) ──── (*) PrayerInteraction
                                                   │
                                                   └──── (*) PrayerCardTag (*) ──── (1) PrayerTag
```

- `PrayerCard.BoxId` → `CardBox.Id` (0 = Unboxed)
- `Prayer.PrayerCardId` → `PrayerCard.Id`
- `PrayerInteraction.PrayerId` → `Prayer.Id`
- `PrayerCardTag.PrayerRequestId` → `Prayer.Id` (current)
- `PrayerCardTag.PrayerTagId` → `PrayerTag.Id`

---

## System Entities

| Type | SystemKey | Title constant | Protected From |
|---|---|---|---|
| PrayerCard | `quick_add` | `TitleQuickAdd = "Quick Add"` | Delete, rename |
| PrayerCard | `shared_with_me` | `TitleSharedWithMe = "Shared with me"` | Delete, rename |
| CardBox | `system` | `"System"` | Delete, rename |
| CardBox | `archived` | `"Archived"` | Delete, rename |
| PrayerTag | (by name) | `"Recently Notified"` | Delete |
| UserColor | `IsDefault=true` | 8 seed colors | Delete |

---

## Common Mistakes

| Mistake | Correct pattern |
|---|---|
| Using `PrayerFrequency.OneTime` as default | Both `Prayer` and `PrayerCard` default to `PrayerFrequency.Weekly` |
| Omitting the null guard in Active Record methods | Every method must `if (_dbService == null) throw new InvalidOperationException(...)` before touching DB |
| Using C# 13 `field` keyword for null-safe strings | Use an explicit private backing field: `private string _title = string.Empty;` |
| Using `_dbService!.InsertAsync(this)` (null-forgiving) | Use the null check + throw pattern — see template above |
| Adding `UpdatedAt` to a junction table | `PrayerCardTag` has no `UpdatedAt`; don't add one unless a schema migration also adds the column |
| Calling `SaveAsync` before `SetDBService` | Always wire all models in the `SetDBService` block of `MauiProgram.CreateMauiApp` before any query |

---

## Checklist: Adding a New Model

1. Create `PrayerApp/Models/NewModel.cs` with Active Record pattern (use template above)
2. Add `[Table("NewModel")]` and all properties with SQLite attributes and `[Column("...")]`
3. Add interface methods to `PrayerApp/Services/IDBService.cs` for any custom queries
4. Implement those methods in `PrayerApp/Services/DBService.cs`
5. Add `await _db.CreateTableAsync<NewModel>();` in `DBService.UpdateSchema()`
6. Add `NewModel.SetDBService(myDBService);` in `MauiProgram.cs`, continuing the `SetDBService` block after `CardBox.SetDBService`
7. Add `<Compile Include="../PrayerApp/Models/NewModel.cs" />` in `PrayerApp.Tests/PrayerApp.Tests.csproj`
8. If the model has a service, create it (see `prayer-app-services`)
