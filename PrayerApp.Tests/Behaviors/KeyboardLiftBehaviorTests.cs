using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using PrayerApp.Behaviors;

namespace PrayerApp.Tests.Behaviors;

/// <summary>
/// xUnit coverage for <see cref="KeyboardLiftBehavior"/>. The keyboard
/// notification wiring is iOS-only and compiled out of this net10.0 project;
/// the platform-neutral <c>Lift</c> math is what runs here.
/// </summary>
public class KeyboardLiftBehaviorTests
{
    [Fact]
    public void Lift_AddsKeyboardHeightToBottom()
    {
        Assert.Equal(new Thickness(1, 2, 3, 304), KeyboardLiftBehavior.Lift(new Thickness(1, 2, 3, 4), 300));
    }

    [Fact]
    public void Lift_NegativeHeight_KeepsBaseline()
    {
        Assert.Equal(new Thickness(0, 0, 0, 8), KeyboardLiftBehavior.Lift(new Thickness(0, 0, 0, 8), -5));
    }

    [Fact]
    public void Overlap_DockedKeyboard_ReturnsOverlap()
    {
        Assert.Equal(334, KeyboardLiftBehavior.Overlap(1024, 690, 1024));
    }

    [Fact]
    public void Overlap_OffscreenEndFrame_ReturnsZero()
    {
        Assert.Equal(0, KeyboardLiftBehavior.Overlap(1024, 1024, 1358));
    }

    [Fact]
    public void Overlap_WindowShorterThanScreen_ClampsToWindow()
    {
        Assert.Equal(210, KeyboardLiftBehavior.Overlap(900, 690, 1024));
    }

    [Fact]
    public void Overlap_FloatingKeyboard_ReturnsKeyboardHeight()
    {
        Assert.Equal(200, KeyboardLiftBehavior.Overlap(1024, 400, 600));
    }

    [Fact]
    public void CanAttachToPopup()
    {
        var popup = new Popup();
        var ex = Record.Exception(() => popup.Behaviors.Add(new KeyboardLiftBehavior()));
        Assert.Null(ex);
    }
}
