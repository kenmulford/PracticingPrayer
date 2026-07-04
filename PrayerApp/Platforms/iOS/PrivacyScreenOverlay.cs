using CoreGraphics;
using UIKit;

namespace PrayerApp.Platforms.iOS;

/// <summary>
/// Issue #257 — background privacy screen. iOS has no FLAG_SECURE equivalent, so a plain
/// branded overlay view is added directly to the key window's view hierarchy on
/// <c>OnResignActivation</c> (WillResignActive) — which fires synchronously before iOS
/// captures the app-switcher snapshot — and removed on <c>OnActivated</c> (DidBecomeActive).
///
/// Plain UIKit only, no MAUI/XAML: MAUI page rendering is asynchronous and cannot be
/// guaranteed to complete before WillResignActive returns, so the overlay must be built
/// from native views. Colors mirror Colors.xaml's PageLight/PageDark + Tertiary/TextPrimaryDark
/// tokens (source of truth), matching the existing Android-side mirror convention in
/// Platforms/Android/Resources/values/colors.xml.
/// </summary>
internal static class PrivacyScreenOverlay
{
    private static UIView? _overlay;

    public static void Show()
    {
        if (_overlay is not null) return;

        var scene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();
        var window = scene?.Windows.FirstOrDefault(w => w.IsKeyWindow);
        if (window is null) return;

        var isDark = window.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;

        var overlay = new UIView(window.Bounds)
        {
            BackgroundColor = isDark
                ? UIColor.FromRGB(0x0d, 0x0e, 0x0c)   // PageDark
                : UIColor.FromRGB(0xFA, 0xF8, 0xF3),  // PageLight
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions
        };

        var label = new UILabel(new CGRect(0, 0, window.Bounds.Width, 30))
        {
            Text = "Practicing Prayer",
            TextAlignment = UITextAlignment.Center,
            Font = UIFont.SystemFontOfSize(20)!,
            TextColor = isDark
                ? UIColor.FromRGB(0xdc, 0xe0, 0xd5)   // TextPrimaryDark
                : UIColor.FromRGB(0x3F, 0x4A, 0x34),  // Tertiary
            AutoresizingMask = UIViewAutoresizing.FlexibleMargins,
            Center = new CGPoint(overlay.Bounds.GetMidX(), overlay.Bounds.GetMidY())
        };
        overlay.AddSubview(label);

        window.AddSubview(overlay);
        _overlay = overlay;
    }

    public static void Hide()
    {
        _overlay?.RemoveFromSuperview();
        _overlay = null;
    }
}
