using System.Diagnostics;
using PrayerApp.Models;
using PrayerApp.Services;

namespace PrayerApp.UITests.Infrastructure;

/// <summary>
/// Builds a deterministic SQLite seed DB on the host and pushes it to the Android
/// emulator's app data dir before each suite run. Guarantees tests start with a
/// known baseline: user Collections, Cards, Prayers, answered/favorite/tagged
/// examples — so data-dependent tests don't fail just because the emulator is
/// fresh or a destructive test wiped state on a prior run.
///
/// Uses the app's real DBService so the seed schema is always in sync with the
/// app. No-op on iOS for now (follow-on pass once Android is green).
/// </summary>
internal static class TestDataSeed
{
    /// <summary>Seed the platform's device/simulator before the suite runs. No-op off-platform.</summary>
    public static async Task SeedAsync()
    {
        if (TestConfig.IsAndroid)
            await SeedAndroidAsync();
        else if (TestConfig.IsIOS)
            await SeedIOSAsync();
    }

    /// <summary>
    /// Pre-seed iOS NSUserDefaults so the app starts past onboarding. Bypasses the
    /// welcome popup and the entire onboarding flow without depending on Appium to
    /// dismiss it from the UI. Mirrors how Settings.OnboardingComplete is persisted
    /// (Preferences.Set → NSUserDefaults on iOS) — value is read on the next app
    /// launch, which happens when Appium activates the bundle after SeedAsync()
    /// terminates it. No-op on Android (keeps the existing in-suite dismissal
    /// flow until the Android toolchain returns).
    /// </summary>
    public static async Task PreSeedOnboardingCompleteAsync()
    {
        if (!TestConfig.IsIOS) return;

        // Mirrors PrayerApp.Services.Settings: Preferences.Set(nameof(OnboardingComplete), true)
        // — `defaults write -bool YES` is the NSUserDefaults equivalent of bool=true.
        await RunSimctlAsync(
            $"spawn booted defaults write {TestConfig.IOSBundleId} OnboardingComplete -bool YES");
    }

    /// <summary>
    /// Android-only: uninstall the UiAutomator2 instrumentation server packages so the
    /// NEXT Appium session redeploys them clean (issue #191, which also closes #192). On a
    /// long Android run the shared UiAutomator2 instrumentation accumulates state and
    /// eventually wedges ("instrumentation process is not running"), crashing the in-flight
    /// test (#191 ArchiveTests.Archive_Capture_Screenshots) and the test that inherits the
    /// wedged session right after it (#192 Cards_ArchiveChip_MovesCardToArchivedSection).
    /// Folded into the existing session-recreate cadence (AppiumSetup.RecreateDriver) so it
    /// bounds instrumentation drift the same way #164 bounds session drift — riding the one
    /// SessionRecreateCadence counter, no second cadence. Uninstalling makes the fresh
    /// CreateDriver reinstall a clean server.
    /// <para>
    /// These are Appium's OWN helper packages (io.appium.uiautomator2.server[.test]), NOT
    /// the app under test, so this is distinct from the "never adb-uninstall the MAUI Debug
    /// APK" rule — that would wipe the FastDev override assemblies; this only makes Appium
    /// reinstall its instrumentation server on the next connect.
    /// </para>
    /// <para>
    /// Best-effort (mirrors AppiumSetup.ApplyAndroidTreeReadSettings): a not-installed
    /// server exits non-zero (allowFailure) and a missing adb throws — both are swallowed so
    /// recovery NEVER aborts session creation; the session just keeps the wedged
    /// instrumentation. No-op off Android.
    /// </para>
    /// </summary>
    public static async Task RecoverAndroidInstrumentationAsync()
    {
        if (!TestConfig.IsAndroid) return;

        try
        {
            await RunAdbAsync($"uninstall {TestConfig.UiAutomator2ServerPackage}", allowFailure: true);
            await RunAdbAsync($"uninstall {TestConfig.UiAutomator2ServerTestPackage}", allowFailure: true);
        }
        catch
        {
            // adb missing/unreachable shouldn't abort the recreate — see summary.
        }
    }

    public static async Task SeedAndroidAsync()
    {
        if (!TestConfig.IsAndroid) return;

        // Always seed to a fresh temp file — guarantees a clean baseline on each run.
        // If the SeedDataAsync idempotency check (cardCount > 0) ever fired against a
        // pre-populated file, our Family/Work/Friends seed would duplicate; starting
        // from empty avoids that entirely.
        string tempPath = Path.Combine(Path.GetTempPath(), $"prayer_seed_{Guid.NewGuid():N}.db");

        try
        {
            await BuildSeedDbAsync(tempPath);
            await PushSeedToDeviceAsync(tempPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static async Task SeedIOSAsync()
    {
        if (!TestConfig.IsIOS) return;

        // Fresh temp file per run (see SeedAndroidAsync for rationale).
        string tempPath = Path.Combine(Path.GetTempPath(), $"prayer_seed_{Guid.NewGuid():N}.db");

        try
        {
            await BuildSeedDbAsync(tempPath);
            await PushSeedToSimulatorAsync(tempPath);
        }
        catch (Exception ex)
        {
            // Don't block the run on a seed failure — let the suite start against
            // whatever state the sim has and surface data-dependent failures in
            // triage. Matches the Android intent (seed is best-effort baseline).
            Console.WriteLine($"[TestDataSeed] iOS seed failed: {ex.Message}");
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static async Task BuildSeedDbAsync(string path)
    {
        var db = new DBService(path);

        // Wire model static so model.SaveAsync() works during seed.
        // These statics are process-local — they don't affect the app on the device,
        // which runs in a separate process with its own statics.
        PrayerCard.SetDBService(db);
        PrayerTag.SetDBService(db);
        PrayerCardTag.SetDBService(db);
        Prayer.SetDBService(db);
        PrayerInteraction.SetDBService(db);
        CardBox.SetDBService(db);

        // UpdateSchema runs automatically in the constructor, but SeedDataAsync
        // awaits it internally. Calling SeedDataAsync creates the app's first-run
        // baseline ("General" card, three default tags, sample prayers).
        await db.SeedDataAsync();

        // Layer on UITest-specific baseline data.
        await SeedUITestContentAsync();

        await db.CloseAsync();
    }

    private static async Task SeedUITestContentAsync()
    {
        // Three user-created collections (Family / Work / Friends) with cards + prayers,
        // plus a "UITest Collection" / "UITest Card" / "UI Test Prayer" set used by
        // existing tests that probe for those specific names.

        var now = DateTime.Now;

        var familyBox = new CardBox { Name = "Family", IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await familyBox.SaveAsync();

        var workBox = new CardBox { Name = "Work", IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await workBox.SaveAsync();

        var friendsBox = new CardBox { Name = "Friends", IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await friendsBox.SaveAsync();

        var uitestBox = new CardBox { Name = "UITest Collection", IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await uitestBox.SaveAsync();

        await SeedCardWithPrayersAsync(familyBox.Id, "Parents", new[]
        {
            ("Dad's health", "Recovery from surgery.", false),
            ("Mom's travel", "Safe trip home.", false),
        });
        await SeedCardWithPrayersAsync(familyBox.Id, "Kids", new[]
        {
            ("School year", "Strength for the new term.", false),
            ("Friendships", "Kind friends at school.", false),
            ("Sports season", "Safety and fun.", false),
        });

        await SeedCardWithPrayersAsync(workBox.Id, "Team", new[]
        {
            ("Big launch", "Smooth release.", false),
            ("Hiring", "Right candidates.", false),
        }, isFavorite: true);

        await SeedCardWithPrayersAsync(friendsBox.Id, "Small Group", new[]
        {
            ("Weekly meeting", "Meaningful discussion.", false),
            ("Chris's job search", "Open doors.", true), // Answered
        });
        await SeedCardWithPrayersAsync(friendsBox.Id, "College", new[]
        {
            ("Class of '22 reunion", "Good turnout.", false),
        });

        // UITest Card lives at top level (BoxId = 0, "Loose Cards") — NOT inside
        // UITest Collection. User-box accordion sections render collapsed by default
        // on the Prayer Cards page, which would hide a card inside them from the UI
        // tree and break any test that scrolls for "UITest Card" by visible text.
        // Loose Cards renders flat and is always visible.
        await SeedCardWithPrayersAsync(boxId: 0, "UITest Card", new[]
        {
            ("UI Test Prayer", "Canonical test prayer used by existing UI tests.", false),
        });

        // Dedicated throwaway targets for destructive tests. Each delete test
        // targets its own "UITest Delete Target" entry instead of the shared
        // UITest baseline — so UITest Collection / UITest Card remain stable for
        // downstream non-destructive tests regardless of run order.
        var deleteColA = new CardBox { Name = TestSeedFixtures.DeleteCollectionA, IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await deleteColA.SaveAsync();
        await SeedCardWithPrayersAsync(deleteColA.Id, TestSeedFixtures.DeleteCardA, new[]
        {
            ("Throwaway prayer A", "Deleted by Boxes_DeleteCollection_DeleteAllCards.", false),
        });

        var deleteColB = new CardBox { Name = TestSeedFixtures.DeleteCollectionB, IsSystem = false, SortOrder = 0, CreatedAt = now, UpdatedAt = now };
        await deleteColB.SaveAsync();
        await SeedCardWithPrayersAsync(deleteColB.Id, TestSeedFixtures.DeleteCardB, new[]
        {
            ("Throwaway prayer B", "Deleted by Boxes_DeleteCollection_UnassignCards.", false),
        });

        // Standalone throwaway card for Cards_DeleteCard_RemovesFromList.
        // Lives at top level (BoxId = 0, "Loose Cards") so it's always visible —
        // user boxes render as collapsed accordion sections on first load and
        // would hide the card from the UI tree.
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.DeleteCard, new[]
        {
            ("Throwaway prayer", "Deleted by Cards_DeleteCard_RemovesFromList.", false),
        });

        // ─── Per-test disposable fixtures ──────────────────────────────
        // Convention: a test that taps, expands, mutates, or otherwise
        // depends on a specific card should OWN a dedicated seed fixture
        // named after the consuming test. Do NOT share "UITest Card" —
        // it's a read-only canary; other destructive tests (delete / move)
        // can mutate it, which breaks anyone sharing it. All fixtures below
        // live at BoxId = 0
        // (Loose Cards) so they render flat and are always visible.
        // See Lessons/uitest-per-test-disposable-fixtures.md.
        await SeedCardWithPrayersAsync(boxId: 0, "UITest AddPrayer Card",
            Array.Empty<(string, string, bool)>());

        await SeedCardWithPrayersAsync(boxId: 0, "UITest EditPrayer Card", new[]
        {
            ("UITest Edit Prayer",
             "Prayer tapped + edited by Cards_EditPrayerFromCard.", false),
        });

        await SeedCardWithPrayersAsync(boxId: 0, "UITest Expanded Card",
            Array.Empty<(string, string, bool)>());

        // Build-95 fallout: recycled-cell BindingContext-stale fixture.
        // "Recycle Big Card" is expanded + deleted by the test; "Recycle
        // Small Card" is the survivor whose cell may be assigned the Big
        // Card's recycled cell after the section's Reset notification.
        // Pre-fix the inner ContentView.Content kept its first-realize
        // BindingContext pointing at Big, so Big's prayer rows continued
        // to render under Small's header. Five prayers is enough for the
        // recycle path; the realize-storm count (50+) only mattered for
        // the BUG-79/80 crash class, which ships in 1.3.1 build 95.
        var recyclePrayers = new (string Title, string Details, bool Answered)[]
        {
            ("Recycle Big Prayer 0", "Filler 0.", false),
            ("Recycle Big Prayer 1", "Filler 1.", false),
            ("Recycle Big Prayer 2", "Filler 2.", false),
            ("Recycle Big Prayer 3", "Filler 3.", false),
            ("Recycle Big Prayer 4", "Filler 4.", false),
        };
        await SeedCardWithPrayersAsync(boxId: 0, "Recycle Big Card", recyclePrayers);

        await SeedCardWithPrayersAsync(boxId: 0, "Recycle Small Card", new[]
        {
            ("Recycle Small Survivor",
             "Should still be the only prayer visible after Big is deleted.", false),
        });

        // Move-prayer fixtures (TD-20 / #42) — one Source card PER consuming test
        // (isolation Principle 1). Previously all three move tests drained a prayer
        // from ONE shared "Move Source Card", so whichever ran second found it
        // already mutated — the order-dependent flake behind #214. Each test now
        // owns its Source (and, for the two user-target moves, its own empty
        // Target); the system-target test moves into the shared system "Quick Add"
        // card, so it needs no Target here.
        //
        // Every Source carries the SAME 4-prayer shape the tests assume. Each test
        // taps a DISTINCT prayer — One → Reflect, Two → Margin, Four → System — and
        // only its own expanded card's prayers are ever in the UI tree (collapsed
        // cards' subtrees are IsVisible=false), so cloning the identical prayer set
        // into three cards never cross-matches. Prayer Three is unused but kept to
        // preserve the original starting shape. All live at BoxId 0 (Loose Cards)
        // so they render flat and always-visible.
        var movePrayers = new (string Title, string Details, bool Answered)[]
        {
            ("Prayer One",   "First prayer in the move-prayer fixture.", false),
            ("Prayer Two",   "Second prayer in the move-prayer fixture.", false),
            ("Prayer Three", "Third prayer in the move-prayer fixture.", false),
            ("Prayer Four",  "Fourth prayer — moved to a system card in issue #42 test.", false),
        };

        // Cards_MovePrayerBetweenCards_BothCardsReflect: taps Prayer One → its own Target.
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.MoveReflectSourceCard, movePrayers);
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.MoveReflectTargetCard,
            Array.Empty<(string, string, bool)>());

        // Cards_MovePrayer_DoesNotLeaveSourceCardWithStaleExpandedMargin: taps Prayer Two → its own Target.
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.MoveMarginSourceCard, movePrayers);
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.MoveMarginTargetCard,
            Array.Empty<(string, string, bool)>());

        // Cards_MovePrayer_ToSystemCard_TargetExpandsAndShowsMovedPrayer: taps Prayer Four → system "Quick Add".
        await SeedCardWithPrayersAsync(boxId: 0, TestSeedFixtures.MoveSystemSourceCard, movePrayers);
    }

    private static async Task SeedCardWithPrayersAsync(int boxId, string cardTitle,
        (string Title, string Details, bool Answered)[] prayers, bool isFavorite = false)
    {
        var now = DateTime.Now;
        var card = new PrayerCard
        {
            Title = cardTitle,
            BoxId = boxId,
            IsFavorite = isFavorite,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await card.SaveAsync();

        foreach (var (title, details, answered) in prayers)
        {
            var prayer = new Prayer
            {
                PrayerCardId = card.Id,
                Title = title,
                Details = details,
                IsAnswered = answered,
                AnsweredAt = answered ? now : null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await prayer.SaveAsync();
        }
    }

    private static async Task PushSeedToDeviceAsync(string localPath)
    {
        // Per `feedback_android_sqlite_seeding.md`:
        //  1. force-stop the app (else SQLite locks block replacement)
        //  2. adb push to /data/local/tmp (app data dir is restricted to app UID)
        //  3. `run-as` cp into the app's files/prayer_app.db
        //  4. remove any stale WAL/SHM sidecar files so SQLite doesn't merge them
        //     into the freshly seeded DB on next app launch

        var pkg = TestConfig.AndroidPackage;
        var stagePath = TestConfig.AndroidTmpSeedPath;
        var appDb = TestConfig.AndroidAppDbRelativePath;

        await RunAdbAsync($"shell am force-stop {pkg}");

        // Clean any pre-existing tmp seed
        await RunAdbAsync($"shell rm -f {stagePath}", allowFailure: true);

        await RunAdbAsync($"push \"{localPath}\" {stagePath}");

        await RunAdbAsync($"shell run-as {pkg} cp {stagePath} {appDb}");

        // Remove stale WAL/SHM sidecars — if they linger from a prior run, SQLite
        // will replay them on top of our seed and the baseline is no longer clean.
        await RunAdbAsync($"shell run-as {pkg} rm -f {appDb}-wal {appDb}-shm",
            allowFailure: true);

        await RunAdbAsync($"shell rm -f {stagePath}", allowFailure: true);
    }

    private static async Task PushSeedToSimulatorAsync(string localPath)
    {
        //  1. terminate the app so SQLite releases its handle
        //  2. resolve the app's data container via `xcrun simctl get_app_container`
        //  3. File.Copy the seed into <container>/<IOSAppDbRelativePath>
        //  4. remove stale WAL/SHM sidecars so SQLite doesn't replay them on next launch
        //
        // Unlike Android we don't need run-as — simulator container files are
        // directly accessible from the host filesystem.

        var bundleId = TestConfig.IOSBundleId;

        await RunSimctlAsync($"terminate booted {bundleId}", allowFailure: true);

        string? container = await RunSimctlCaptureAsync($"get_app_container booted {bundleId} data");
        if (string.IsNullOrWhiteSpace(container))
            throw new InvalidOperationException(
                $"Could not resolve data container for {bundleId}. Is the app installed on the booted simulator?");

        container = container.Trim();
        var destPath = Path.Combine(container, TestConfig.IOSAppDbRelativePath);
        var destDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destDir);

        File.Copy(localPath, destPath, overwrite: true);

        TryDelete(destPath + "-wal");
        TryDelete(destPath + "-shm");
    }

    /// <summary>Runs an adb command. Throws if adb exits non-zero unless allowFailure is true.</summary>
    private static async Task RunAdbAsync(string arguments, bool allowFailure = false)
    {
        _ = await RunProcessAsync("adb", arguments, allowFailure,
            notFoundHint: "Is Android SDK platform-tools on PATH?");
    }

    /// <summary>Runs an xcrun simctl command. Throws on non-zero exit unless allowFailure is true.</summary>
    private static async Task RunSimctlAsync(string arguments, bool allowFailure = false)
    {
        _ = await RunProcessAsync("xcrun", "simctl " + arguments, allowFailure,
            notFoundHint: "Are Xcode command-line tools installed?");
    }

    /// <summary>Runs `xcrun simctl &lt;args&gt;` and returns stdout. Throws on non-zero exit.</summary>
    private static async Task<string> RunSimctlCaptureAsync(string arguments)
    {
        var (stdout, _) = await RunProcessAsync("xcrun", "simctl " + arguments,
            allowFailure: false, notFoundHint: "Are Xcode command-line tools installed?");
        return stdout;
    }

    /// <summary>
    /// Launches a process and returns its stdout/stderr. Reads streams before
    /// <c>WaitForExitAsync</c> to avoid pipe-buffer deadlocks on large outputs.
    /// </summary>
    private static async Task<(string stdout, string stderr)> RunProcessAsync(
        string fileName, string arguments, bool allowFailure, string notFoundHint)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}. {notFoundHint}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(
                $"{fileName} {arguments} failed (exit {proc.ExitCode}).\nstdout: {stdout}\nstderr: {stderr}");

        return (stdout, stderr);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
