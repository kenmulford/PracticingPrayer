using PrayerApp.Helpers;
using PrayerApp.Models;

namespace PrayerApp.Tests.Helpers;

public class NotificationHelperTests
{
    private static Prayer MakePrayer(
        int id,
        PrayerFrequency frequency,
        DateTime createdAt,
        bool canNotify = true,
        bool isAnswered = false) =>
        new()
        {
            Id = id,
            Title = $"Prayer {id}",
            PrayerFrequency = frequency,
            CreatedAt = createdAt,
            CanNotify = canNotify,
            IsAnswered = isAnswered,
        };

    // ── Filter guards ──────────────────────────────────────────────────────────

    [Fact]
    public void ExcludesPrayers_WhenCanNotifyIsFalse()
    {
        // Created 2 days ago, daily — would qualify if CanNotify were true
        var now = new DateTime(2026, 3, 22, 10, 0, 0); // 10 AM
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now.AddDays(-2), canNotify: false);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    [Fact]
    public void ExcludesPrayers_WhenIsAnsweredIsTrue()
    {
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now.AddDays(-2), isAnswered: true);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    [Fact]
    public void ExcludesPrayers_WhenFirstFireTimeIsInFuture()
    {
        // Created 30 minutes ago; first fire is tomorrow 9 AM
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now.AddMinutes(-30));

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── Daily ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Daily_IncludedWhenFiredToday()
    {
        // Created 3 days ago. Now is 10 AM → today's 9 AM notification fired 1 hour ago.
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now.AddDays(-3));

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void Daily_IncludedWhenNowIsBefore9AM_FiredYesterday()
    {
        // Created 3 days ago. Now is 7 AM → yesterday's 9 AM was 22 hours ago (within 24h).
        var now = new DateTime(2026, 3, 22, 7, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now.AddDays(-3));

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void Daily_ExcludedWhenCreatedToday_FirstFireTomorrow()
    {
        // Created today at 10 AM. First fire = tomorrow 9 AM. Not yet notified.
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Daily, now);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── Weekly ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Weekly_IncludedOnFireDay()
    {
        // Created on March 8 (Sunday) at 10 AM. First fire = March 9 (Monday) 9 AM.
        // Fires: Mar 9, Mar 16, Mar 23. Now = Mar 16 at 10 AM → fired 1h ago.
        var created = new DateTime(2026, 3, 8, 10, 0, 0);
        var now = new DateTime(2026, 3, 16, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Weekly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void Weekly_ExcludedOnNonFireDay()
    {
        // Created on March 8 (Sunday) at 10 AM. First fire = March 9 (Monday) 9 AM.
        // Next fires: Mar 9, Mar 16, Mar 23. Now = Mar 14 (Saturday) 10 AM → last fire was Mar 9 (5 days ago).
        var created = new DateTime(2026, 3, 8, 10, 0, 0);
        var now = new DateTime(2026, 3, 14, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Weekly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── Monthly (30-day interval) ──────────────────────────────────────────────

    [Fact]
    public void Monthly_IncludedOnFireDay()
    {
        // Created Jan 1 at 10 AM. First fire = Jan 2 at 9 AM.
        // Monthly fires on the 2nd each month. Now = Feb 2 at 10 AM → fired 1h ago.
        var created = new DateTime(2026, 1, 1, 10, 0, 0);
        var now = new DateTime(2026, 2, 2, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Monthly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void Monthly_ExcludedBetweenFireDays()
    {
        // Created Jan 1 at 10 AM. First fire = Jan 2 at 9 AM.
        // Now = Jan 20 → last fire was Jan 2 (18 days ago).
        var created = new DateTime(2026, 1, 1, 10, 0, 0);
        var now = new DateTime(2026, 1, 20, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Monthly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── Yearly (365-day interval) ──────────────────────────────────────────────

    [Fact]
    public void Yearly_IncludedOnFireDay()
    {
        // Created Jan 1, 2025 at 10 AM. First fire = Jan 2, 2025 at 9 AM.
        // 365-day interval: next fire = Jan 2, 2026. Now = Jan 2, 2026 at 10 AM.
        var created = new DateTime(2025, 1, 1, 10, 0, 0);
        var now = new DateTime(2026, 1, 2, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Yearly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void Yearly_ExcludedOnNonFireDay()
    {
        // Created Jan 1, 2025. First fire = Jan 2, 2025 at 9 AM.
        // Now = Jun 15, 2025 → last fire was Jan 2 (months ago).
        var created = new DateTime(2025, 1, 1, 10, 0, 0);
        var now = new DateTime(2025, 6, 15, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.Yearly, created);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── OneTime ────────────────────────────────────────────────────────────────

    [Fact]
    public void OneTime_IncludedOnFireDay()
    {
        // Created March 21 at 10 AM. First (and only) fire = March 22 at 9 AM. Now = March 22 at 10 AM.
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.OneTime, new DateTime(2026, 3, 21, 10, 0, 0));

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void OneTime_ExcludedAfterFireDay()
    {
        // Created March 19 at 8 AM. First fire = March 20 at 9 AM. Now = March 22 at 10 AM (>24h).
        var now = new DateTime(2026, 3, 22, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.OneTime, new DateTime(2026, 3, 19, 8, 0, 0));

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    // ── Multiple prayers ───────────────────────────────────────────────────────

    [Fact]
    public void ReturnsOnlyMatchingPrayerIds()
    {
        var now = new DateTime(2026, 3, 22, 10, 0, 0);

        var prayers = new[]
        {
            MakePrayer(1, PrayerFrequency.Daily, now.AddDays(-5)),           // included
            MakePrayer(2, PrayerFrequency.Daily, now.AddDays(-5), canNotify: false), // excluded
            MakePrayer(3, PrayerFrequency.OneTime, now.AddDays(-5)),         // excluded (>24h)
            MakePrayer(4, PrayerFrequency.Daily, now),                       // excluded (first fire tomorrow)
        };

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(prayers, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void ReturnsEmptyList_WhenNoPrayersProvided()
    {
        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(
            Array.Empty<Prayer>(), DateTime.Now);

        Assert.Empty(result);
    }

    // ── NotifyDate ─────────────────────────────────────────────────────────────

    [Fact]
    public void NextYearlyFire_Feb29_MapsToFeb28InNonLeapYear()
    {
        var result = NotificationHelper.NextYearlyFire(
            new DateTime(2024, 2, 29), 9, 0, new DateTime(2027, 1, 10, 8, 0, 0));

        Assert.Equal(new DateTime(2027, 2, 28, 9, 0, 0), result);
    }

    [Fact]
    public void GetRecentlyNotifiedPrayerIds_OneTimeNotifyDateInWindow_Returned()
    {
        var now = new DateTime(2027, 2, 28, 10, 0, 0);
        var prayer = MakePrayer(1, PrayerFrequency.OneTime, new DateTime(2027, 1, 1));
        prayer.NotifyDate = new DateTime(2027, 2, 28);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 1 }, result);
    }

    [Fact]
    public void GetRecentlyNotifiedPrayerIds_YearlyFeb29InNonLeapYear_Returned()
    {
        var now = new DateTime(2027, 2, 28, 10, 0, 0);
        var prayer = MakePrayer(2, PrayerFrequency.Yearly, new DateTime(2024, 1, 1));
        prayer.NotifyDate = new DateTime(2024, 2, 29);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Equal(new[] { 2 }, result);
    }

    [Fact]
    public void GetRecentlyNotifiedPrayerIds_YearlyCreatedAfterThisYearsFire_NotReturned()
    {
        var now = new DateTime(2027, 3, 5, 10, 0, 0);
        var prayer = MakePrayer(3, PrayerFrequency.Yearly, new DateTime(2027, 3, 5, 9, 30, 0));
        prayer.NotifyDate = new DateTime(2027, 3, 5);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRecentlyNotifiedPrayerIds_OneTimeCreatedAfterItsDate_NotReturned()
    {
        var now = new DateTime(2027, 3, 5, 10, 0, 0);
        var prayer = MakePrayer(4, PrayerFrequency.OneTime, new DateTime(2027, 3, 5, 9, 30, 0));
        prayer.NotifyDate = new DateTime(2027, 3, 5);

        var result = NotificationHelper.GetRecentlyNotifiedPrayerIds(new[] { prayer }, now);

        Assert.Empty(result);
    }
}
