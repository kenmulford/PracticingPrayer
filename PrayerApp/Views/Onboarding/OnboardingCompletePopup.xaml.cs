// PrayerApp/Views/Onboarding/OnboardingCompletePopup.xaml.cs
using CommunityToolkit.Maui.Views;
using PrayerApp.Helpers;
using PrayerApp.Services;

namespace PrayerApp.Views.Onboarding;

public partial class OnboardingCompletePopup : Popup
{
    public OnboardingCompletePopup()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            BtnDone.SetSemanticFocus();
        };
        BtnDone.Clicked += async (_, _) =>
        {
            PrayerApp.Services.Settings.OnboardingComplete = true;
            await this.TryCloseAsync();
        };
    }
}
