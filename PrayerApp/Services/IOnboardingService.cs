using PrayerApp.Models;

namespace PrayerApp.Services;

public interface IOnboardingService
{
    OnboardingStep CurrentStep { get; }
    /// <summary>True when step is Welcome through PrayerTimeHighlight inclusive; false for None and Complete.</summary>
    bool IsActive { get; }
    /// <summary>In-memory only. True when user entered via share link — suppresses onboarding this session.</summary>
    bool IsDeepLinkSession { get; }
    /// <summary>In-memory only. Prevents welcome popup re-showing on each OnAppearing.</summary>
    bool WelcomeShownThisSession { get; }
    void Advance();
    void Skip();
    /// <summary>Clears persisted step key, sets OnboardingComplete=false, resets WelcomeShownThisSession, sets CurrentStep=Welcome in memory.</summary>
    void Reset();
    /// <summary>Called by MainPage on entering the first-visit welcome gate, before the share handoff and the welcome popup. Sets WelcomeShownThisSession=true.</summary>
    void MarkWelcomeShown();
    /// <summary>Called by MauiProgram.HandleDeepLink before UI dispatch. Suppresses onboarding for this session.</summary>
    void MarkDeepLinkSession();
    event EventHandler StepChanged;
}
