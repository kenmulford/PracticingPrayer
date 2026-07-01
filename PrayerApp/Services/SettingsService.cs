namespace PrayerApp.Services;

/// <summary>
/// Injectable wrapper around the static Settings class.
/// Delegates to Settings so notification side-effects are preserved.
/// </summary>
public class SettingsService : ISettings
{
    public bool FirstRun
    {
        get => Settings.FirstRun;
        set => Settings.FirstRun = value;
    }

    public int AutoModeIntervalSeconds
    {
        get => Settings.AutoModeIntervalSeconds;
        set => Settings.AutoModeIntervalSeconds = value;
    }

    public int OverdueDayThreshold
    {
        get => Settings.OverdueDayThreshold;
        set => Settings.OverdueDayThreshold = value;
    }

    public bool OnboardingComplete
    {
        get => Settings.OnboardingComplete;
        set => Settings.OnboardingComplete = value;
    }

    public string OnboardingStep
    {
        get => Settings.OnboardingStep;
        set => Settings.OnboardingStep = value;
    }

    public int DefaultNotifyHour
    {
        get => Settings.DefaultNotifyHour;
        set => Settings.DefaultNotifyHour = value;
    }

    public int DefaultNotifyMinute
    {
        get => Settings.DefaultNotifyMinute;
        set => Settings.DefaultNotifyMinute = value;
    }

    public bool AllowNotifications
    {
        get => Settings.AllowNotifications;
        set => Settings.AllowNotifications = value;
    }

    public bool QuickAddTipDismissed
    {
        get => Settings.QuickAddTipDismissed;
        set => Settings.QuickAddTipDismissed = value;
    }

    public bool CollectionsBannerDismissed
    {
        get => Settings.CollectionsBannerDismissed;
        set => Settings.CollectionsBannerDismissed = value;
    }

    public bool PrayerTimeLandscape
    {
        get => Settings.PrayerTimeLandscape;
        set => Settings.PrayerTimeLandscape = value;
    }

    public int ArchivedFolderId
    {
        get => Settings.ArchivedFolderId;
        set => Settings.ArchivedFolderId = value;
    }

    public string ExpandedSectionIds
    {
        get => Settings.ExpandedSectionIds;
        set => Settings.ExpandedSectionIds = value;
    }
}
