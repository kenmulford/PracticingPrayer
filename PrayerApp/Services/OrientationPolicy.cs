using Microsoft.Maui.Devices;

namespace PrayerApp.Services;

public static class OrientationPolicy
{
    public static bool AllowsRotation(DeviceIdiom idiom) => idiom == DeviceIdiom.Tablet;
}
