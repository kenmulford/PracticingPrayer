using Microsoft.Maui.Devices;
using PrayerApp.Services;
using UIKit;

namespace PrayerApp.Platforms.iOS;

public class OrientationService : IOrientationService
{
    public static UIInterfaceOrientationMask AllowedOrientations { get; private set; }
        = OrientationPolicy.AllowsRotation(DeviceInfo.Current.Idiom)
            ? UIInterfaceOrientationMask.AllButUpsideDown
            : UIInterfaceOrientationMask.Portrait;

    public void LockLandscape()
    {
        AllowedOrientations = UIInterfaceOrientationMask.LandscapeRight;
        RequestUpdate(UIInterfaceOrientationMask.LandscapeRight);
    }

    public void LockPortrait()
    {
        AllowedOrientations = UIInterfaceOrientationMask.Portrait;
        RequestUpdate(UIInterfaceOrientationMask.Portrait);
    }

    public void Unlock()
    {
        AllowedOrientations = UIInterfaceOrientationMask.AllButUpsideDown;
        RequestUpdate(UIInterfaceOrientationMask.AllButUpsideDown);
    }

    public void ApplyDefault()
    {
        if (OrientationPolicy.AllowsRotation(DeviceInfo.Current.Idiom))
            Unlock();
        else
            LockPortrait();
    }

    private static void RequestUpdate(UIInterfaceOrientationMask mask)
    {
        var geometryPreferences = new UIWindowSceneGeometryPreferencesIOS(mask);
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene) continue;
            // UIKit re-reads supportedInterfaceOrientations only after this call (iOS 16+); without it
            // Unlock() leaves a forced-landscape iPad sideways because its mask still contains landscape.
            foreach (var window in windowScene.Windows)
                window.RootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();
            windowScene.RequestGeometryUpdate(geometryPreferences, _ => { });
        }
    }
}
