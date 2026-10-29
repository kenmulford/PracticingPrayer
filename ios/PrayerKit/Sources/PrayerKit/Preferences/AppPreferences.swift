import Foundation

/// Typed reads and writes of the MAUI app's preference keys, so a 1.x to 2.0 upgrade keeps the user's settings.
public struct AppPreferences {
    // Raw values are the MAUI property names; they are the on-disk keys and must not change.
    private enum Key: String {
        case autoModeIntervalSeconds = "AutoModeIntervalSeconds"
        case overdueDayThreshold = "OverdueDayThreshold"
        case onboardingComplete = "OnboardingComplete"
        case onboardingStep = "OnboardingStep"
        case defaultNotifyHour = "DefaultNotifyHour"
        case defaultNotifyMinute = "DefaultNotifyMinute"
        case allowNotifications = "AllowNotifications"
        case collectionsBannerDismissed = "CollectionsBannerDismissed"
        case prayerTimeLandscape = "PrayerTimeLandscape"
        case archivedFolderId = "ArchivedFolderId"
        case expandedSectionIds = "ExpandedSectionIds"
        case shareHandoffPrompted = "ShareHandoffPrompted"
    }

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    // Defaults apply only when the key is unset (not via register(defaults:), which every UserDefaults instance shares).
    private func int(_ key: Key, _ fallback: Int) -> Int {
        defaults.object(forKey: key.rawValue) == nil ? fallback : defaults.integer(forKey: key.rawValue)
    }

    private func bool(_ key: Key, _ fallback: Bool) -> Bool {
        defaults.object(forKey: key.rawValue) == nil ? fallback : defaults.bool(forKey: key.rawValue)
    }

    private func string(_ key: Key, _ fallback: String) -> String {
        defaults.string(forKey: key.rawValue) ?? fallback
    }

    public var autoModeIntervalSeconds: Int {
        get { int(.autoModeIntervalSeconds, 30) }
        nonmutating set { defaults.set(newValue, forKey: Key.autoModeIntervalSeconds.rawValue) }
    }

    public var overdueDayThreshold: Int {
        get { int(.overdueDayThreshold, 30) }
        nonmutating set { defaults.set(max(1, newValue), forKey: Key.overdueDayThreshold.rawValue) }
    }

    public var onboardingComplete: Bool {
        get { bool(.onboardingComplete, false) }
        nonmutating set { defaults.set(newValue, forKey: Key.onboardingComplete.rawValue) }
    }

    public var onboardingStep: String {
        get { string(.onboardingStep, "None") }
        nonmutating set { defaults.set(newValue, forKey: Key.onboardingStep.rawValue) }
    }

    public var defaultNotifyHour: Int {
        get { int(.defaultNotifyHour, 9) }
        nonmutating set { defaults.set(min(max(newValue, 0), 23), forKey: Key.defaultNotifyHour.rawValue) }
    }

    public var defaultNotifyMinute: Int {
        get { int(.defaultNotifyMinute, 0) }
        nonmutating set { defaults.set(min(max(newValue, 0), 59), forKey: Key.defaultNotifyMinute.rawValue) }
    }

    public var allowNotifications: Bool {
        get { bool(.allowNotifications, true) }
        nonmutating set { defaults.set(newValue, forKey: Key.allowNotifications.rawValue) }
    }

    public var collectionsBannerDismissed: Bool {
        get { bool(.collectionsBannerDismissed, false) }
        nonmutating set { defaults.set(newValue, forKey: Key.collectionsBannerDismissed.rawValue) }
    }

    public var prayerTimeLandscape: Bool {
        get { bool(.prayerTimeLandscape, true) }
        nonmutating set { defaults.set(newValue, forKey: Key.prayerTimeLandscape.rawValue) }
    }

    public var archivedFolderId: Int {
        get { int(.archivedFolderId, 0) }
        nonmutating set { defaults.set(newValue, forKey: Key.archivedFolderId.rawValue) }
    }

    public var expandedSectionIds: String {
        get { string(.expandedSectionIds, "") }
        nonmutating set { defaults.set(newValue, forKey: Key.expandedSectionIds.rawValue) }
    }

    public var shareHandoffPrompted: Bool {
        get { bool(.shareHandoffPrompted, false) }
        nonmutating set { defaults.set(newValue, forKey: Key.shareHandoffPrompted.rawValue) }
    }
}
