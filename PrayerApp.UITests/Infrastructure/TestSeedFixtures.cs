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

    // ── Move-prayer fixtures (isolation Principle 1) ───────────────
    // Each move-prayer test in PrayerCardTests.cs OWNS its own Source card
    // (and, for the two user-target moves, its own Target) so no test drains a
    // prayer from a card another move test also mutates — the shared
    // "Move Source Card" order-dependence behind #214. Names are unique full
    // strings (none a substring of another) so the exact/contains locators and
    // the card picker never cross-match. Seeded at BoxId 0 (Loose Cards) — see
    // TestDataSeed.SeedUITestContentAsync.
    public const string MoveReflectSourceCard = "Move Reflect Source Card"; // Cards_MovePrayerBetweenCards_BothCardsReflect
    public const string MoveReflectTargetCard = "Move Reflect Target Card"; // Cards_MovePrayerBetweenCards_BothCardsReflect
    public const string MoveMarginSourceCard = "Move Margin Source Card";   // Cards_MovePrayer_DoesNotLeaveSourceCardWithStaleExpandedMargin
    public const string MoveMarginTargetCard = "Move Margin Target Card";   // Cards_MovePrayer_DoesNotLeaveSourceCardWithStaleExpandedMargin
    public const string MoveSystemSourceCard = "Move System Source Card";   // Cards_MovePrayer_ToSystemCard_... (target is the system "Quick Add" card)

    // Multi-select move fixture (isolation Principle 1).
    // Cards_MultiSelect_MoveToCollection long-presses THIS card into
    // multi-select and MOVES it into "UITest Collection" — so the shared,
    // read-only "UITest Card" stays pristine at top level for the tests that
    // read it (e.g. Cards_Search_ExpandsMatchingSections). Seeded at BoxId 0.
    public const string MultiSelectMoveCard = "UITest MultiSelectMove Card";
}
