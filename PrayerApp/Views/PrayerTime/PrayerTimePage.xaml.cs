using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Views.PrayerTime;

public partial class PrayerTimePage : ContentPage
{
    private readonly IOrientationService _orientationService;

    public PrayerTimePage(PrayerTimeViewModel vm, IOrientationService orientationService)
    {
        InitializeComponent();
        BindingContext = vm;
        _orientationService = orientationService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (PrayerApp.Services.Settings.PrayerTimeLandscape)
            _orientationService.LockLandscape();

        // Pause auto-mode when the window is backgrounded; resume when it returns
        if (Window is not null)
        {
            Window.Stopped  += OnWindowStopped;
            Window.Resumed  += OnWindowResumed;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _orientationService.ApplyDefault();

        // Unsubscribe window lifecycle events
        if (Window is not null)
        {
            Window.Stopped  -= OnWindowStopped;
            Window.Resumed  -= OnWindowResumed;
        }

        // Stop auto-mode fully when leaving the page
        if (BindingContext is PrayerTimeViewModel vm)
            vm.StopAutoMode();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        if (BindingContext is PrayerTimeViewModel vm)
            vm.PauseAutoMode();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        if (BindingContext is PrayerTimeViewModel vm)
            vm.ResumeAutoMode();
    }
}
