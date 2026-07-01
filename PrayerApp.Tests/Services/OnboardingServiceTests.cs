using PrayerApp.Models;
using PrayerApp.Services;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Unit coverage for the onboarding welcome-gate decision logic, converted from the
/// fragile E2E <c>Onboarding_WelcomePopup_ShowsOnFirstLaunch</c> (issue #190). That E2E
/// forced "first launch" via <c>defaults write/delete</c>, but the iOS simulator's
/// cfprefsd caches the app's prefs across relaunches, so the writes never reached the app.
/// The welcome gate is deterministic decision logic, so a unit test covers it reliably.
///
/// The "data" under test is the mapping from persisted (OnboardingStep, OnboardingComplete)
/// to the resulting <see cref="IOnboardingService.CurrentStep"/> / <see cref="IOnboardingService.IsActive"/>,
/// plus the Advance/Skip/Reset transitions. <see cref="OnboardingService"/> reads/writes its
/// state through the injected <see cref="ISettings"/> abstraction (the same seam ViewModels use),
/// so an in-memory <see cref="FakeSettings"/> stands in for MAUI Preferences here.
/// </summary>
public class OnboardingServiceTests
{
    // ── helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the welcome-gate condition in MainPage.xaml.cs OnAppearing (~line 105):
    /// show the popup only on the Welcome step, when it has not been shown this session
    /// and the session was not entered via a share deep-link.
    /// </summary>
    private static bool WelcomeGateWouldShow(IOnboardingService s) =>
        s.CurrentStep == OnboardingStep.Welcome
        && !s.WelcomeShownThisSession
        && !s.IsDeepLinkSession;

    private static OnboardingService NewService(string persistedStep, bool onboardingComplete) =>
        new(new FakeSettings { OnboardingStep = persistedStep, OnboardingComplete = onboardingComplete });

    // ── ctor: persisted state → resolved step (the welcome-gate decision) ──

    [Fact]
    public void Ctor_FirstInstall_NoStepAndNotComplete_StartsAtWelcome()
    {
        var sut = NewService(nameof(OnboardingStep.None), onboardingComplete: false);

        Assert.Equal(OnboardingStep.Welcome, sut.CurrentStep);
        Assert.True(sut.IsActive);
        Assert.True(WelcomeGateWouldShow(sut));
    }

    [Fact]
    public void Ctor_OnboardingComplete_StepNone_DoesNotStartAtWelcome()
    {
        var sut = NewService(nameof(OnboardingStep.None), onboardingComplete: true);

        Assert.Equal(OnboardingStep.None, sut.CurrentStep);
        Assert.False(sut.IsActive);
        Assert.False(WelcomeGateWouldShow(sut));
    }

    [Fact]
    public void Ctor_PersistedComplete_NotFlaggedComplete_StaysCompleteNotWelcome()
    {
        // A returning user whose persisted step is Complete must not be re-shown Welcome,
        // even if the legacy OnboardingComplete flag was never written.
        var sut = NewService(nameof(OnboardingStep.Complete), onboardingComplete: false);

        Assert.Equal(OnboardingStep.Complete, sut.CurrentStep);
        Assert.False(sut.IsActive);
        Assert.False(WelcomeGateWouldShow(sut));
    }

    [Fact]
    public void Ctor_PersistedMidStep_RestoresThatStep()
    {
        var sut = NewService(nameof(OnboardingStep.AddRequest), onboardingComplete: false);

        Assert.Equal(OnboardingStep.AddRequest, sut.CurrentStep);
        Assert.True(sut.IsActive);
        Assert.False(WelcomeGateWouldShow(sut)); // mid-flow is not the Welcome step
    }

    [Theory]
    [InlineData(OnboardingStep.PrayerTime)]
    [InlineData(OnboardingStep.PrayerTimeActive)]
    [InlineData(OnboardingStep.ShareIntro)]
    [InlineData(OnboardingStep.SharePrayer)]
    public void Ctor_LegacyStep_MigratesToPrayerTimeHighlight(OnboardingStep legacy)
    {
        var sut = NewService(legacy.ToString(), onboardingComplete: false);

        Assert.Equal(OnboardingStep.PrayerTimeHighlight, sut.CurrentStep);
        Assert.True(sut.IsActive);
    }

    [Fact]
    public void Ctor_UnparsablePersistedValue_AndNotComplete_FallsBackToWelcome()
    {
        // Defensive: a corrupt/unknown persisted value parses as None, so first-install
        // logic applies and the user lands on Welcome rather than a dead step.
        var sut = NewService("not-a-real-step", onboardingComplete: false);

        Assert.Equal(OnboardingStep.Welcome, sut.CurrentStep);
        Assert.True(sut.IsActive);
    }

    // ── welcome-gate suppression inputs ──────────────────────────

    [Fact]
    public void WelcomeGate_AfterMarkWelcomeShown_IsSuppressed()
    {
        var sut = NewService(nameof(OnboardingStep.None), onboardingComplete: false);
        Assert.True(WelcomeGateWouldShow(sut));

        sut.MarkWelcomeShown();

        Assert.True(sut.WelcomeShownThisSession);
        Assert.False(WelcomeGateWouldShow(sut)); // not re-shown on a later OnAppearing
    }

    [Fact]
    public void WelcomeGate_DeepLinkSession_IsSuppressed()
    {
        var sut = NewService(nameof(OnboardingStep.None), onboardingComplete: false);
        Assert.True(WelcomeGateWouldShow(sut));

        sut.MarkDeepLinkSession();

        Assert.True(sut.IsDeepLinkSession);
        Assert.False(WelcomeGateWouldShow(sut)); // share-link entry skips onboarding
    }

    // ── Advance ──────────────────────────────────────────────────

    [Fact]
    public void Advance_FromWelcome_GoesToCreateCard_AndPersists()
    {
        var settings = new FakeSettings { OnboardingStep = nameof(OnboardingStep.None), OnboardingComplete = false };
        var sut = new OnboardingService(settings);
        var raised = 0;
        sut.StepChanged += (_, _) => raised++;

        sut.Advance();

        Assert.Equal(OnboardingStep.CreateCard, sut.CurrentStep);
        Assert.Equal(nameof(OnboardingStep.CreateCard), settings.OnboardingStep); // persisted
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Advance_AtLastStep_GoesToComplete_AndDeactivates()
    {
        var sut = NewService(nameof(OnboardingStep.PrayerTimeHighlight), onboardingComplete: false);

        sut.Advance();

        Assert.Equal(OnboardingStep.Complete, sut.CurrentStep);
        Assert.False(sut.IsActive);
    }

    [Fact]
    public void Advance_WhenComplete_IsNoOp()
    {
        var settings = new FakeSettings { OnboardingStep = nameof(OnboardingStep.Complete), OnboardingComplete = false };
        var sut = new OnboardingService(settings);
        var raised = 0;
        sut.StepChanged += (_, _) => raised++;

        sut.Advance();

        Assert.Equal(OnboardingStep.Complete, sut.CurrentStep);
        Assert.Equal(0, raised);
    }

    // ── Skip ─────────────────────────────────────────────────────

    [Fact]
    public void Skip_FromWelcome_SetsComplete_AndPersists()
    {
        var settings = new FakeSettings { OnboardingStep = nameof(OnboardingStep.None), OnboardingComplete = false };
        var sut = new OnboardingService(settings);
        var raised = 0;
        sut.StepChanged += (_, _) => raised++;

        sut.Skip();

        Assert.Equal(OnboardingStep.Complete, sut.CurrentStep);
        Assert.False(sut.IsActive);
        Assert.Equal(nameof(OnboardingStep.Complete), settings.OnboardingStep); // persisted
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Skip_WhenAlreadyComplete_IsNoOp()
    {
        var sut = NewService(nameof(OnboardingStep.Complete), onboardingComplete: false);
        var raised = 0;
        sut.StepChanged += (_, _) => raised++;

        sut.Skip();

        Assert.Equal(OnboardingStep.Complete, sut.CurrentStep);
        Assert.Equal(0, raised);
    }

    // ── Reset ────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsPersistedStepAndCompleteFlag_AndReturnsToWelcome()
    {
        // Start from a fully-completed install.
        var settings = new FakeSettings { OnboardingStep = nameof(OnboardingStep.Complete), OnboardingComplete = true };
        var sut = new OnboardingService(settings);
        sut.MarkWelcomeShown();
        sut.MarkDeepLinkSession();

        sut.Reset();

        Assert.Equal(OnboardingStep.Welcome, sut.CurrentStep);
        Assert.True(sut.IsActive);
        Assert.Equal(nameof(OnboardingStep.None), settings.OnboardingStep); // persisted step cleared
        Assert.False(settings.OnboardingComplete);                          // flag cleared
        Assert.False(sut.WelcomeShownThisSession);
        Assert.False(sut.IsDeepLinkSession);
        Assert.True(WelcomeGateWouldShow(sut)); // a reset re-arms the welcome popup
    }

    /// <summary>
    /// In-memory <see cref="ISettings"/> double that actually stores round-trips, so
    /// Advance/Skip/Reset persistence is observable. Non-onboarding members are present
    /// only to satisfy the interface and are unused here.
    /// </summary>
    private sealed class FakeSettings : ISettings
    {
        public bool FirstRun { get; set; }
        public int AutoModeIntervalSeconds { get; set; }
        public int OverdueDayThreshold { get; set; }
        public bool OnboardingComplete { get; set; }
        public int DefaultNotifyHour { get; set; }
        public int DefaultNotifyMinute { get; set; }
        public bool AllowNotifications { get; set; }
        public bool QuickAddTipDismissed { get; set; }
        public bool PrayerTimeLandscape { get; set; }
        public bool CollectionsBannerDismissed { get; set; }
        public int ArchivedFolderId { get; set; }
        public string ExpandedSectionIds { get; set; } = string.Empty;
        public string OnboardingStep { get; set; } = nameof(Models.OnboardingStep.None);
    }
}
