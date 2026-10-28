using System.Security.Cryptography;
using PrayerApp.Models;
using SQLite;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Reads every row of the seven tables from the committed SQLite fixtures under contracts/db/.
/// Both files were written by the MAUI app: a fresh v1.7.2 install, and a 1.0.0 install upgraded
/// in place to v1.7.2. All rows are synthetic. A missing fixture fails the test; it never skips.
/// </summary>
public class ContractDbFixtureTests : IDisposable
{
    private const string FreshInstallPath = "contracts/db/fresh-install.db";
    private const string LongUpgradedPath = "contracts/db/long-upgraded.db";

    private readonly List<string> _tempPaths = new();

    static ContractDbFixtureTests()
    {
        // The desktop test host never runs MauiProgram, so register the native SQLitePCLRaw provider here.
        SQLitePCL.Batteries_V2.Init();
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(path + suffix))
                    File.Delete(path + suffix);
    }

    private static string RequireFixture(string relativePath)
    {
        var fullPath = Path.GetFullPath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Contract DB fixture not found at {fullPath}", fullPath);
        return fullPath;
    }

    private async Task<Dictionary<string, int>> ReadAllTablesAsync(string relativePath)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pp_contract_db_{Guid.NewGuid():N}.db");
        _tempPaths.Add(tempPath);
        File.Copy(RequireFixture(relativePath), tempPath);

        var connection = new SQLiteAsyncConnection(tempPath, storeDateTimeAsTicks: true);
        try
        {
            return new Dictionary<string, int>
            {
                ["PrayerCard"] = (await connection.Table<PrayerCard>().ToListAsync()).Count,
                ["PrayerRequest"] = (await connection.Table<Prayer>().ToListAsync()).Count,
                ["PrayerTag"] = (await connection.Table<PrayerTag>().ToListAsync()).Count,
                ["PrayerCardTag"] = (await connection.Table<PrayerCardTag>().ToListAsync()).Count,
                ["PrayerInteraction"] = (await connection.Table<PrayerInteraction>().ToListAsync()).Count,
                ["UserColor"] = (await connection.Table<UserColor>().ToListAsync()).Count,
                ["CardBox"] = (await connection.Table<CardBox>().ToListAsync()).Count,
            };
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    [Fact]
    public async Task FreshInstall_ReadsEveryRow_WithCapturedCounts()
    {
        var counts = await ReadAllTablesAsync(FreshInstallPath);

        Assert.Equal(1, counts["PrayerCard"]);
        Assert.Equal(0, counts["PrayerRequest"]);
        Assert.Equal(1, counts["PrayerTag"]);
        Assert.Equal(0, counts["PrayerCardTag"]);
        Assert.Equal(0, counts["PrayerInteraction"]);
        Assert.Equal(8, counts["UserColor"]);
        Assert.Equal(2, counts["CardBox"]);
    }

    [Fact]
    public async Task LongUpgraded_ReadsEveryRow_WithCapturedCounts()
    {
        var counts = await ReadAllTablesAsync(LongUpgradedPath);

        Assert.Equal(4, counts["PrayerCard"]);
        Assert.Equal(7, counts["PrayerRequest"]);
        Assert.Equal(6, counts["PrayerTag"]);
        Assert.Equal(2, counts["PrayerCardTag"]);
        Assert.Equal(3, counts["PrayerInteraction"]);
        Assert.Equal(8, counts["UserColor"]);
        Assert.Equal(2, counts["CardBox"]);
        Assert.All(counts.Values, count => Assert.True(count >= 1));
    }

    [Fact]
    public void MissingFixture_FailsNamingExpectedPath()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => RequireFixture("contracts/db/absent.db"));

        Assert.Contains(Path.GetFullPath("contracts/db/absent.db"), ex.Message);
    }

    [Fact]
    public void Fixtures_HaveNoSidecarFiles()
    {
        foreach (var fixture in new[] { FreshInstallPath, LongUpgradedPath })
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                Assert.False(File.Exists(Path.GetFullPath(fixture) + suffix), $"{fixture}{suffix} must not exist");
    }

    [Fact]
    public async Task ReadAllTablesAsync_LeavesCommittedFixtureUnchanged()
    {
        var path = RequireFixture(LongUpgradedPath);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));

        await ReadAllTablesAsync(LongUpgradedPath);

        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        Assert.Equal(before, after);
    }
}
