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
    public void CanAttachToBorder()
    {
        var border = new Border();
        var ex = Record.Exception(() => border.Behaviors.Add(new KeyboardLiftBehavior()));
        Assert.Null(ex);
    }
}
