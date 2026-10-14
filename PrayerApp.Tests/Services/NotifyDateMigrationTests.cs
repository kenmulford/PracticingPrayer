using PrayerApp.Models;
using PrayerApp.Services;
using SQLite;
using Xunit;

namespace PrayerApp.Tests.Services;

public class NotifyDateMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pp_notifydate_test_{Guid.NewGuid():N}.db");
    private static readonly DateTime CreatedAt = new(2026, 4, 17, 21, 45, 0);
    private static readonly DateTime FirstFireDate = CreatedAt.Date.AddDays(1);

    static NotifyDateMigrationTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private async Task<SQLiteAsyncConnection> CreateSchemaAsync()
    {
        var connection = new SQLiteAsyncConnection(_dbPath);
        await connection.CreateTableAsync<Prayer>();
        return connection;
    }

    private static Prayer Row(string title, PrayerFrequency frequency, DateTime? notifyDate = null) =>
        new() { Title = title, PrayerFrequency = frequency, NotifyDate = notifyDate, CreatedAt = CreatedAt, NotifyHour = 9, NotifyMinute = 0 };

    private static async Task<DateTime?> NotifyDateOf(SQLiteAsyncConnection db, string title) =>
        (await db.Table<Prayer>().Where(p => p.Title == title).FirstAsync()).NotifyDate;

    [Fact]
    public async Task ApplyAsync_NullOneTimeAndYearly_BackfillsFirstFireDate()
    {
        var db = await CreateSchemaAsync();
        await db.InsertAllAsync(new[] { Row("once", PrayerFrequency.OneTime), Row("yearly", PrayerFrequency.Yearly) });

        await NotifyDateMigration.ApplyAsync(db);

        Assert.Equal(FirstFireDate, await NotifyDateOf(db, "once"));
        Assert.Equal(FirstFireDate, await NotifyDateOf(db, "yearly"));
        await db.CloseAsync();
    }

    [Fact]
    public async Task ApplyAsync_DailyWeeklyMonthly_LeftNull()
    {
        var db = await CreateSchemaAsync();
        await db.InsertAllAsync(new[]
        {
            Row("daily", PrayerFrequency.Daily), Row("weekly", PrayerFrequency.Weekly), Row("monthly", PrayerFrequency.Monthly)
        });

        await NotifyDateMigration.ApplyAsync(db);

        Assert.Null(await NotifyDateOf(db, "daily"));
        Assert.Null(await NotifyDateOf(db, "weekly"));
        Assert.Null(await NotifyDateOf(db, "monthly"));
        await db.CloseAsync();
    }

    [Fact]
    public async Task ApplyAsync_ExistingNotifyDate_Unchanged()
    {
        var db = await CreateSchemaAsync();
        var existing = new DateTime(2027, 3, 5);
        await db.InsertAsync(Row("set", PrayerFrequency.OneTime, existing));

        await NotifyDateMigration.ApplyAsync(db);

        Assert.Equal(existing, await NotifyDateOf(db, "set"));
        await db.CloseAsync();
    }

    [Fact]
    public async Task ApplyAsync_SecondRun_NoOp()
    {
        var db = await CreateSchemaAsync();
        await db.InsertAsync(Row("once", PrayerFrequency.OneTime));
        await NotifyDateMigration.ApplyAsync(db);

        var updated = await NotifyDateMigration.ApplyAsync(db);

        Assert.Equal(0, updated);
        Assert.Equal(FirstFireDate, await NotifyDateOf(db, "once"));
        await db.CloseAsync();
    }
}
