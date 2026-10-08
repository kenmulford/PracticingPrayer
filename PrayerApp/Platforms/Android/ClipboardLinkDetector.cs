using Android.Content;
using Android.Views;
using PrayerApp.Services;

namespace PrayerApp.Platforms.Android;

/// <summary>
/// Reads only the clip's description, never its items: Android 12+ shows the "pasted from"
/// toast on <c>getPrimaryClip()</c>, which the share-import flow defers until the user taps
/// Import. Android returns no clip data to an app without input focus, so the check waits
/// for window focus first.
/// </summary>
public class ClipboardLinkDetector : IClipboardLinkDetector
{
    public async Task<bool> HasProbableLinkAsync()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var observer = activity?.Window?.DecorView.ViewTreeObserver;
        if (activity is null || observer is null)
            return false;

        if (!activity.HasWindowFocus)
            await WaitForWindowFocusAsync(observer);

        var clipboard = activity.GetSystemService(Context.ClipboardService) as ClipboardManager;
        return clipboard is { HasPrimaryClip: true }
            && clipboard.PrimaryClipDescription?.HasMimeType(ClipDescription.MimetypeTextPlain) == true;
    }

    private static Task WaitForWindowFocusAsync(ViewTreeObserver observer)
    {
        var focused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ViewTreeObserver.WindowFocusChangeEventArgs>? onFocusChange = null;
        onFocusChange = (_, e) =>
        {
            if (!e.HasFocus)
                return;
            observer.WindowFocusChange -= onFocusChange;
            focused.TrySetResult();
        };
        observer.WindowFocusChange += onFocusChange;
        return focused.Task;
    }
}
