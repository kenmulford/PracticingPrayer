using NSubstitute;
using PrayerApp.Models;
using PrayerApp.Services;

namespace PrayerApp.Tests.Services;

public class NotificationServiceTests
{
    private readonly ILocalNotificationCenter _center;
    private readonly NotificationService _service;
    private readonly NotificationService _serviceDisabled;

    public NotificationServiceTests()
    {
        _center = Substitute.For<ILocalNotificationCenter>();
        _service         = new NotificationService(_center, () => true);
        _serviceDisabled = new NotificationService(_center, () => false);
    }

    // ── Delegation — simple pass-through methods ───────────────────────────────

    [Fact]
    public async Task RequestPermissionAsync_DelegatesToCenter()
    {
        _center.RequestNotificationPermission().Returns(true);

        var result = await _service.RequestPermissionAsync();

        Assert.True(result);
        await _center.Received(1).RequestNotificationPermission();
    }

    [Fact]
    public async Task AreNotificationsEnabledAsync_DelegatesToCenter()
    {
        _center.AreNotificationsEnabled().Returns(false);

        var result = await _service.AreNotificationsEnabledAsync();

        Assert.False(result);
        await _center.Received(1).AreNotificationsEnabled();
    }

    [Fact]
    public async Task ClearAllAsync_CallsCancelAll()
    {
        await _service.ClearAllAsync();

        _center.Received(1).CancelAll();
    }

    [Fact]
    public async Task CancelAsync_CallsCancelWithCorrectId()
    {
        await _service.CancelAsync(42);

        _center.Received(1).Cancel(42);
    }

    // ── ScheduleAsync — notifications disabled ────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_WhenNotificationsDisabled_DoesNotCallShow()
    {
        var prayer = new Prayer { Id = 1, Title = "Test", PrayerFrequency = PrayerFrequency.Daily };

        await _serviceDisabled.ScheduleAsync(prayer);

        await _center.DidNotReceive().ShowAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
    }

    // ── ScheduleAsync — repeat type mapping ───────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_DailyFrequency_SchedulesWithDailyRepeat()
    {
        var prayer = new Prayer { Id = 1, Title = "Morning", PrayerFrequency = PrayerFrequency.Daily };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            1, "Practicing Prayer", "Morning",
            Arg.Any<DateTime>(), NotifyRepeat.Daily, null);
    }

    [Fact]
    public async Task ScheduleAsync_WeeklyFrequency_SchedulesWithWeeklyRepeat()
    {
        var prayer = new Prayer { Id = 2, Title = "Weekly Check", PrayerFrequency = PrayerFrequency.Weekly };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            2, "Practicing Prayer", "Weekly Check",
            Arg.Any<DateTime>(), NotifyRepeat.Weekly, null);
    }

    [Fact]
    public async Task ScheduleAsync_MonthlyFrequency_CallsScheduleMonthlyAsync()
    {
        var prayer = new Prayer
        {
            Id = 3, Title = "Monthly", PrayerFrequency = PrayerFrequency.Monthly,
            NotifyDayOfMonth = 15, NotifyHour = 8, NotifyMinute = 0
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ScheduleMonthlyAsync(3, "Practicing Prayer", "Monthly", 15, 8, 0);
        await _center.DidNotReceive().ShowAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ScheduleAsync_Monthly_DefaultDayOfMonth_UsesCreatedAtDay()
    {
        var prayer = new Prayer
        {
            Id = 4, Title = "Monthly", PrayerFrequency = PrayerFrequency.Monthly,
            NotifyDayOfMonth = -1, CreatedAt = new DateTime(2026, 3, 20)
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ScheduleMonthlyAsync(4, "Practicing Prayer", "Monthly", 20, 9, 0);
    }

    [Fact]
    public async Task ScheduleAsync_YearlyFrequency_SchedulesOneShot()
    {
        var prayer = new Prayer { Id = 4, Title = "Yearly", PrayerFrequency = PrayerFrequency.Yearly };

        await _service.ScheduleAsync(prayer);

        // Yearly now schedules as a one-shot at the next anniversary to avoid leap-year drift
        await _center.Received(1).ShowAsync(
            4, "Practicing Prayer", "Yearly",
            Arg.Any<DateTime>(), NotifyRepeat.No, null);
    }

    [Fact]
    public async Task ScheduleAsync_OneTimeFrequency_SchedulesWithNoRepeat()
    {
        var prayer = new Prayer { Id = 5, Title = "One Time", PrayerFrequency = PrayerFrequency.OneTime };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            5, "Practicing Prayer", "One Time",
            Arg.Any<DateTime>(), NotifyRepeat.No, null);
    }

    // ── ScheduleAsync — notification time ────────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_UsesDefaultTime_WhenNotCustomized()
    {
        var prayer = new Prayer { Id = 6, Title = "Time Test", PrayerFrequency = PrayerFrequency.Daily };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<DateTime>(d => d.Hour == 9 && d.Minute == 0 && d > DateTime.Now),
            Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ScheduleAsync_UsesPerRequestTime()
    {
        var prayer = new Prayer
        {
            Id = 7, Title = "Custom Time", PrayerFrequency = PrayerFrequency.Daily,
            NotifyHour = 14, NotifyMinute = 30
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            7, "Practicing Prayer", "Custom Time",
            Arg.Is<DateTime>(d => d.Hour == 14 && d.Minute == 30),
            NotifyRepeat.Daily, null);
    }

    [Fact]
    public async Task ScheduleAsync_Weekly_WithDayOfWeek_SchedulesCorrectDay()
    {
        var prayer = new Prayer
        {
            Id = 8, Title = "Tuesday Prayer", PrayerFrequency = PrayerFrequency.Weekly,
            NotifyDayOfWeek = 2, NotifyHour = 7, NotifyMinute = 30
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            8, "Practicing Prayer", "Tuesday Prayer",
            Arg.Is<DateTime>(d => d.DayOfWeek == DayOfWeek.Tuesday && d.Hour == 7 && d.Minute == 30),
            NotifyRepeat.Weekly, null);
    }

    // ── CancelAsync — frequency routing ─────────────────────────────────────

    [Fact]
    public async Task CancelAsync_Monthly_CallsCancelMonthly()
    {
        await _service.CancelAsync(42, PrayerFrequency.Monthly);

        _center.Received(1).CancelMonthly(42);
    }

    [Fact]
    public async Task CancelAsync_NonMonthly_CallsCancel()
    {
        await _service.CancelAsync(42, PrayerFrequency.Daily);

        _center.Received(1).Cancel(42);
    }

    // ── ScheduleAsync — cancel-before-reschedule ─────────────────────────────

    [Fact]
    public async Task ScheduleAsync_CancelsBothVariantsBeforeScheduling()
    {
        var prayer = new Prayer
        {
            Id = 10, Title = "Reschedule", PrayerFrequency = PrayerFrequency.Daily
        };

        await _service.ScheduleAsync(prayer);

        // Should cancel both non-monthly and monthly variants before scheduling
        _center.Received(1).Cancel(10);
        _center.Received(1).CancelMonthly(10);
    }

    [Fact]
    public async Task ScheduleAsync_Monthly_CancelsBothVariantsBeforeScheduling()
    {
        var prayer = new Prayer
        {
            Id = 11, Title = "Monthly Reschedule", PrayerFrequency = PrayerFrequency.Monthly,
            NotifyDayOfMonth = 15, NotifyHour = 8, NotifyMinute = 0
        };

        await _service.ScheduleAsync(prayer);

        // Should cancel both variants before scheduling monthly
        _center.Received(1).Cancel(11);
        _center.Received(1).CancelMonthly(11);
        await _center.Received(1).ScheduleMonthlyAsync(11, "Practicing Prayer", "Monthly Reschedule", 15, 8, 0);
    }

    // ── GetNextDayOfWeek ────────────────────────────────────────────────────

    [Fact]
    public void GetNextDayOfWeek_ReturnsCorrectDayAndTime()
    {
        // Target: next Tuesday at 7:30
        var result = NotificationService.GetNextDayOfWeek(DayOfWeek.Tuesday, 7, 30);

        Assert.Equal(DayOfWeek.Tuesday, result.DayOfWeek);
        Assert.Equal(7, result.Hour);
        Assert.Equal(30, result.Minute);
        Assert.True(result > DateTime.Now);
    }

    // ── RenewMonthlyNotificationsAsync ────────────────────────────────────

    [Fact]
    public async Task RenewMonthly_SchedulesMonthlyPrayers()
    {
        var prayers = new List<Prayer>
        {
            new() { Id = 1, CanNotify = true, PrayerFrequency = PrayerFrequency.Monthly, NotifyHour = 9, NotifyMinute = 0, NotifyDayOfMonth = 15 },
            new() { Id = 2, CanNotify = true, PrayerFrequency = PrayerFrequency.Monthly, NotifyHour = 10, NotifyMinute = 30, NotifyDayOfMonth = 1 }
        };

        await _service.RenewMonthlyNotificationsAsync(prayers);

        await _center.Received(2).ScheduleMonthlyAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task RenewMonthly_SkipsNonMonthlyPrayers()
    {
        var prayers = new List<Prayer>
        {
            new() { Id = 1, CanNotify = true, PrayerFrequency = PrayerFrequency.Daily, NotifyHour = 9, NotifyMinute = 0 },
            new() { Id = 2, CanNotify = true, PrayerFrequency = PrayerFrequency.Weekly, NotifyHour = 9, NotifyMinute = 0 }
        };

        await _service.RenewMonthlyNotificationsAsync(prayers);

        await _center.DidNotReceive().ScheduleMonthlyAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task RenewMonthly_SkipsCanNotifyFalse()
    {
        var prayers = new List<Prayer>
        {
            new() { Id = 1, CanNotify = false, PrayerFrequency = PrayerFrequency.Monthly, NotifyHour = 9, NotifyMinute = 0, NotifyDayOfMonth = 15 }
        };

        await _service.RenewMonthlyNotificationsAsync(prayers);

        await _center.DidNotReceive().ScheduleMonthlyAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task RenewMonthly_EmptyList_NoCalls()
    {
        await _service.RenewMonthlyNotificationsAsync(new List<Prayer>());

        await _center.DidNotReceive().ScheduleMonthlyAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // ── ReconcileNotificationsAsync ───────────────────────────────────────

    [Fact]
    public async Task Reconcile_ClearsAllThenReschedulesNotifyPrayers()
    {
        var prayers = new List<Prayer>
        {
            new() { Id = 1, CanNotify = true, PrayerFrequency = PrayerFrequency.Daily, NotifyHour = 9, NotifyMinute = 0 },
            new() { Id = 2, CanNotify = true, PrayerFrequency = PrayerFrequency.Monthly, NotifyHour = 10, NotifyMinute = 0, NotifyDayOfMonth = 15 },
            new() { Id = 3, CanNotify = false, PrayerFrequency = PrayerFrequency.Weekly, NotifyHour = 8, NotifyMinute = 0 }
        };

        await _service.ReconcileNotificationsAsync(prayers);

        // ClearAllPending called first
        _center.Received(1).ClearAllPending();
        // Only prayers with CanNotify=true get scheduled (ids 1 and 2)
        // Each ScheduleAsync call cancels then schedules, so we check the schedule calls
        // Prayer 1 (Daily) goes through ShowAsync
        await _center.Received(1).ShowAsync(
            Arg.Is(1), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
        // Prayer 2 (Monthly) goes through ScheduleMonthlyAsync
        await _center.Received(1).ScheduleMonthlyAsync(
            Arg.Is(2), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Reconcile_SkipsWhenNotificationsDisabled()
    {
        var prayers = new List<Prayer>
        {
            new() { Id = 1, CanNotify = true, PrayerFrequency = PrayerFrequency.Daily, NotifyHour = 9, NotifyMinute = 0 }
        };

        await _serviceDisabled.ReconcileNotificationsAsync(prayers);

        _center.DidNotReceive().ClearAllPending();
    }

    [Fact]
    public async Task Reconcile_EmptyList_StillClearsOrphans()
    {
        await _service.ReconcileNotificationsAsync(new List<Prayer>());

        // Should still clear to remove any orphaned notifications
        _center.Received(1).ClearAllPending();
    }

    [Fact]
    public void GetNextDayOfWeek_SameDayFutureTime_ReturnsSameDay()
    {
        var today = DateTime.Now.DayOfWeek;
        // Use 23:59 to guarantee it's in the future
        var result = NotificationService.GetNextDayOfWeek(today, 23, 59);

        // If current time is already past 23:59 (unlikely), it'll be next week
        Assert.Equal(today, result.DayOfWeek);
        Assert.Equal(23, result.Hour);
        Assert.Equal(59, result.Minute);
    }

    // ── ScheduleAsync — NotifyDate ────────────────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_OneTimeWithNotifyDate_SchedulesAtThatDateAndTime()
    {
        var date = DateTime.Today.AddDays(3);
        var prayer = new Prayer
        {
            Id = 10, Title = "Once", PrayerFrequency = PrayerFrequency.OneTime,
            NotifyHour = 8, NotifyMinute = 30, NotifyDate = date
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            10, "Practicing Prayer", "Once",
            date.AddHours(8).AddMinutes(30), NotifyRepeat.No, null);
    }

    [Fact]
    public async Task ScheduleAsync_YearlyWithNotifyDate_SchedulesNextOccurrence()
    {
        var prayer = new Prayer
        {
            Id = 11, Title = "Yearly", PrayerFrequency = PrayerFrequency.Yearly,
            NotifyHour = 9, NotifyMinute = 0, NotifyDate = new DateTime(2020, 3, 5)
        };

        await _service.ScheduleAsync(prayer);

        var expected = PrayerApp.Helpers.NotificationHelper.NextYearlyFire(
            prayer.NotifyDate!.Value, 9, 0, DateTime.Now);
        await _center.Received(1).ShowAsync(
            11, "Practicing Prayer", "Yearly", expected, NotifyRepeat.No, null);
    }

    [Fact]
    public async Task ScheduleAsync_YearlyNullNotifyDate_AnchorsOnCreationDate()
    {
        var created = DateTime.Today.AddDays(-40).AddHours(8);
        var prayer = new Prayer
        {
            Id = 13, Title = "Yearly legacy", PrayerFrequency = PrayerFrequency.Yearly,
            NotifyHour = 9, NotifyMinute = 0, CreatedAt = created
        };

        await _service.ScheduleAsync(prayer);

        var expected = PrayerApp.Helpers.NotificationHelper.NextYearlyFire(created.Date, 9, 0, DateTime.Now);
        await _center.Received(1).ShowAsync(
            13, "Practicing Prayer", "Yearly legacy", expected, NotifyRepeat.No, null);
    }

    [Fact]
    public async Task ScheduleAsync_OneTimeWithPastNotifyDate_SchedulesNothing()
    {
        var prayer = new Prayer
        {
            Id = 12, Title = "Past", PrayerFrequency = PrayerFrequency.OneTime,
            NotifyHour = 8, NotifyMinute = 0, NotifyDate = DateTime.Today.AddDays(-2)
        };

        await _service.ScheduleAsync(prayer);

        _center.Received(1).Cancel(12);
        await _center.DidNotReceive().ShowAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ScheduleAsync_OneTimeNullNotifyDateFirstFirePassed_SchedulesNothing()
    {
        var prayer = new Prayer
        {
            Id = 14, Title = "Legacy past", PrayerFrequency = PrayerFrequency.OneTime,
            NotifyHour = 9, NotifyMinute = 0, NotifyDate = null,
            CreatedAt = DateTime.Today.AddDays(-1).AddHours(8)
        };

        await _service.ScheduleAsync(prayer);

        _center.Received(1).Cancel(14);
        await _center.DidNotReceive().ShowAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<NotifyRepeat>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ScheduleAsync_OneTimeNullNotifyDateCreatedAfterTime_SchedulesNextDay()
    {
        var createdAt = DateTime.Today.AddHours(21).AddMinutes(45);
        var prayer = new Prayer
        {
            Id = 15, Title = "Legacy late", PrayerFrequency = PrayerFrequency.OneTime,
            NotifyHour = 9, NotifyMinute = 0, NotifyDate = null, CreatedAt = createdAt
        };

        await _service.ScheduleAsync(prayer);

        await _center.Received(1).ShowAsync(
            15, "Practicing Prayer", "Legacy late",
            DateTime.Today.AddDays(1).AddHours(9), NotifyRepeat.No, null);
    }

    [Fact]
    public async Task ScheduleAsync_OneTimeNullNotifyDate_KeepsTodayOrTomorrow()
    {
        var prayer = new Prayer
        {
            Id = 13, Title = "Legacy", PrayerFrequency = PrayerFrequency.OneTime,
            NotifyHour = 9, NotifyMinute = 0, NotifyDate = null
        };

        await _service.ScheduleAsync(prayer);

        var call = _center.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ILocalNotificationCenter.ShowAsync));
        var fire = (DateTime)call.GetArguments()[3]!;
        Assert.True(fire > DateTime.Now);
        Assert.True(fire - DateTime.Now <= TimeSpan.FromHours(24));
    }
}
