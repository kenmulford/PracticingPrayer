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
        observer.AddOnWindowFocusChangeListener(new FocusListener(activity, observer, focused));
        return focused.Task;
    }

    // An explicit listener object, unlike an event handler, is the same instance to Add and
    // Remove on whichever observer ends up holding it.
    private sealed class FocusListener(Activity activity, ViewTreeObserver observer, TaskCompletionSource focused)
        : Java.Lang.Object, ViewTreeObserver.IOnWindowFocusChangeListener
    {
        public void OnWindowFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
                return;
            // The captured observer can be the floating pre-attach one, so remove from the
            // window's live one too. Removing from a dead observer throws IllegalStateException.
            if (observer.IsAlive)
                observer.RemoveOnWindowFocusChangeListener(this);
            var live = activity.Window?.DecorView?.ViewTreeObserver;
            if (live is { IsAlive: true } && !ReferenceEquals(live, observer))
                live.RemoveOnWindowFocusChangeListener(this);
            focused.TrySetResult();
        }
    }
}
