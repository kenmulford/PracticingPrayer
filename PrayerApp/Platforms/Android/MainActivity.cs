using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui.Devices;
using PrayerApp.Services;

namespace PrayerApp
{
    // Manifest value applies before MAUI creates the window; at runtime App.xaml Application.WindowSoftInputModeAdjust is the source of truth.
    [Activity(Theme ="@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
        WindowSoftInputMode = SoftInput.AdjustResize,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    // Deep link handler: https://practicingprayerapp.com/share/*
    [IntentFilter(
        new[] { Android.Content.Intent.ActionView },
        Categories = new[] {
            Android.Content.Intent.CategoryDefault,
            Android.Content.Intent.CategoryBrowsable
        },
        DataScheme = "https",
        DataHost = "practicingprayerapp.com",
        DataPathPrefix = "/share",
        AutoVerify = true)]
    // File handler: .prayercard files shared via messaging apps
    [IntentFilter(
        new[] { Android.Content.Intent.ActionView },
        Categories = new[] {
            Android.Content.Intent.CategoryDefault,
            Android.Content.Intent.CategoryBrowsable
        },
        DataScheme = "content",
        DataMimeType = "application/x-prayercard")]
    // Selection-toolbar handler: text selected in any app -> "Practicing Prayer" action
    [IntentFilter(
        new[] { Android.Content.Intent.ActionProcessText },
        Categories = new[] { Android.Content.Intent.CategoryDefault },
        DataMimeType = "text/plain")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Before base.OnCreate so MAUI builds its first window and page already portrait on phones.
            RequestedOrientation = OrientationPolicy.AllowsRotation(DeviceInfo.Current.Idiom)
                ? ScreenOrientation.Unspecified
                : ScreenOrientation.Portrait;
            base.OnCreate(savedInstanceState);
        }
    }
}
