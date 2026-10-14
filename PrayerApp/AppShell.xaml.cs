#if ANDROID
using Android.Content.Res;
using Android.Graphics;
using AndroidX.AppCompat.Widget;
using AndroidX.Core.View;
#endif
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;
using PrayerApp.Views;
using PrayerApp.Views.Onboarding;
using PrayerApp.Views.Prayer;
using PrayerApp.Views.PrayerCard;
using PrayerApp.Views.PrayerTime;
using PrayerApp.Views.Settings;
using PrayerApp.Views.Boxes;
using PrayerApp.Views.Tags;

namespace PrayerApp
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

#if ANDROID
            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", static (handler, view) =>
            {
                if (handler.PlatformView is AppCompatEditText editText)
                {
                    ViewCompat.SetBackgroundTintList(editText, ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
                }
            });

            Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", static (handler, view) =>
            {
                if (handler.PlatformView is AppCompatEditText editText)
                {
                    ViewCompat.SetBackgroundTintList(editText, ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
                }
            });
#endif

            // register explicit routes for child pages
            Routing.RegisterRoute(nameof(PrayerCardPage), typeof(PrayerCardPage));
            Routing.RegisterRoute(nameof(PrayerDetailPage), typeof(PrayerDetailPage));
            Routing.RegisterRoute(nameof(PrayerTimePage), typeof(PrayerTimePage));
            Routing.RegisterRoute(nameof(TagDetailPage), typeof(TagDetailPage));
            Routing.RegisterRoute(nameof(BoxesPage), typeof(BoxesPage));
            Routing.RegisterRoute(nameof(BoxDetailPage), typeof(BoxDetailPage));
            Routing.RegisterRoute(nameof(AppSettingsPage), typeof(AppSettingsPage));
            Routing.RegisterRoute(nameof(BackupPage), typeof(BackupPage));
            Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
            Routing.RegisterRoute(nameof(HelpPage), typeof(HelpPage));
            Routing.RegisterRoute(nameof(ConfirmImportPage), typeof(ConfirmImportPage));

            // Unsaved-changes guard on back navigation
            this.Navigating += OnShellNavigating;

            // Subtle crossfade when switching tabs
            this.Navigated += OnShellNavigated;

            // Subscribe to onboarding step changes to show the closing popup
            var onboardingService = IPlatformApplication.Current!.Services
                .GetRequiredService<IOnboardingService>();

            onboardingService.StepChanged += (_, _) =>
            {
                if (onboardingService.CurrentStep != OnboardingStep.Complete) return;

                // If the user skipped onboarding while editing (IEditGuard.IsDirty),
                // silently complete — don't interrupt with a popup that may disrupt
                // the navigation stack and lose their unsaved work.
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (CurrentPage?.BindingContext is IEditGuard { IsDirty: true })
                    {
                        Settings.OnboardingComplete = true;
                        return;
                    }
                    ShowOnboardingCompletePopupAsync().SafeFireAndForget();
                });
            };
        }

        private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs args)
        {
            // iOS swipe-back gesture may fire as Unknown — include it so the
            // unsaved-changes guard isn't bypassed on iOS back navigation.
            // PopToRoot fires from the Android CustomShellRenderer when the
            // active tab is tapped — guard it too so a dirty detail page
            // doesn't silently discard on tab reselection.
            if (args.Source is not (ShellNavigationSource.Pop
                or ShellNavigationSource.PopToRoot
                or ShellNavigationSource.ShellItemChanged
                or ShellNavigationSource.ShellSectionChanged
                or ShellNavigationSource.Unknown))
                return;

            if (CurrentPage?.BindingContext is IEditGuard guard && guard.IsDirty)
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (!await guard.CanLeaveAsync())
                        args.Cancel();
                }
                finally
                {
                    deferral.Complete();
                }
            }
        }

        private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
        {
            // Only animate Push (new pages). Pop and tab-switch pages already
            // have visible content; hiding them to fade back in causes a flash
            // when OnAppearing triggers a data refresh.
            if (e.Source != ShellNavigationSource.Push) return;

            var page = CurrentPage;
            if (page is null) return;

            page.TranslationX = 40;
            await page.TranslateToAsync(0, 0, 220, Easing.CubicOut);
        }

        private static async Task ShowOnboardingCompletePopupAsync()
        {
            var page = Shell.Current?.CurrentPage;
            if (page is not null)
                await page.ShowPopupAsync(new OnboardingCompletePopup(),
                    new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false },
                    CancellationToken.None);
        }
    }
}
