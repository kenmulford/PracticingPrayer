namespace PrayerApp.Services;

/// <summary>
/// Abstraction over app preferences for testability.
/// ViewModels inject this instead of referencing the static Settings class.
/// </summary>
public interface ISettings
{
    bool FirstRun { get; set; }
    int AutoModeIntervalSeconds { get; set; }
    int OverdueDayThreshold { get; set; }
    bool OnboardingComplete { get; set; }

    /// <summary>
    /// Persisted onboarding step, stored as the <c>OnboardingStep</c> enum's string name
    /// (e.g. "None", "Welcome", "Complete"). "None" is the default for new installs.
    /// OnboardingService owns the parse/format and legacy-step migration.
    /// </summary>
    string OnboardingStep { get; set; }
    int DefaultNotifyHour { get; set; }
    int DefaultNotifyMinute { get; set; }
    bool AllowNotifications { get; set; }
    bool QuickAddTipDismissed { get; set; }
    bool PrayerTimeLandscape { get; set; }
    bool CollectionsBannerDismissed { get; set; }

    /// <summary>
    /// The CardBox.Id of the Archived folder. Written once during DB migration.
    /// All "is archived?" checks use card.BoxId == ArchivedFolderId.
    /// </summary>
    int ArchivedFolderId { get; set; }

    /// <summary>
    /// Comma-delimited BoxIds of expanded sections on the Cards page.
    /// Empty string = all collapsed (the default for new installs).
    /// </summary>
    string ExpandedSectionIds { get; set; }
}
