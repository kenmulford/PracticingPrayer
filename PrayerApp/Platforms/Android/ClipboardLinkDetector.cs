using Android.App;
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
            await WaitForWindowFocusAsync(activity, observer);

        var clipboard = activity.GetSystemService(Context.ClipboardService) as ClipboardManager;
        return clipboard is { HasPrimaryClip: true }
            && clipboard.PrimaryClipDescription?.HasMimeType(ClipDescription.MimetypeTextPlain) == true;
    }

    // The clipboard is readable only by the focused window on Android 10+.
    private static Task WaitForWindowFocusAsync(Activity activity, ViewTreeObserver observer)
    {
        var focused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ViewTreeObserver.WindowFocusChangeEventArgs>? onFocusChange = null;
        onFocusChange = (_, e) =>
        {
            if (!e.HasFocus)
                return;
            // The captured observer can be the floating pre-attach one, so unsubscribe from
            // the window's live one. Removing from a dead observer throws IllegalStateException.
            var live = activity.Window?.DecorView?.ViewTreeObserver;
            if (live is { IsAlive: true })
                live.WindowFocusChange -= onFocusChange;
            focused.TrySetResult();
        };
        observer.WindowFocusChange += onFocusChange;
        return focused.Task;
    }
}
