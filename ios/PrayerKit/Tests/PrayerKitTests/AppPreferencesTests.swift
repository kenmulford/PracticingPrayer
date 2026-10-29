import Foundation
import Testing
@testable import PrayerKit

@Suite struct AppPreferencesTests {
    private func withStore(_ body: (UserDefaults, AppPreferences) -> Void) {
        let name = "AppPreferencesTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        defer { defaults.removePersistentDomain(forName: name) }
        body(defaults, AppPreferences(defaults: defaults))
    }

    @Test func emptyStoreReturnsMauiDefaults() {
        withStore { _, p in
            #expect(p.autoModeIntervalSeconds == 30)
            #expect(p.overdueDayThreshold == 30)
            #expect(p.onboardingComplete == false)
            #expect(p.onboardingStep == "None")
            #expect(p.defaultNotifyHour == 9)
            #expect(p.defaultNotifyMinute == 0)
            #expect(p.allowNotifications == true)
            #expect(p.collectionsBannerDismissed == false)
            #expect(p.prayerTimeLandscape == true)
            #expect(p.archivedFolderId == 0)
            #expect(p.expandedSectionIds == "")
            #expect(p.shareHandoffPrompted == false)
        }
    }

    @Test func writtenValuesRoundTrip() {
        withStore { _, p in
            p.autoModeIntervalSeconds = 45
            p.overdueDayThreshold = 14
            p.onboardingComplete = true
            p.onboardingStep = "Done"
            p.defaultNotifyHour = 7
            p.defaultNotifyMinute = 15
            p.allowNotifications = false
            p.collectionsBannerDismissed = true
            p.prayerTimeLandscape = false
            p.archivedFolderId = 12
            p.expandedSectionIds = "1,2"
            p.shareHandoffPrompted = true
            #expect(p.autoModeIntervalSeconds == 45)
            #expect(p.overdueDayThreshold == 14)
            #expect(p.onboardingComplete == true)
            #expect(p.onboardingStep == "Done")
            #expect(p.defaultNotifyHour == 7)
            #expect(p.defaultNotifyMinute == 15)
            #expect(p.allowNotifications == false)
            #expect(p.collectionsBannerDismissed == true)
            #expect(p.prayerTimeLandscape == false)
            #expect(p.archivedFolderId == 12)
            #expect(p.expandedSectionIds == "1,2")
            #expect(p.shareHandoffPrompted == true)
        }
    }

    @Test func mauiSeededValuesRead() {
        withStore { d, p in
            d.set(60, forKey: "AutoModeIntervalSeconds")
            d.set(5, forKey: "OverdueDayThreshold")
            d.set(true, forKey: "OnboardingComplete")
            d.set("Welcome", forKey: "OnboardingStep")
            d.set(20, forKey: "DefaultNotifyHour")
            d.set(30, forKey: "DefaultNotifyMinute")
            d.set(false, forKey: "AllowNotifications")
            d.set(true, forKey: "CollectionsBannerDismissed")
            d.set(false, forKey: "PrayerTimeLandscape")
            d.set(3, forKey: "ArchivedFolderId")
            d.set("4,5", forKey: "ExpandedSectionIds")
            d.set(true, forKey: "ShareHandoffPrompted")
            #expect(p.autoModeIntervalSeconds == 60)
            #expect(p.overdueDayThreshold == 5)
            #expect(p.onboardingComplete == true)
            #expect(p.onboardingStep == "Welcome")
            #expect(p.defaultNotifyHour == 20)
            #expect(p.defaultNotifyMinute == 30)
            #expect(p.allowNotifications == false)
            #expect(p.collectionsBannerDismissed == true)
            #expect(p.prayerTimeLandscape == false)
            #expect(p.archivedFolderId == 3)
            #expect(p.expandedSectionIds == "4,5")
            #expect(p.shareHandoffPrompted == true)
        }
    }

    @Test func storedFalseAndZeroAreNotReplacedByDefaults() {
        withStore { d, p in
            d.set(false, forKey: "AllowNotifications")
            d.set(false, forKey: "PrayerTimeLandscape")
            d.set(0, forKey: "AutoModeIntervalSeconds")
            d.set(0, forKey: "DefaultNotifyHour")
            d.set(0, forKey: "OverdueDayThreshold")
            #expect(p.allowNotifications == false)
            #expect(p.prayerTimeLandscape == false)
            #expect(p.autoModeIntervalSeconds == 0)
            #expect(p.defaultNotifyHour == 0)
            #expect(p.overdueDayThreshold == 0)
        }
    }

    @Test func overdueDayThresholdClampsToAtLeastOne() {
        withStore { _, p in
            p.overdueDayThreshold = 0
            #expect(p.overdueDayThreshold == 1)
            p.overdueDayThreshold = -5
            #expect(p.overdueDayThreshold == 1)
        }
    }

    @Test func notifyHourClamps() {
        withStore { _, p in
            p.defaultNotifyHour = -1
            #expect(p.defaultNotifyHour == 0)
            p.defaultNotifyHour = 24
            #expect(p.defaultNotifyHour == 23)
        }
    }

    @Test func notifyMinuteClamps() {
        withStore { _, p in
            p.defaultNotifyMinute = -1
            #expect(p.defaultNotifyMinute == 0)
            p.defaultNotifyMinute = 60
            #expect(p.defaultNotifyMinute == 59)
        }
    }

    @Test func droppedKeysAreNeverWritten() {
        withStore { d, p in
            p.autoModeIntervalSeconds = 1
            p.overdueDayThreshold = 1
            p.onboardingComplete = true
            p.onboardingStep = "x"
            p.defaultNotifyHour = 1
            p.defaultNotifyMinute = 1
            p.allowNotifications = true
            p.collectionsBannerDismissed = true
            p.prayerTimeLandscape = true
            p.archivedFolderId = 1
            p.expandedSectionIds = "x"
            p.shareHandoffPrompted = true
            #expect(d.object(forKey: "FirstRun") == nil)
            #expect(d.object(forKey: "QuickAddTipDismissed") == nil)
        }
    }
}
