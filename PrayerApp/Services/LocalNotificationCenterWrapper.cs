using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
#if IOS
using Foundation;
using UserNotifications;
#endif

namespace PrayerApp.Services;

/// <summary>
/// Production implementation of <see cref="ILocalNotificationCenter"/>.
/// Delegates to the <c>Plugin.LocalNotification</c> static singleton and translates
/// our <see cref="NotifyRepeat"/> enum to the plugin's <c>NotificationRepeat</c>.
/// </summary>
public class LocalNotificationCenterWrapper : ILocalNotificationCenter
{
    public const string PrayerRemindersChannelId = "prayer_reminders";

    /// <summary>Offset for monthly Android notification IDs to avoid colliding with prayer IDs.</summary>
    private const int MonthlyIdOffset = 1_000_000;

    public event EventHandler<int>? NotificationTapped;

    public LocalNotificationCenterWrapper()
    {
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;
    }

    private void OnNotificationActionTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
    {
        var notificationId = e.Request?.NotificationId ?? 0;
        if (notificationId > 0)
            NotificationTapped?.Invoke(this, notificationId);
    }

    public Task<bool> RequestNotificationPermission() =>
        LocalNotificationCenter.Current.RequestNotificationPermission();

    public Task<bool> AreNotificationsEnabled() =>
        LocalNotificationCenter.Current.AreNotificationsEnabled();

    public void CancelAll() =>
        LocalNotificationCenter.Current.CancelAll();

    public void ClearAllPending()
    {
        LocalNotificationCenter.Current.CancelAll();

#if IOS
        // Also clear native UNCalendarNotificationTrigger monthly notifications
        // which are scheduled outside the plugin via UNUserNotificationCenter directly
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
#endif
    }

    public void Cancel(params int[] notificationIds) =>
        LocalNotificationCenter.Current.Cancel(notificationIds);

    public void CancelMonthly(int notificationId)
    {
        // Always cancel legacy Plugin one-shots (IDs notificationId*100+0 through +11)
        // so existing users don't get duplicates after the native scheduling migration.
        Cancel(Enumerable.Range(0, 12).Select(i => MonthlyIdOffset + notificationId * 100 + i).ToArray());

#if IOS
        // Also cancel the native repeating notification
        var identifier = $"monthly_{notificationId}";
        System.Diagnostics.Debug.WriteLine($"[Notify] CancelMonthly (iOS native): removing identifier={identifier}");
        UNUserNotificationCenter.Current.RemovePendingNotificationRequests(new[] { identifier });
#endif
    }

    public async Task ScheduleMonthlyAsync(int notificationId, string title, string description,
                                            int dayOfMonth, int hour, int minute)
    {
        System.Diagnostics.Debug.WriteLine($"[Notify] ScheduleMonthlyAsync: notifId={notificationId}, dayOfMonth={dayOfMonth}, hour={hour}, min={minute}");

#if IOS
        // Use native UNCalendarNotificationTrigger for monthly repeat on iOS.
        // Plugin.LocalNotification's NotificationRepeat.No one-shots don't fire on iOS.
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = description,
            Sound = UNNotificationSound.Default
        };

        var dateComponents = new NSDateComponents
        {
            Day = dayOfMonth,
            Hour = hour,
            Minute = minute
        };

        var trigger = UNCalendarNotificationTrigger.CreateTrigger(dateComponents, repeats: true);
        var identifier = $"monthly_{notificationId}";
        var request = UNNotificationRequest.FromIdentifier(identifier, content, trigger);

        System.Diagnostics.Debug.WriteLine($"[Notify] iOS native monthly: id={identifier}, day={dayOfMonth}, hour={hour}, min={minute}, repeats=true");

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            UNUserNotificationCenter.Current.AddNotificationRequest(request, (error) =>
            {
                if (error != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Notify] iOS native monthly FAILED: {error.LocalizedDescription}");
                    tcs.SetResult(false);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Notify] iOS native monthly SCHEDULED successfully");
                    tcs.SetResult(true);
                }
            });
            await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notify] iOS native monthly EXCEPTION: {ex.Message}");
        }
#else
        // Android: schedule the next 12 monthly occurrences as individual one-shot notifications
        var now = DateTime.Now;
        var scheduled = 0;
        for (int i = 0; i < 12; i++)
        {
            var baseDate = new DateTime(now.Year, now.Month, 1).AddMonths(i);
            var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(baseDate.Year, baseDate.Month));
            var target = new DateTime(baseDate.Year, baseDate.Month, day, hour, minute, 0);
            if (target <= now)
            {
                System.Diagnostics.Debug.WriteLine($"[Notify]   month {i}: SKIPPED target={target} <= now");
                continue;
            }

            System.Diagnostics.Debug.WriteLine($"[Notify]   month {i}: SCHEDULING id={MonthlyIdOffset + notificationId * 100 + i}, target={target}");
            await ShowAsync(MonthlyIdOffset + notificationId * 100 + i, title, description,
                            target, NotifyRepeat.No, null);
            scheduled++;
        }
        System.Diagnostics.Debug.WriteLine($"[Notify] ScheduleMonthlyAsync: scheduled {scheduled} notifications");
#endif
    }

    public Task ShowAsync(int notificationId, string title, string description,
                          DateTime notifyTime, NotifyRepeat repeat, TimeSpan? repeatInterval)
    {
        System.Diagnostics.Debug.WriteLine($"[Notify] ShowAsync: id={notificationId}, time={notifyTime}, repeat={repeat}, interval={repeatInterval}");
        var request = new NotificationRequest
        {
            NotificationId = notificationId,
            Title = title,
            Description = description,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = notifyTime,
                RepeatType = repeat switch
                {
                    NotifyRepeat.Daily         => NotificationRepeat.Daily,
                    NotifyRepeat.Weekly        => NotificationRepeat.Weekly,
                    NotifyRepeat.TimeInterval  => NotificationRepeat.TimeInterval,
                    _                          => NotificationRepeat.No
                },
                NotifyRepeatInterval = repeatInterval,
#if ANDROID
                Android = new AndroidScheduleOptions
                {
                    AlarmType = AndroidAlarmType.RtcWakeup,
                    // Without SCHEDULE_EXACT_ALARM (denied by default on Android 14+) alarms fire ~1m20s late;
                    // the plugin's 1-minute default would drop them as outside the valid range.
                    AllowedDelay = TimeSpan.FromHours(1)
                }
#endif
            },
#if ANDROID
            Android = new AndroidOptions
            {
                ChannelId = PrayerRemindersChannelId
            }
#endif
        };

        return LocalNotificationCenter.Current.Show(request);
    }
}
