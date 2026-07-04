using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services.Confidential;

/// <summary>
/// Unit coverage for the confidential-export passphrase policy (issue #256): minimum-length
/// gate (12 chars, resolved on the issue) and the live strength-meter buckets. No rigid
/// character-class rule is enforced — variety only influences the strength bucket, never
/// blocks export on its own.
/// </summary>
public class PassphrasePolicyTests
{
    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")] // 11 chars — one short of the minimum
    [InlineData("")]
    public void MeetsMinimumLength_UnderTwelveChars_ReturnsFalse(string passphrase)
    {
        Assert.False(PassphrasePolicy.MeetsMinimumLength(passphrase));
    }

    [Theory]
    [InlineData("twelvecharas")] // exactly 12
    [InlineData("well over twelve characters long")]
    public void MeetsMinimumLength_TwelveOrMoreChars_ReturnsTrue(string passphrase)
    {
        Assert.True(PassphrasePolicy.MeetsMinimumLength(passphrase));
    }

    [Fact]
    public void GetStrength_UnderMinimumLength_IsTooShortRegardlessOfVariety()
    {
        // High variety (upper/lower/digit/symbol) but only 8 characters — length is the hard gate.
        Assert.Equal(PassphraseStrength.TooShort, PassphrasePolicy.GetStrength("Ab1!Ab1!"));
    }

    [Fact]
    public void GetStrength_TwelveLowercaseOnly_IsWeak()
    {
        Assert.Equal(PassphraseStrength.Weak, PassphrasePolicy.GetStrength("aaaaaaaaaaaa"));
    }

    [Fact]
    public void GetStrength_TwelveCharsWithVariety_IsStrongerThanNoVariety()
    {
        var noVariety = PassphrasePolicy.GetStrength("aaaaaaaaaaaa");
        var withVariety = PassphrasePolicy.GetStrength("aA1!aA1!aA1!");

        Assert.True(withVariety > noVariety);
    }

    [Fact]
    public void GetStrength_LongPassphraseWithVariety_IsStrong()
    {
        Assert.Equal(PassphraseStrength.Strong, PassphrasePolicy.GetStrength("Correct-Horse-Battery-Staple-9!"));
    }

    [Fact]
    public void GetStrength_NoRigidClassRule_LongLowercaseOnlyStillClearsWeak()
    {
        // No rigid character-class requirement — a long passphrase with only one character
        // class must still be able to climb out of Weak on length alone.
        var strength = PassphrasePolicy.GetStrength("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); // 36 chars

        Assert.True(strength > PassphraseStrength.Weak);
    }
}
