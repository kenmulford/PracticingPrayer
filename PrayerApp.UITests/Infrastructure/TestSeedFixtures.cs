namespace PrayerApp.UITests.Infrastructure;

/// <summary>
/// Canonical names for the "UITest Delete Target *" throwaway fixture family.
///
/// Seeded by <see cref="TestDataSeed"/> and consumed by destructive UI tests
/// (delete-card, delete-collection, delete-prayer, delete-tag). Centralising
/// the strings here means a rename touches one file and the
/// <c>TestDataSeedConsistencyTests</c> guard in <c>PrayerApp.Tests</c> catches
/// any drift between this set and what the seed actually writes.
/// </summary>
public static class TestSeedFixtures
{
    public const string DeleteCard = "UITest Delete Target Card";
    public const string DeleteCardA = "UITest Delete Target Card A";
    public const string DeleteCardB = "UITest Delete Target Card B";
    public const string DeleteCollectionA = "UITest Delete Target Collection A";
    public const string DeleteCollectionB = "UITest Delete Target Collection B";

    // Runtime-typed by PrayerListTests.cs (not from seed; named here for family symmetry).
    public const string DeleteRuntimePrayer = "UITest Delete Target Prayer";

    // Runtime-generated prefix by TagTests.cs (suffixed with DateTime.UtcNow.Ticks
    // for uniqueness; named here for family symmetry).
    public const string DeleteRuntimeTagPrefix = "UITest Delete Target Tag";
}
