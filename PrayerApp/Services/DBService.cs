using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;

using PrayerApp.Models;


namespace PrayerApp.Services
{
    class DBService : IDBService
    {
        private SQLiteAsyncConnection? _db;
        private readonly Task _initTask;

        public DBService(string dbPath)
        {
            // Startup recovery check — runs synchronously before any connection is opened.
            // Handles any state left behind by a force-close mid-restore.
            RunStartupRecovery(dbPath);

            _db = new SQLiteAsyncConnection(dbPath);

            // Start table creation + schema migration asynchronously.
            // All public DB methods await _initTask before proceeding,
            // so callers never hit an uninitialized database.
            _initTask = UpdateSchema();
        }

        /// <summary>
        /// Awaits the initial table creation and schema migration.
        /// Completes instantly after the first call since Task caches its result.
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            await _initTask;
        }

        private static void RunStartupRecovery(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath)!;
            var restorePath = Path.Combine(dir, "prayer_app_restore.db");
            var backupTmpPath = Path.Combine(dir, "prayer_app_backup.tmp");

            bool restoreExists = File.Exists(restorePath);
            bool dbExists = File.Exists(dbPath);
            bool backupTmpExists = File.Exists(backupTmpPath);

            if (restoreExists && backupTmpExists)
            {
                // Both stale — impossible by construction but handle defensively
                File.Delete(restorePath);
                File.Delete(backupTmpPath);
            }
            else if (restoreExists && dbExists)
            {
                // Write interrupted — original intact; discard partial restore
                File.Delete(restorePath);
            }
            else if (restoreExists && !dbExists)
            {
                // Swap interrupted mid-flight — complete the rename
                File.Move(restorePath, dbPath);
            }
            else if (backupTmpExists && dbExists)
            {
                // Swap completed, stale backup remains
                File.Delete(backupTmpPath);
            }
            else if (backupTmpExists && !dbExists)
            {
                // Catastrophic swap failure — roll back to original
                File.Move(backupTmpPath, dbPath);
            }
            // else: normal startup — nothing to do
        }

        public async Task UpdateSchema()
        {
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            await _db.CreateTableAsync<PrayerCard>();
            await _db.CreateTableAsync<Prayer>();
            await _db.CreateTableAsync<PrayerTag>();
            await _db.CreateTableAsync<PrayerCardTag>();
            await _db.CreateTableAsync<PrayerInteraction>();
            await _db.CreateTableAsync<UserColor>();

            await EnsurePrayerCardColumnsAsync();

            // Add IsSystem column to PrayerCard for system-managed cards (e.g., Quick Add)
            // Must run BEFORE EnsureCardBoxMigrationAsync which queries WHERE IsSystem = 1
            try
            {
                await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN IsSystem INTEGER DEFAULT 0");
            }
            catch { /* Column already exists */ }

            // F-10: Add IsImported column to PrayerCard and PrayerRequest for shared content
            // Must run BEFORE EnsureCardBoxMigrationAsync which queries WHERE IsImported = 1
            try { await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN IsImported INTEGER DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { await _db.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN IsImported INTEGER DEFAULT 0"); }
            catch { /* Column already exists */ }

            // BUG-58: Backfill IsSystem on legacy system cards created before IsSystem feature.
            // Uses Title match since these cards may have IsSystem=0 from the DEFAULT.
            try
            {
                await _db.ExecuteAsync(
                    "UPDATE PrayerCard SET IsSystem = 1 WHERE Title = ? AND IsSystem = 0",
                    PrayerCard.TitleQuickAdd);
                await _db.ExecuteAsync(
                    "UPDATE PrayerCard SET IsSystem = 1 WHERE Title = ? AND IsSystem = 0",
                    PrayerCard.TitleSharedWithMe);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BUG-58] IsSystem backfill: {ex.Message}");
            }

            // F-24: CardBox table + PrayerCard box columns + data migration
            await _db.CreateTableAsync<CardBox>();
            await EnsureCardBoxMigrationAsync();

            // Confidential Cards (#250): box-level cascade — protect every card in the box
            // that doesn't already set its own ProtectionMode. Must run after CardBox table
            // creation above (EnsurePrayerCardColumnsAsync runs before CardBox exists).
            try
            {
                await _db.ExecuteAsync("ALTER TABLE CardBox ADD COLUMN ProtectAllCards INTEGER DEFAULT 0");
            }
            catch { /* Column already exists */ }

            try
            {
                await _db.ExecuteAsync("ALTER TABLE CardBox ADD COLUMN CardProtectionMode INTEGER DEFAULT 0");
            }
            catch { /* Column already exists */ }

            try
            {
                await _db.ExecuteAsync("DROP TABLE IF EXISTS PrayerRequestTag");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateSchema] PrayerRequestTag migration: {ex.Message}");
            }

            // One-time cleanup: remove orphaned PrayerInteraction and PrayerCardTag rows
            // left behind by prior deletions that didn't cascade.
            try
            {
                await _db.ExecuteAsync(
                    "DELETE FROM PrayerInteraction WHERE PrayerId NOT IN (SELECT Id FROM PrayerRequest)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateSchema] Orphan PrayerInteraction cleanup: {ex.Message}");
            }

            try
            {
                // PrayerRequestId == 0 rows are legacy card-level rows handled by BUG-21 migration
                await _db.ExecuteAsync(
                    "DELETE FROM PrayerCardTag WHERE PrayerRequestId > 0 AND PrayerRequestId NOT IN (SELECT Id FROM PrayerRequest)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateSchema] Orphan PrayerCardTag cleanup: {ex.Message}");
            }

            // Add IsDefault column to UserColor and backfill existing seed colors
            try
            {
                await _db.ExecuteAsync("ALTER TABLE UserColor ADD COLUMN IsDefault INTEGER DEFAULT 0");
            }
            catch { /* Column already exists */ }

            // Add IsSystem column to PrayerTag for system-managed tags
            try
            {
                await _db.ExecuteAsync("ALTER TABLE PrayerTag ADD COLUMN IsSystem INTEGER DEFAULT 0");
            }
            catch { /* Column already exists */ }

            // Add notification scheduling columns to PrayerRequest
            try { await _db.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN NotifyHour INTEGER DEFAULT 9"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN NotifyMinute INTEGER DEFAULT 0"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN NotifyDayOfWeek INTEGER DEFAULT -1"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN NotifyDayOfMonth INTEGER DEFAULT -1"); } catch { }

            try
            {
                // Mark the 8 original seed colors as defaults (by hex value)
                var seedHexValues = new[] {
                    "#B84040", "#B35A20", "#7A4020", "#1E7870",
                    "#2E5A9A", "#663C8C", "#8C3860", "#505050"
                };
                foreach (var hex in seedHexValues)
                {
                    await _db.ExecuteAsync(
                        "UPDATE UserColor SET IsDefault = 1 WHERE UPPER(HexValue) = ?",
                        hex.ToUpperInvariant());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateSchema] UserColor IsDefault backfill: {ex.Message}");
            }
        }

        public async Task CloseAsync()
        {
            await EnsureInitializedAsync();
            if (_db == null) return;
            // ExecuteScalarAsync handles the result set PRAGMA wal_checkpoint returns
            // (busy, log, checkpointed columns). ExecuteAsync calls ExecuteNonQuery
            // which throws SQLiteException("not an error") on SQLITE_ROW being returned.
            await _db.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE)");
            await _db.CloseAsync();
            _db = null;
        }

        public async Task ReinitializeAsync(string path)
        {
            _db = new SQLiteAsyncConnection(path);
            await UpdateSchema();
        }

        public async Task<List<T>> GetAllAsync<T>() where T : new()
        {
            // PrayerApp.Helpers.PerfLog.Log($"DBService.GetAllAsync<{typeof(T).Name}>.entry");
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            // PrayerApp.Helpers.PerfLog.Log($"DBService.GetAllAsync<{typeof(T).Name}>.before ToListAsync");
            // PERF probe: Stopwatch + ConfigureAwait(false) so the continuation runs on
            // the threadpool. taskMs = pure DB time. Caller's next log line shows when
            // their UI-thread resume fired — the delta is UI-thread queuing cost. Read
            // taskMs directly; cross-thread Logcat line ordering isn't deterministic.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _db.Table<T>().ToListAsync().ConfigureAwait(false);
            sw.Stop();
            // PrayerApp.Helpers.PerfLog.Log($"DBService.GetAllAsync<{typeof(T).Name}>.after ToListAsync (count={result.Count}, taskMs={sw.ElapsedMilliseconds})");
            return result;
        }

        public async Task<T> GetByIdAsync<T>(int id) where T : new()
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.FindAsync<T>(id);
        }

        public async Task<int> InsertAsync<T>(T item)
        {
            // PrayerApp.Helpers.PerfLog.Log($"DBService.InsertAsync<{typeof(T).Name}>.entry");
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _db.InsertAsync(item).ConfigureAwait(false);
            sw.Stop();
            // PrayerApp.Helpers.PerfLog.Log($"DBService.InsertAsync<{typeof(T).Name}>.exit (taskMs={sw.ElapsedMilliseconds})");
            return result;
        }

        public async Task<int> UpdateAsync<T>(T item)
        {
            // PrayerApp.Helpers.PerfLog.Log($"DBService.UpdateAsync<{typeof(T).Name}>.entry");
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _db.UpdateAsync(item).ConfigureAwait(false);
            sw.Stop();
            // PrayerApp.Helpers.PerfLog.Log($"DBService.UpdateAsync<{typeof(T).Name}>.exit (taskMs={sw.ElapsedMilliseconds})");
            return result;
        }

        public async Task<int> DeleteAsync<T>(T item)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.DeleteAsync(item);
        }

        public async Task<int> DropTableAsync<T>() where T : new()
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.DropTableAsync<T>();
        }

        public async Task<List<PrayerCardTag>> GetByRequestIdAsync(int prayerRequestId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.Table<PrayerCardTag>()
                .Where(pct => pct.PrayerRequestId == prayerRequestId)
                .ToListAsync();
        }

        public async Task<List<PrayerCardTag>> GetByTagIdAsync(int prayerTagId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.Table<PrayerCardTag>()
                .Where(pct => pct.PrayerTagId == prayerTagId)
                .ToListAsync();
        }

        public async Task<List<PrayerCardTag>> GetByTagIdsAsync(IEnumerable<int> tagIds)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var idSet = tagIds.ToHashSet();
            // sqlite-net-pcl doesn't support Contains in LINQ, so filter in memory
            var all = await _db.Table<PrayerCardTag>().ToListAsync();
            return all.Where(pct => idSet.Contains(pct.PrayerTagId)).ToList();
        }

        public async Task<int> DeleteByTagIdAsync(int prayerTagId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.ExecuteAsync(
                "DELETE FROM PrayerCardTag WHERE PrayerTagId = ?",
                prayerTagId
            );
        }

        public async Task<int> DeleteJunctionRowsByRequestIdAsync(int prayerRequestId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.ExecuteAsync(
                "DELETE FROM PrayerCardTag WHERE PrayerRequestId = ?",
                prayerRequestId
            );
        }

        public async Task<List<LatestInteractionResult>> GetLatestInteractionByPrayerAsync()
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.QueryAsync<LatestInteractionResult>(
                "SELECT PrayerId, MAX(InteractionAt) AS LatestInteractionAt FROM PrayerInteraction GROUP BY PrayerId");
        }

        public async Task<DateTime?> GetMaxInteractionDateAsync()
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var result = await _db.ExecuteScalarAsync<long?>(
                "SELECT MAX(InteractionAt) FROM PrayerInteraction");
            return result.HasValue ? new DateTime(result.Value) : null;
        }

        public async Task<int> DeleteInteractionsByPrayerIdAsync(int prayerId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.ExecuteAsync(
                "DELETE FROM PrayerInteraction WHERE PrayerId = ?", prayerId);
        }

        public async Task<int> CountInteractionsByPrayerIdAsync(int prayerId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PrayerInteraction WHERE PrayerId = ?", prayerId);
        }

        public async Task UnassignBoxFromCardsAsync(int boxId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            await _db.ExecuteAsync(
                "UPDATE PrayerCard SET BoxId = 0 WHERE BoxId = ?", boxId);
        }

        public async Task<List<PrayerCard>> GetCardsByBoxIdAsync(int boxId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.Table<PrayerCard>()
                .Where(c => c.BoxId == boxId)
                .ToListAsync();
        }

        public async Task SeedDataAsync()
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var cardCount = await _db.Table<PrayerCard>().CountAsync();
            if (cardCount > 0) return;

            var generalCard = new PrayerCard
            {
                Title = "General",
                IsFavorite = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            await InsertAsync(generalCard);

            var tagCount = await _db.Table<PrayerTag>().CountAsync();
            if (tagCount == 0)
            {
                await InsertAsync(new PrayerTag { Name = "Urgent", Color = "#FF0000" });
                await InsertAsync(new PrayerTag { Name = "Family", Color = "#0000FF" });
                await InsertAsync(new PrayerTag { Name = "Work", Color = "#00FF00" });
            }

            await InsertAsync(new Prayer
            {
                PrayerCardId = generalCard.Id,
                Title = "Sample Prayer Entry 1",
                Details = "Sample details.",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            await InsertAsync(new Prayer
            {
                PrayerCardId = generalCard.Id,
                Title = "Sample Prayer Entry 2",
                Details = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Duis in sem sit amet sapien tincidunt pretium. Mauris tristique libero tellus, laoreet blandit metus congue non. Ut at sagittis lacus.",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            await InsertAsync(new Prayer
            {
                PrayerCardId = generalCard.Id,
                Title = "Sample Prayer Entry 3",
                Details = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Duis in sem sit amet sapien tincidunt pretium. Mauris tristique libero tellus, laoreet blandit metus congue non. Ut at sagittis lacus. Nullam in felis quam. Phasellus nisi augue, hendrerit non vulputate fermentum, maximus a risus.",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            await InsertAsync(new Prayer
            {
                PrayerCardId = generalCard.Id,
                Title = "Sample Prayer Entry 4",
                Details = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Duis in sem sit amet sapien tincidunt pretium. Mauris tristique libero tellus, laoreet blandit metus congue non. Ut at sagittis lacus. Nullam in felis quam. Phasellus nisi augue, hendrerit non vulputate fermentum, maximus a risus. Phasellus aliquam fringilla libero et feugiat. Nam eget varius mi. Curabitur sit amet rutrum sem. Morbi ut ipsum ex. Nulla est ante, hendrerit vitae mollis quis, fringilla id ligula. Vestibulum id nisi sed nunc finibus egestas. Phasellus eleifend ante at enim ornare auctor a ac dolor. Nullam nec nisi vulputate, ultrices nisi quis, bibendum ligula. Proin fermentum mauris nec ipsum ultrices gravida. Sed faucibus scelerisque massa at porttitor.",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        public async Task<List<Prayer>> GetPrayersByCardIdAsync(int prayerCardId)
        {
            // PrayerApp.Helpers.PerfLog.Log($"DBService.GetPrayersByCardIdAsync({prayerCardId}).entry");
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _db.Table<Prayer>()
                .Where(prayer => prayer.PrayerCardId == prayerCardId)
                .ToListAsync()
                .ConfigureAwait(false);
            sw.Stop();
            // PrayerApp.Helpers.PerfLog.Log($"DBService.GetPrayersByCardIdAsync({prayerCardId}).exit (count={result.Count}, taskMs={sw.ElapsedMilliseconds})");
            return result;
        }

        public async Task<List<PrayerInteraction>> GetInteractionsByPrayerIdAsync(int prayerId)
        {
            await EnsureInitializedAsync();
            if (_db == null) throw new InvalidOperationException("Database is not available.");
            return await _db.Table<PrayerInteraction>()
                .Where(i => i.PrayerId == prayerId)
                .ToListAsync();
        }

        private async Task EnsurePrayerCardColumnsAsync()
        {
            // _db is guaranteed non-null here — callers hold the null guard
            try
            {
                await _db!.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN IsFavorite INTEGER DEFAULT 0");
            }
            catch
            {
            }

            // Confidential Cards (#250): per-card protection mode. Enums persist as INTEGER
            // via sqlite-net; None = 0 is the safe no-op default for existing rows.
            try
            {
                await _db!.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN ProtectionMode INTEGER DEFAULT 0");
            }
            catch
            {
            }

            try
            {
                await _db!.ExecuteAsync("ALTER TABLE PrayerRequest ADD COLUMN AnsweredAt TEXT");
            }
            catch
            {
            }

            // BUG-21: Add PrayerRequestId column to PrayerCardTag — tags now live at the request level
            try
            {
                await _db!.ExecuteAsync("ALTER TABLE PrayerCardTag ADD COLUMN PrayerRequestId INTEGER DEFAULT 0");
            }
            catch
            {
            }

            // BUG-21 data migration: promote legacy card-level tag rows to request-level rows.
            // For each PrayerCardTag row where PrayerRequestId is still 0 (old schema),
            // find all prayer requests on that card and create a new row per request.
            try
            {
                await MigrateCardTagsToRequestTagsAsync();
            }
            catch (Exception ex)
            {
                // Non-fatal: app continues, but existing tag assignments may not be migrated.
                // The exception message here will surface in device logs for diagnosis.
                System.Diagnostics.Debug.WriteLine($"[BUG-21 Migration] MigrateCardTagsToRequestTagsAsync failed — {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[BUG-21 Migration] Stack: {ex.StackTrace}");
            }
        }

        private async Task MigrateCardTagsToRequestTagsAsync()
        {
            if (_db == null) return;

            // Guard: verify PrayerRequestId column is present before querying it.
            // If the ALTER TABLE above failed silently, the column may not exist yet,
            // which would cause a SQLiteException here and crash the app on startup.
            var columnExists = false;
            try
            {
                var rows = await _db.QueryAsync<SQLiteColumnInfo>("PRAGMA table_info(PrayerCardTag)");
                columnExists = rows.Any(r => r.name == "PrayerRequestId");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BUG-21 Migration] PRAGMA table_info failed: {ex.Message}");
                return;
            }

            if (!columnExists)
            {
                System.Diagnostics.Debug.WriteLine("[BUG-21 Migration] PrayerRequestId column not found — skipping migration.");
                return;
            }

            // Find all legacy rows (PrayerRequestId == 0)
            var legacyRows = await _db.Table<PrayerCardTag>()
                .Where(pct => pct.PrayerRequestId == 0)
                .ToListAsync();

            if (legacyRows.Count == 0) return;

            System.Diagnostics.Debug.WriteLine($"[BUG-21 Migration] Migrating {legacyRows.Count} legacy card-level tag row(s).");

            foreach (var legacy in legacyRows)
            {
                // Get all prayer requests on the card this tag was assigned to
                var prayers = await _db.Table<Prayer>()
                    .Where(p => p.PrayerCardId == legacy.PrayerCardId)
                    .ToListAsync();

                foreach (var prayer in prayers)
                {
                    // Avoid duplicates
                    var existing = await _db.Table<PrayerCardTag>()
                        .Where(pct => pct.PrayerRequestId == prayer.Id && pct.PrayerTagId == legacy.PrayerTagId)
                        .FirstOrDefaultAsync();

                    if (existing == null)
                    {
                        await _db.InsertAsync(new PrayerCardTag
                        {
                            PrayerCardId = legacy.PrayerCardId,
                            PrayerTagId = legacy.PrayerTagId,
                            PrayerRequestId = prayer.Id,
                            CreatedAt = legacy.CreatedAt
                        });
                    }
                }

                // Remove the legacy card-level row now that it's been migrated
                await _db.DeleteAsync(legacy);
            }

            System.Diagnostics.Debug.WriteLine("[BUG-21 Migration] Migration complete.");
        }

        /// <summary>
        /// F-24: Seeds System + Archived boxes, adds BoxId/IsArchived/SystemKey columns to PrayerCard,
        /// and migrates existing system/imported cards into the System box.
        /// </summary>
        private async Task EnsureCardBoxMigrationAsync()
        {
            if (_db == null) return;

            // Load all boxes once for both seed checks
            var allBoxes = await _db.Table<CardBox>().ToListAsync();

            // 1. Seed System box if not exists
            var systemBox = allBoxes.FirstOrDefault(b => b.SystemKey == CardBox.SystemKeySystem);
            if (systemBox == null)
            {
                systemBox = new CardBox
                {
                    Name = "System",
                    IsSystem = true,
                    SystemKey = CardBox.SystemKeySystem,
                    SortOrder = 900,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _db.InsertAsync(systemBox);
            }

            // 2. Seed Archived box if not exists
            var archivedBox = allBoxes.FirstOrDefault(b => b.SystemKey == CardBox.SystemKeyArchived);
            if (archivedBox == null)
            {
                archivedBox = new CardBox
                {
                    Name = "Archived",
                    IsSystem = true,
                    SystemKey = CardBox.SystemKeyArchived,
                    SortOrder = 999,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _db.InsertAsync(archivedBox);
            }

            // 3. Persist ArchivedFolderId in Settings for zero-query runtime lookups
            Settings.ArchivedFolderId = archivedBox.Id;

            // 4. Add new columns to PrayerCard
            try { await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN BoxId INTEGER DEFAULT 0"); }
            catch { System.Diagnostics.Debug.WriteLine("[F-24 Migration] BoxId column already exists"); }
            try { await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN SystemKey TEXT"); }
            catch { System.Diagnostics.Debug.WriteLine("[F-24 Migration] SystemKey column already exists"); }

            // BUG-121: remember a card's pre-archive collection so unarchiving can restore it.
            try { await _db.ExecuteAsync("ALTER TABLE PrayerCard ADD COLUMN PreArchiveBoxId INTEGER"); }
            catch { System.Diagnostics.Debug.WriteLine("[BUG-121 Migration] PreArchiveBoxId column already exists"); }

            // 5. Migrate system and imported cards into the System box
            // Only cards with BoxId=0 need migration — already-assigned cards are left alone.
            try
            {
                var rows = await _db.ExecuteAsync(
                    "UPDATE PrayerCard SET BoxId = ? WHERE BoxId = 0 AND (IsSystem = 1 OR IsImported = 1)",
                    systemBox.Id);
                System.Diagnostics.Debug.WriteLine($"[F-24 Migration] Migrated {rows} system/imported cards to System box (id={systemBox.Id})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[F-24 Migration] System card migration FAILED: {ex.Message}");
            }

            // 6. Set SystemKey on known system cards (idempotent — uses WHERE SystemKey IS NULL)
            try
            {
                var qaRows = await _db.ExecuteAsync(
                    "UPDATE PrayerCard SET SystemKey = ? WHERE IsSystem = 1 AND Title = ? AND SystemKey IS NULL",
                    PrayerCard.SystemKeyQuickAdd, PrayerCard.TitleQuickAdd);
                var swmRows = await _db.ExecuteAsync(
                    "UPDATE PrayerCard SET SystemKey = ? WHERE IsSystem = 1 AND Title = ? AND SystemKey IS NULL",
                    PrayerCard.SystemKeySharedWithMe, PrayerCard.TitleSharedWithMe);
                System.Diagnostics.Debug.WriteLine($"[F-24 Migration] SystemKey backfill: quick_add={qaRows}, shared_with_me={swmRows}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[F-24 Migration] SystemKey backfill FAILED: {ex.Message}");
            }
        }

        /// <summary>Maps a single row from PRAGMA table_info(...) for column existence checks.</summary>
        private class SQLiteColumnInfo
        {
            public string name { get; set; } = string.Empty;
        }
    }
}
