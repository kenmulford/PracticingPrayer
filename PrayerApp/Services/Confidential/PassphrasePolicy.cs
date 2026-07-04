namespace PrayerApp.Services.Confidential;

/// <summary>Strength bucket shown by the live meter in the passphrase-entry popup.</summary>
public enum PassphraseStrength
{
    TooShort,
    Weak,
    Fair,
    Strong
}

/// <summary>
/// Validation + live strength heuristic for the confidential-export passphrase (issue #256).
/// Policy (resolved on the issue): minimum 12 characters, no rigid character-class rules —
/// the meter rewards length and character variety but never blocks export on missing
/// uppercase/digit/symbol the way a rigid policy would.
/// </summary>
internal static class PassphrasePolicy
{
    public const int MinLength = 12;

    /// <summary>True once the passphrase meets the minimum length — the only hard export gate.</summary>
    public static bool MeetsMinimumLength(string passphrase) => passphrase.Length >= MinLength;

    /// <summary>
    /// Buckets a passphrase into a strength tier for the live meter. Below
    /// <see cref="MinLength"/> is always <see cref="PassphraseStrength.TooShort"/> regardless of
    /// variety — length is the hard gate. At or above the minimum, longer length and more
    /// character-class variety (lower/upper/digit/symbol) push the bucket up; neither is
    /// required on its own (no rigid class rules).
    /// </summary>
    public static PassphraseStrength GetStrength(string passphrase)
    {
        if (!MeetsMinimumLength(passphrase))
            return PassphraseStrength.TooShort;

        var varietyCount = CountCharacterClasses(passphrase);

        // Length score: every 4 extra characters past the minimum adds one point.
        var lengthScore = (passphrase.Length - MinLength) / 4;
        var score = lengthScore + varietyCount;

        return score switch
        {
            <= 1 => PassphraseStrength.Weak,
            <= 3 => PassphraseStrength.Fair,
            _ => PassphraseStrength.Strong
        };
    }

    private static int CountCharacterClasses(string passphrase)
    {
        bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false;
        foreach (var c in passphrase)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (!char.IsWhiteSpace(c)) hasSymbol = true;
        }
        return (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
    }
}
