using CommunityToolkit.Maui.Behaviors;
#if IOS
using Foundation;
using UIKit;
#endif

namespace PrayerApp.Behaviors;

/// <summary>
/// Raises a view's bottom margin by the keyboard height while the keyboard is
/// shown, so a centred popup re-centres in the area above it. iOS-only: a
/// CommunityToolkit popup is centred on a full-screen page, so the platform's
/// <c>KeyboardAutoManagerScroll</c> only nudges the focused Entry and never
/// re-lays out the popup. Android resizes the window (#357). No-op elsewhere.
/// </summary>
public class KeyboardLiftBehavior : BaseBehavior<View>
{
#if IOS
    private Thickness _baselineMargin;
    private NSObject? _willShowToken;
    private NSObject? _willChangeFrameToken;
    private NSObject? _willHideToken;
#endif

    public static Thickness Lift(Thickness baseline, double keyboardHeight) =>
        new(baseline.Left, baseline.Top, baseline.Right,
            baseline.Bottom + Math.Max(0, keyboardHeight));

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
#if IOS
        _baselineMargin = bindable.Margin;
        _willShowToken = UIKeyboard.Notifications.ObserveWillShow(OnKeyboardFrame);
        _willChangeFrameToken = UIKeyboard.Notifications.ObserveWillChangeFrame(OnKeyboardFrame);
        _willHideToken = UIKeyboard.Notifications.ObserveWillHide(OnKeyboardWillHide);
#endif
    }

    protected override void OnDetachingFrom(View bindable)
    {
#if IOS
        _willShowToken?.Dispose();
        _willShowToken = null;
        _willChangeFrameToken?.Dispose();
        _willChangeFrameToken = null;
        _willHideToken?.Dispose();
        _willHideToken = null;
        bindable.Margin = _baselineMargin;
#endif
        base.OnDetachingFrom(bindable);
    }

#if IOS
    private void OnKeyboardFrame(object? sender, UIKeyboardEventArgs args)
    {
        if (View is null) return;
        View.Margin = Lift(_baselineMargin, args.FrameEnd.Height);
    }

    private void OnKeyboardWillHide(object? sender, UIKeyboardEventArgs args)
    {
        if (View is null) return;
        View.Margin = _baselineMargin;
    }
#endif
}
