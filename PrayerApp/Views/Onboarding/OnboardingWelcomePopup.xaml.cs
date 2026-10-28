// PrayerApp/Views/Onboarding/OnboardingWelcomePopup.xaml.cs
using CommunityToolkit.Maui.Views;
using PrayerApp.Helpers;
using PrayerApp.Services;

namespace PrayerApp.Views.Onboarding;

public partial class OnboardingWelcomePopup : Popup
{
    private readonly IOnboardingService _onboardingService;

    public OnboardingWelcomePopup(IOnboardingService onboardingService)
    {
        InitializeComponent();
        _onboardingService = onboardingService;

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            BtnGetStarted.SetSemanticFocus();
        };

        BtnGetStarted.Clicked += async (_, _) =>
        {
            await this.TryCloseAsync();
            _onboardingService.Advance(); // Welcome → CreateCard
            await Shell.Current.GoToAsync("//CardsPage");
        };

        SkipButton.Clicked += async (_, _) =>
        {
            await this.TryCloseAsync();
            _onboardingService.Skip();
        };
    }
}
