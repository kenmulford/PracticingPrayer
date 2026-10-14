using PrayerApp.Helpers;
using PrayerApp.Models;
using SQLite;

namespace PrayerApp.Services;

internal static class NotifyDateMigration
{
    // The NotifyDate column itself comes from CreateTableAsync<Prayer>() in DBService.UpdateSchema.
    // Runs in C# rather than SQL date arithmetic so DateTime binding follows the connection's storage format.
    public static async Task<int> ApplyAsync(SQLiteAsyncConnection db)
    {
        var count = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var rows = conn.Query<Prayer>(
                "SELECT * FROM PrayerRequest WHERE PrayerFrequency IN (?, ?) AND NotifyDate IS NULL",
                (int)PrayerFrequency.OneTime, (int)PrayerFrequency.Yearly);
            foreach (var row in rows)
                conn.Execute("UPDATE PrayerRequest SET NotifyDate = ? WHERE Id = ?",
                    NotificationHelper.GetFirstFireTime(row.CreatedAt, row.NotifyHour, row.NotifyMinute).Date, row.Id);
            count = rows.Count;
        });
        return count;
    }
}
