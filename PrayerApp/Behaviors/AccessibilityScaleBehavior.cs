using CommunityToolkit.Maui.Behaviors;
#if IOS
using Foundation;
using UIKit;
#endif

namespace PrayerApp.Behaviors;

/// <summary>
/// Detects iOS "accessibility" Dynamic Type sizes (issue #156) and exposes the
/// result as a bindable <see cref="IsAccessibilityScale"/> flag. MainPage binds
/// two parallel Home-dashboard layouts against this single flag via
/// <c>x:Reference</c> — the existing 2-column metric Grid (normal scale) and a
/// single-column <see cref="VerticalStackLayout"/> (accessibility scale) — so
/// neither layout mutates <c>Grid.Row</c>/<c>Grid.Column</c> live in
/// code-behind; the flag alone drives which one is shown.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the #154 <see cref="ChipStripHeightBehavior"/> detection pattern:
/// read the current <c>UIContentSizeCategory</c> on attach, and re-evaluate on
/// every OS content-size-category change (<c>ObserveContentSizeCategoryChanged</c>)
/// so the reflow happens live while Home is open — no relaunch required.
/// </para>
/// <para>
/// <b>iOS-scoped.</b> Off iOS, <see cref="IsAccessibilityScale"/> stays at its
/// default <c>false</c>, so Android's existing 2-column grid is unchanged.
/// </para>
/// <para>Citations:
///   https://learn.microsoft.com/dotnet/api/uikit.uicontentsizecategoryextensions.isaccessibilitycategory?view=net-ios-26.4-10.0 ("IsAccessibilityCategory" extension on UIContentSizeCategory)
///   https://learn.microsoft.com/dotnet/api/uikit.uiapplication.getpreferredcontentsizecategory?view=net-ios-26.4-10.0
///   https://learn.microsoft.com/dotnet/api/uikit.uicontentsizecategory?view=net-ios-26.4-10.0 (ObserveContentSizeCategoryChanged notification contract)
///   https://learn.microsoft.com/dotnet/maui/xaml/markup-extensions/consume#xreference-markup-extension (x:Reference resolves any named object in the page namescope, not only VisualElements)
/// </para>
/// </remarks>
public class AccessibilityScaleBehavior : BaseBehavior<VisualElement>
{
    public static readonly BindableProperty IsAccessibilityScaleProperty =
        BindableProperty.Create(
            nameof(IsAccessibilityScale),
            typeof(bool),
            typeof(AccessibilityScaleBehavior),
            false);

    /// <summary>
    /// True when the OS is at an iOS accessibility Dynamic Type size. Always
    /// <c>false</c> off iOS (Android is unaffected).
    /// </summary>
    public bool IsAccessibilityScale
    {
        get => (bool)GetValue(IsAccessibilityScaleProperty);
        private set => SetValue(IsAccessibilityScaleProperty, value);
    }

#if IOS
    private NSObject? _contentSizeChangedToken;
#endif

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
#if IOS
        Apply();
        _contentSizeChangedToken = UIApplication.Notifications.ObserveContentSizeCategoryChanged(
            (_, _) => Apply());
#endif
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
#if IOS
        _contentSizeChangedToken?.Dispose();
        _contentSizeChangedToken = null;
#endif
        IsAccessibilityScale = false;
        base.OnDetachingFrom(bindable);
    }

#if IOS
    private void Apply() =>
        IsAccessibilityScale = UIApplication.SharedApplication.GetPreferredContentSizeCategory().IsAccessibilityCategory();
#endif
}
