using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PrayerApp.Helpers;
using Plugin.LocalNotification;

namespace PrayerApp.Services
{
    public static class Settings
    {
        private static INotificationService? _notificationService;

        public static void ConfigureNotificationService(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public static bool FirstRun
        {
            get => Preferences.Get(nameof(FirstRun), true);
            set => Preferences.Set(nameof(FirstRun), value);
        }

        /// <summary>Seconds per card in auto-mode. Persisted across sessions.</summary>
        public static int AutoModeIntervalSeconds
        {
            get => Preferences.Get(nameof(AutoModeIntervalSeconds), 30);
            set => Preferences.Set(nameof(AutoModeIntervalSeconds), value);
        }

        /// <summary>Number of days without prayer before a request appears in the "overdue" list. Default 30.</summary>
        public static int OverdueDayThreshold
        {
            get => Preferences.Get(nameof(OverdueDayThreshold), 30);
            set => Preferences.Set(nameof(OverdueDayThreshold), Math.Max(1, value));
        }

        public static bool OnboardingComplete
        {
            get => Preferences.Get(nameof(OnboardingComplete), false);
            set => Preferences.Set(nameof(OnboardingComplete), value);
        }

        /// <summary>
        /// Persisted onboarding step as the OnboardingStep enum's string name. The key string
        /// ("OnboardingStep") is unchanged from when OnboardingService wrote it directly, so
        /// existing installs migrate seamlessly. Default "None" for new installs.
        /// </summary>
        public static string OnboardingStep
        {
            get => Preferences.Get(nameof(OnboardingStep), nameof(PrayerApp.Models.OnboardingStep.None));
            set => Preferences.Set(nameof(OnboardingStep), value);
        }

        public static int DefaultNotifyHour
        {
            get => Preferences.Get(nameof(DefaultNotifyHour), 9);
            set => Preferences.Set(nameof(DefaultNotifyHour), Math.Clamp(value, 0, 23));
        }

        public static int DefaultNotifyMinute
        {
            get => Preferences.Get(nameof(DefaultNotifyMinute), 0);
            set => Preferences.Set(nameof(DefaultNotifyMinute), Math.Clamp(value, 0, 59));
        }

        public static bool AllowNotifications
        {
            get => Preferences.Get(nameof(AllowNotifications), true);
            set {
                Preferences.Set(nameof(AllowNotifications), value);
                if (_notificationService is null)
                    return;

                UpdateAllowNotificationsAsync(value).SafeFireAndForget();
            }
        }

        private static async Task UpdateAllowNotificationsAsync(bool allowNotifications)
        {
            if (_notificationService is null)
                return;

            if (allowNotifications)
            {
                var granted = await _notificationService.RequestPermissionAsync();
                if (!granted)
                {
                    Preferences.Set(nameof(AllowNotifications), false);
                }
            }
            else
            {
                await _notificationService.ClearAllAsync();
            }
        }

        /// <summary>
        /// Requests the OS notification permission if global notifications are enabled.
        /// Call when a user enables per-item notifications anywhere in the app (e.g. during
        /// onboarding or prayer creation) so the OS dialog isn't deferred until Settings is visited.
        /// </summary>
        public static void EnsureNotificationPermissionRequested()
        {
            if (AllowNotifications && _notificationService != null)
                _notificationService.RequestPermissionAsync().SafeFireAndForget();
        }

        /// <summary>True once the user dismisses the Quick Add tip banner.</summary>
        public static bool QuickAddTipDismissed
        {
            get => Preferences.Get(nameof(QuickAddTipDismissed), false);
            set => Preferences.Set(nameof(QuickAddTipDismissed), value);
        }

        /// <summary>True once the user dismisses the Collections education banner.</summary>
        public static bool CollectionsBannerDismissed
        {
            get => Preferences.Get(nameof(CollectionsBannerDismissed), false);
            set => Preferences.Set(nameof(CollectionsBannerDismissed), value);
        }

        /// <summary>Whether Prayer Time locks to landscape orientation. Default true (landscape).</summary>
        public static bool PrayerTimeLandscape
        {
            get => Preferences.Get(nameof(PrayerTimeLandscape), true);
            set => Preferences.Set(nameof(PrayerTimeLandscape), value);
        }

        /// <summary>CardBox.Id of the Archived folder. Written during DB migration, read at runtime.</summary>
        public static int ArchivedFolderId
        {
            get => Preferences.Get(nameof(ArchivedFolderId), 0);
            set => Preferences.Set(nameof(ArchivedFolderId), value);
        }

        /// <summary>Comma-delimited BoxIds of expanded sections on the Cards page.</summary>
        public static string ExpandedSectionIds
        {
            get => Preferences.Get(nameof(ExpandedSectionIds), string.Empty);
            set => Preferences.Set(nameof(ExpandedSectionIds), value);
        }

        public static void ClearSettings()
        {
            // reset "first run" flag
            Preferences.Clear();
        }
    }
}
