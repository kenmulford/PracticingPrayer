using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Views;
#if IOS
using Foundation;
using UIKit;
#endif

namespace PrayerApp.Behaviors;

/// <summary>
/// Lifts a popup through <c>Popup.Margin</c> by the keyboard's overlap with the
/// window, so a centred popup re-centres in the area above it. iOS-only: a
/// CommunityToolkit popup is centred on a full-screen page, so the platform's
/// <c>KeyboardAutoManagerScroll</c> only nudges the focused Entry and never
/// re-lays out the popup. Android resizes the window (#357). No-op elsewhere.
/// </summary>
public class KeyboardLiftBehavior : BaseBehavior<Popup>
{
#if IOS
    // Observers are tied to Loaded/Unloaded because a popup that closes without
    // detaching its behaviors would otherwise leak them and lift later popups (#363).
    private Thickness _baselineMargin;
    private bool _hasBaseline;
    private NSObject? _willShowToken;
    private NSObject? _willChangeFrameToken;
    private NSObject? _willHideToken;
#if DEBUG
    private static int _liveObservers;
#endif
#endif

    public static Thickness Lift(Thickness baseline, double keyboardHeight) =>
        new(baseline.Left, baseline.Top, baseline.Right,
            baseline.Bottom + Math.Max(0, keyboardHeight));

    // The end frame is screen-sized and sits off-screen when the keyboard hides, so only
    // the part inside the window counts; a floating keyboard overlaps by its own height.
    public static double Overlap(double windowBottom, double keyboardMinY, double keyboardMaxY) =>
        Math.Max(0, Math.Min(windowBottom, keyboardMaxY) - keyboardMinY);

    protected override void OnAttachedTo(Popup bindable)
    {
        base.OnAttachedTo(bindable);
#if IOS
        bindable.Loaded += OnLoaded;
        bindable.Unloaded += OnUnloaded;
#endif
    }

    protected override void OnDetachingFrom(Popup bindable)
    {
#if IOS
        bindable.Loaded -= OnLoaded;
        bindable.Unloaded -= OnUnloaded;
        DisposeObservers();
        if (_hasBaseline) bindable.Margin = _baselineMargin;
#endif
        base.OnDetachingFrom(bindable);
    }

#if IOS
    private void OnLoaded(object? sender, EventArgs e)
    {
        if (View is null) return;
        DisposeObservers();
        _baselineMargin = View.Margin;
        _hasBaseline = true;
        _willShowToken = UIKeyboard.Notifications.ObserveWillShow(OnKeyboardFrame);
        _willChangeFrameToken = UIKeyboard.Notifications.ObserveWillChangeFrame(OnKeyboardFrame);
        _willHideToken = UIKeyboard.Notifications.ObserveWillHide(OnKeyboardWillHide);
#if DEBUG
        LogLiveObservers(Interlocked.Add(ref _liveObservers, 3));
#endif
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        DisposeObservers();
        if (View is null || !_hasBaseline) return;
        View.Margin = _baselineMargin;
    }

    private void DisposeObservers()
    {
#if DEBUG
        var held = _willShowToken is not null;
#endif
        _willShowToken?.Dispose();
        _willShowToken = null;
        _willChangeFrameToken?.Dispose();
        _willChangeFrameToken = null;
        _willHideToken?.Dispose();
        _willHideToken = null;
#if DEBUG
        if (held) LogLiveObservers(Interlocked.Add(ref _liveObservers, -3));
#endif
    }

#if DEBUG
    private static void LogLiveObservers(int count) =>
        PrayerApp.Helpers.Diagnostics.ResolveLog()?.Log("KeyboardLift", $"live observers={count}");
#endif

    private void OnKeyboardFrame(object? sender, UIKeyboardEventArgs args)
    {
        if (View is null) return;
        var window = (View.Handler?.PlatformView as UIView)?.Window;
        if (window is null) return;
        var kb = window.ConvertRectFromWindow(args.FrameEnd, null);
        View.Margin = Lift(_baselineMargin, Overlap(window.Bounds.Height, kb.Top, kb.Bottom));
    }

    private void OnKeyboardWillHide(object? sender, UIKeyboardEventArgs args)
    {
        if (View is null) return;
        View.Margin = _baselineMargin;
    }
#endif
}
