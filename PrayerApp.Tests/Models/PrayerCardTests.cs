using PrayerApp.Models;

namespace PrayerApp.Tests.Models;

public class PrayerCardTests
{
    // ── GetEffectiveProtectionMode ───────────────────────────────────────────

    [Fact]
    public void GetEffectiveProtectionMode_CardModeSet_CardModeWins()
    {
        var card = new PrayerCard { ProtectionMode = CardProtectionMode.Hidden };
        var box = new CardBox { ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible };

        var effective = PrayerCard.GetEffectiveProtectionMode(card, box);

        Assert.Equal(CardProtectionMode.Hidden, effective);
    }

    [Fact]
    public void GetEffectiveProtectionMode_CardNoneBoxProtectAllOn_CascadesFromBox()
    {
        var card = new PrayerCard { ProtectionMode = CardProtectionMode.None };
        var box = new CardBox { ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible };

        var effective = PrayerCard.GetEffectiveProtectionMode(card, box);

        Assert.Equal(CardProtectionMode.LockedVisible, effective);
    }

    [Fact]
    public void GetEffectiveProtectionMode_CardNoneBoxProtectAllOff_ReturnsNone()
    {
        var card = new PrayerCard { ProtectionMode = CardProtectionMode.None };
        var box = new CardBox { ProtectAllCards = false, CardProtectionMode = CardProtectionMode.Hidden };

        var effective = PrayerCard.GetEffectiveProtectionMode(card, box);

        Assert.Equal(CardProtectionMode.None, effective);
    }

    [Fact]
    public void GetEffectiveProtectionMode_NoBox_FallsBackToCardMode()
    {
        var card = new PrayerCard { ProtectionMode = CardProtectionMode.LockedVisible };

        var effective = PrayerCard.GetEffectiveProtectionMode(card, box: null);

        Assert.Equal(CardProtectionMode.LockedVisible, effective);
    }

    [Fact]
    public void GetEffectiveProtectionMode_NoBoxCardNone_ReturnsNone()
    {
        var card = new PrayerCard { ProtectionMode = CardProtectionMode.None };

        var effective = PrayerCard.GetEffectiveProtectionMode(card, box: null);

        Assert.Equal(CardProtectionMode.None, effective);
    }
}
