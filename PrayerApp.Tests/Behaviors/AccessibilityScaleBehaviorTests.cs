using Microsoft.Maui.Controls;
using PrayerApp.Behaviors;

namespace PrayerApp.Tests.Behaviors;

/// <summary>
/// xUnit coverage for <see cref="AccessibilityScaleBehavior"/> — the iOS-only
/// accessibility-Dynamic-Type detector behind the Home dashboard's single-column
/// reflow (issue #156). The desktop test host (<c>net10.0</c>, no iOS runtime)
/// exercises the platform-scoping contract: off iOS,
/// <see cref="AccessibilityScaleBehavior.IsAccessibilityScale"/> must stay
/// <c>false</c> so Android's existing 2-column grid is unaffected. The actual
/// iOS <c>UIContentSizeCategory</c> detection is verified on-device (visual
/// sign-off), the same boundary the sibling iOS behaviors
/// (<see cref="ChipStripHeightBehavior"/>) document.
/// </summary>
public class AccessibilityScaleBehaviorTests
{
    [Fact]
    public void IsAccessibilityScale_DefaultsFalse_OffIos()
    {
        var sut = new AccessibilityScaleBehavior();
        Assert.False(sut.IsAccessibilityScale);
    }

    [Fact]
    public void Behavior_AttachAndDetach_DoesNotThrow_OffIos()
    {
        // Off iOS the attach/detach lifecycle is a no-op; IsAccessibilityScale
        // must stay false so the parallel single-column layout it drives never
        // shows on Android.
        var grid = new Grid();
        var sut = new AccessibilityScaleBehavior();

        grid.Behaviors.Add(sut);
        Assert.False(sut.IsAccessibilityScale);

        grid.Behaviors.Remove(sut);
        Assert.False(sut.IsAccessibilityScale);
    }
}
