using Android.Content.PM;
using Microsoft.Maui.Devices;
using PrayerApp.Services;

namespace PrayerApp.Platforms.Android;

public class OrientationService : IOrientationService
{
    public void LockLandscape()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
            activity.RequestedOrientation = ScreenOrientation.Landscape;
    }

    public void LockPortrait()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
            activity.RequestedOrientation = ScreenOrientation.Portrait;
    }

    public void Unlock()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
            activity.RequestedOrientation = ScreenOrientation.Unspecified;
    }

    public void ApplyDefault()
    {
        if (OrientationPolicy.AllowsRotation(DeviceInfo.Current.Idiom))
            Unlock();
        else
            LockPortrait();
    }
}
