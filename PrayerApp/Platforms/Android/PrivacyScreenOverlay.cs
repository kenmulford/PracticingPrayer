using Android.App;
using Android.Content.Res;
using Android.Views;
using Android.Widget;
using AndroidColor = Android.Graphics.Color;

namespace PrayerApp.Platforms.Android;

/// <summary>
/// Issue #257 — background privacy screen. Blanks the app-switcher (recents) thumbnail
/// with a branded placeholder for users who have at least one confidential card, so
/// locked content never leaks into the OS multitasking snapshot.
///
/// Two layered mechanisms, both gated on MauiProgram's confidential-cards-exist cache:
/// 1. <see cref="WindowManagerFlags.Secure"/> on the activity's native Window — the
///    OS-guaranteed mechanism (also blocks in-app screenshots/screen-recording, a
///    reasonable trade-off for someone with confidential content; see Decision Log).
///    Set/cleared by MauiProgram.RefreshConfidentialCardsCacheAsync, not by this class.
/// 2. A plain native overlay view (matching <c>colorPageLight</c>/<c>colorPageDark</c> —
///    already-established mirrors of Colors.xaml's PageLight/PageDark tokens, see
///    Platforms/Android/Resources/values/colors.xml) so the recents thumbnail itself
///    reads as the app rather than the OS's generic blank placeholder.
///
/// Added in <c>OnPause</c> (before the OS captures the recents snapshot) and removed in
/// <c>OnResume</c>. Plain native views only — no MAUI/XAML — because MAUI page rendering
/// is asynchronous and cannot be guaranteed to complete before OnPause returns.
/// </summary>
internal static class PrivacyScreenOverlay
{
    private const int OverlayId = 0x50727950; // "PryP" — arbitrary stable view id for lookup/removal.

    /// <summary>
    /// Called from OnPause. Shows the branded overlay only if FLAG_SECURE is already set —
    /// which <see cref="Hide"/> (called from the prior OnResume) set based on whether any
    /// confidential card currently exists. This keeps OnPause itself synchronous and gate-free:
    /// the async "confidential cards exist" check already happened on the way in.
    /// </summary>
    public static void Show(Activity activity)
    {
        var flags = activity.Window?.Attributes?.Flags ?? 0;
        if (!flags.HasFlag(WindowManagerFlags.Secure)) return;

        var decorView = (ViewGroup?)activity.Window?.DecorView;
        if (decorView is null || decorView.FindViewById(OverlayId) is not null) return;

        var isDark = (activity.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;

        var overlay = new LinearLayout(activity)
        {
            Id = OverlayId,
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        overlay.SetGravity(GravityFlags.Center);
        overlay.SetBackgroundColor(isDark ? AndroidColor.ParseColor("#0d0e0c") : AndroidColor.ParseColor("#FAF8F3"));

        var label = new TextView(activity)
        {
            Text = "Practicing Prayer",
            TextSize = 20
        };
        label.SetTextColor(isDark ? AndroidColor.ParseColor("#dce0d5") : AndroidColor.ParseColor("#3F4A34"));
        overlay.AddView(label);

        decorView.AddView(overlay, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>
    /// Removes the overlay on resume. FLAG_SECURE's on/off state is managed separately by
    /// MauiProgram's RefreshConfidentialCardsCacheAsync (called right after this on every
    /// resume, and again on every card/box change message) — this method only cleans up
    /// the view added by <see cref="Show"/>.
    /// </summary>
    public static void Hide(Activity activity)
    {
        var decorView = (ViewGroup?)activity.Window?.DecorView;
        var overlay = decorView?.FindViewById(OverlayId);
        if (overlay is not null)
            decorView!.RemoveView(overlay);
    }
}
