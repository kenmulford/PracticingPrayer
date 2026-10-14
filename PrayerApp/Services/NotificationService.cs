using PrayerApp.Helpers;
using PrayerApp.Models;

namespace PrayerApp.Services;

public class NotificationService : INotificationService
{
    private readonly ILocalNotificationCenter _center;
    private readonly Func<bool> _isNotificationsAllowed;

    public NotificationService(ILocalNotificationCenter center, Func<bool> isNotificationsAllowed)
    {
        _center = center;
        _isNotificationsAllowed = isNotificationsAllowed;
    }

    public Task<bool> RequestPermissionAsync() =>
        _center.RequestNotificationPermission();

    public Task<bool> AreNotificationsEnabledAsync() =>
        _center.AreNotificationsEnabled();

    public Task ClearAllAsync()
    {
        _center.CancelAll();
        return Task.CompletedTask;
    }

    public Task CancelAsync(int notificationId, PrayerFrequency frequency = PrayerFrequency.OneTime)
    {
        if (frequency == PrayerFrequency.Monthly)
            _center.CancelMonthly(notificationId);
        else
            _center.Cancel(notificationId);
        return Task.CompletedTask;
    }

    public async Task ScheduleAsync(Prayer prayer)
    {
        try
        {
        if (!_isNotificationsAllowed())
        {
            System.Diagnostics.Debug.WriteLine($"[Notify] ScheduleAsync SKIPPED — notifications not allowed");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Notify] ScheduleAsync: id={prayer.Id}, freq={prayer.PrayerFrequency}, " +
            $"hour={prayer.NotifyHour}, min={prayer.NotifyMinute}, " +
            $"dayOfWeek={prayer.NotifyDayOfWeek}, dayOfMonth={prayer.NotifyDayOfMonth}, " +
            $"canNotify={prayer.CanNotify}");

        // Cancel any existing notification for this prayer before rescheduling.
        // Cancel both monthly and non-monthly variants since we don't know the
        // previous frequency — the prayer may have changed from Daily to Monthly.
        _center.Cancel(prayer.Id);
        _center.CancelMonthly(prayer.Id);
        System.Diagnostics.Debug.WriteLine($"[Notify] Cancelled existing notifications for id={prayer.Id}");

        var hour = prayer.NotifyHour;
        var minute = prayer.NotifyMinute;

        // Monthly uses native calendar-based scheduling
        if (prayer.PrayerFrequency == PrayerFrequency.Monthly)
        {
            var dayOfMonth = prayer.NotifyDayOfMonth > 0
                ? prayer.NotifyDayOfMonth
                : prayer.CreatedAt.Day;
            System.Diagnostics.Debug.WriteLine($"[Notify] Monthly: dayOfMonth={dayOfMonth} (raw={prayer.NotifyDayOfMonth}, createdAt={prayer.CreatedAt.Day})");
            await _center.ScheduleMonthlyAsync(
                prayer.Id, "Practicing Prayer", prayer.Title,
                dayOfMonth, hour, minute);
            return;
        }

        if (prayer.PrayerFrequency == PrayerFrequency.OneTime)
        {
            var fire = NotificationHelper.OneTimeFire(prayer);
            if (fire <= DateTime.Now)
                return;
            await _center.ShowAsync(prayer.Id, "Practicing Prayer", prayer.Title, fire, NotifyRepeat.No, null);
            return;
        }

        // Daily/Weekly/Yearly — use existing plugin path
        DateTime notifyTime;
        if (prayer.PrayerFrequency == PrayerFrequency.Yearly)
        {
            // One-shot at the next anniversary avoids leap-year drift; a null date anchors on the first fire after creation.
            var anchor = prayer.NotifyDate ?? NotificationHelper.GetFirstFireTime(prayer.CreatedAt, hour, minute).Date;
            notifyTime = NotificationHelper.NextYearlyFire(anchor, hour, minute, DateTime.Now);
        }
        else if (prayer.PrayerFrequency == PrayerFrequency.Weekly && prayer.NotifyDayOfWeek >= 0)
        {
            notifyTime = GetNextDayOfWeek((DayOfWeek)prayer.NotifyDayOfWeek, hour, minute);
        }
        else
        {
            notifyTime = DateTime.Now.Date.AddHours(hour).AddMinutes(minute);
            if (notifyTime <= DateTime.Now)
                notifyTime = notifyTime.AddDays(1);
        }

        var repeatType = prayer.PrayerFrequency switch
        {
            PrayerFrequency.Daily => NotifyRepeat.Daily,
            PrayerFrequency.Weekly => NotifyRepeat.Weekly,
            _ => NotifyRepeat.No
        };

        await _center.ShowAsync(
            prayer.Id,
            "Practicing Prayer",
            prayer.Title,
            notifyTime,
            repeatType,
            null);
        }
        catch (Exception ex)
        {
            // Don't let notification failures block prayer saves
            System.Diagnostics.Debug.WriteLine($"[Notify] ScheduleAsync FAILED: {ex.Message}");
        }
    }

    public async Task RenewMonthlyNotificationsAsync(IEnumerable<Prayer> activePrayers)
    {
        foreach (var prayer in activePrayers
            .Where(p => p.CanNotify && p.PrayerFrequency == PrayerFrequency.Monthly))
        {
            await ScheduleAsync(prayer);
        }
    }

    public async Task ReconcileNotificationsAsync(IEnumerable<Prayer> activePrayers)
    {
        if (!_isNotificationsAllowed()) return;

        // Wipe all pending notifications (plugin + native iOS) to eliminate orphans
        _center.ClearAllPending();

        // Reschedule only prayers that should be notifying
        foreach (var prayer in activePrayers.Where(p => p.CanNotify))
        {
            await ScheduleAsync(prayer);
        }
    }

    internal static DateTime GetNextDayOfWeek(DayOfWeek targetDay, int hour, int minute)
    {
        var now = DateTime.Now;
        var today = now.Date.AddHours(hour).AddMinutes(minute);
        var daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && today <= now)
            daysUntil = 7;
        return today.AddDays(daysUntil);
    }
}
