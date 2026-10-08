using PrayerApp.Models;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class ProtectionPolicyTests
{
    private static PrayerCard Card(CardProtectionMode mode) => new() { ProtectionMode = mode };

    [Theory]
    [InlineData(CardProtectionMode.None)]
    [InlineData(CardProtectionMode.LockedVisible)]
    [InlineData(CardProtectionMode.Hidden)]
    public void IsHiddenWhileLocked_SessionUnlocked_ReturnsFalse(CardProtectionMode mode)
    {
        Assert.False(ProtectionPolicy.IsHiddenWhileLocked(Card(mode), null, isSessionUnlocked: true));
    }

    [Fact]
    public void IsHiddenWhileLocked_LockedHiddenCard_ReturnsTrue()
    {
        Assert.True(ProtectionPolicy.IsHiddenWhileLocked(Card(CardProtectionMode.Hidden), null, isSessionUnlocked: false));
    }

    [Fact]
    public void IsHiddenWhileLocked_LockedLockedVisibleCard_ReturnsFalse()
    {
        Assert.False(ProtectionPolicy.IsHiddenWhileLocked(Card(CardProtectionMode.LockedVisible), null, isSessionUnlocked: false));
    }

    [Fact]
    public void IsHiddenWhileLocked_LockedUnprotectedCard_ReturnsFalse()
    {
        Assert.False(ProtectionPolicy.IsHiddenWhileLocked(Card(CardProtectionMode.None), null, isSessionUnlocked: false));
    }

    [Fact]
    public void IsHiddenWhileLocked_LockedNoneCardInHiddenBox_ReturnsTrue()
    {
        var box = new CardBox { ProtectAllCards = true, CardProtectionMode = CardProtectionMode.Hidden };

        Assert.True(ProtectionPolicy.IsHiddenWhileLocked(Card(CardProtectionMode.None), box, isSessionUnlocked: false));
    }
}
