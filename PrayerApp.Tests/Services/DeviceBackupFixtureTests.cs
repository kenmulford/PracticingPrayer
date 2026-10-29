using PrayerApp.Models;
using SQLite;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Skips unless PP_DEVICE_BACKUP_DB names an existing file, so CI and fresh clones never need the backup.
/// The skip reason never includes the path.
/// </summary>
public sealed class DeviceBackupFactAttribute : FactAttribute
{
    public const string VariableName = "PP_DEVICE_BACKUP_DB";

    public DeviceBackupFactAttribute()
    {
        Skip = SkipReasonFor(Environment.GetEnvironmentVariable(VariableName));
    }

    internal static string? SkipReasonFor(string? path) =>
        string.IsNullOrEmpty(path) || !File.Exists(path)
            ? $"{VariableName} is unset or does not name an existing file"
            : null;
}

/// <summary>
/// Local-only: reads every row of the seven tables from a copy of a real device backup. Failure messages
/// carry the table name and exception type only, because exception text can include column values.
/// </summary>
public class DeviceBackupFixtureTests
{
    static DeviceBackupFixtureTests()
    {
        // The desktop test host never runs MauiProgram, so register the native SQLitePCLRaw provider here.
        SQLitePCL.Batteries_V2.Init();
    }

    [DeviceBackupFact]
    public async Task DeviceBackup_ReadsEveryRowOfSevenTables()
    {
        var path = Environment.GetEnvironmentVariable(DeviceBackupFactAttribute.VariableName)!;
        var connection = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadOnly, storeDateTimeAsTicks: true);
        try
        {
            await ReadTable("PrayerCard", () => connection.Table<PrayerCard>().ToListAsync());
            await ReadTable("PrayerRequest", () => connection.Table<Prayer>().ToListAsync());
            await ReadTable("PrayerTag", () => connection.Table<PrayerTag>().ToListAsync());
            await ReadTable("PrayerCardTag", () => connection.Table<PrayerCardTag>().ToListAsync());
            await ReadTable("PrayerInteraction", () => connection.Table<PrayerInteraction>().ToListAsync());
            await ReadTable("UserColor", () => connection.Table<UserColor>().ToListAsync());
            await ReadTable("CardBox", () => connection.Table<CardBox>().ToListAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task ReadTable<T>(string table, Func<Task<List<T>>> read)
    {
        try
        {
            await read();
        }
        catch (Exception ex)
        {
            Assert.Fail($"{DeviceBackupFactAttribute.VariableName}: reading {table} failed ({ex.GetType().Name})");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pp-device-backup-absent/prayer_app.db")]
    public void SkipReasonFor_SkipsWhenUnsetEmptyOrMissing(string? path)
    {
        var reason = DeviceBackupFactAttribute.SkipReasonFor(path);

        Assert.NotNull(reason);
        Assert.Contains(DeviceBackupFactAttribute.VariableName, reason);
    }

    [Fact]
    public void SkipReasonFor_RunsWhenFileExists()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Null(DeviceBackupFactAttribute.SkipReasonFor(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
