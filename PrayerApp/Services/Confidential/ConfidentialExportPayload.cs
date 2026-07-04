using System.Text.Json;
using PrayerApp.Models;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// The sidecar JSON schema encrypted into <c>confidential.enc</c> (issue #256). Carries the
/// full confidential-card subtree — the cards themselves plus every related row (prayers,
/// interactions, tag junctions) — so issue #258's import can round-trip it back into the
/// database exactly. Property names are the schema; #258 must deserialize this exact shape.
/// </summary>
internal class ConfidentialExportPayload
{
    /// <summary>Schema version for this JSON shape, independent of <see cref="ConfidentialBackupCrypto.FormatVersion"/>
    /// (the encryption envelope version). Bump this if the payload shape changes.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<PrayerCard> Cards { get; set; } = new();
    public List<Prayer> Prayers { get; set; } = new();
    public List<PrayerInteraction> Interactions { get; set; } = new();
    public List<PrayerCardTag> TagJunctions { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ConfidentialExportPayload FromJson(string json) =>
        JsonSerializer.Deserialize<ConfidentialExportPayload>(json, SerializerOptions)
            ?? throw new InvalidDataException("Confidential export payload JSON deserialized to null.");
}

/// <summary>
/// Splits a full in-memory snapshot of the DB's card-related tables into a "confidential"
/// subtree (to be encrypted into the sidecar) and a "scrubbed" remainder (safe to leave in
/// the plaintext <c>prayer_app.db</c> entry). A card is confidential when
/// <see cref="PrayerCard.GetEffectiveProtectionMode"/> is anything other than
/// <see cref="CardProtectionMode.None"/>, resolved against the card's own <see cref="CardBox"/>
/// (looked up by <see cref="PrayerCard.BoxId"/>; a missing/unknown box behaves like no box).
/// </summary>
internal static class ConfidentialExportSplitter
{
    public readonly record struct SplitResult(
        List<PrayerCard> ConfidentialCards,
        List<PrayerCard> RemainingCards,
        List<Prayer> ConfidentialPrayers,
        List<Prayer> RemainingPrayers,
        List<PrayerInteraction> ConfidentialInteractions,
        List<PrayerInteraction> RemainingInteractions,
        List<PrayerCardTag> ConfidentialTagJunctions,
        List<PrayerCardTag> RemainingTagJunctions);

    /// <summary>True when any card in <paramref name="cards"/> has an effective protection mode other than None.</summary>
    public static bool HasAnyConfidentialCard(IReadOnlyList<PrayerCard> cards, IReadOnlyList<CardBox> boxes)
    {
        var boxesById = boxes.ToDictionary(b => b.Id);
        return cards.Any(c => IsConfidential(c, boxesById));
    }

    public static SplitResult Split(
        IReadOnlyList<PrayerCard> cards,
        IReadOnlyList<CardBox> boxes,
        IReadOnlyList<Prayer> prayers,
        IReadOnlyList<PrayerInteraction> interactions,
        IReadOnlyList<PrayerCardTag> tagJunctions)
    {
        var boxesById = boxes.ToDictionary(b => b.Id);

        var confidentialCards = cards.Where(c => IsConfidential(c, boxesById)).ToList();
        var confidentialCardIds = confidentialCards.Select(c => c.Id).ToHashSet();
        var remainingCards = cards.Where(c => !confidentialCardIds.Contains(c.Id)).ToList();

        var confidentialPrayers = prayers.Where(p => confidentialCardIds.Contains(p.PrayerCardId)).ToList();
        var confidentialPrayerIds = confidentialPrayers.Select(p => p.Id).ToHashSet();
        var remainingPrayers = prayers.Where(p => !confidentialCardIds.Contains(p.PrayerCardId)).ToList();

        var confidentialInteractions = interactions.Where(i => confidentialPrayerIds.Contains(i.PrayerId)).ToList();
        var remainingInteractions = interactions.Where(i => !confidentialPrayerIds.Contains(i.PrayerId)).ToList();

        // PrayerCardTag rows key off PrayerRequestId (the current schema — PrayerCardId is
        // deprecated legacy, see PrayerCardTag.cs). Legacy rows with PrayerRequestId == 0
        // cannot be attributed to a confidential prayer, so they are left in the remainder —
        // matching the existing migration's treatment of legacy junction rows.
        var confidentialTagJunctions = tagJunctions
            .Where(t => t.PrayerRequestId != 0 && confidentialPrayerIds.Contains(t.PrayerRequestId))
            .ToList();
        var confidentialTagJunctionIds = confidentialTagJunctions.Select(t => t.Id).ToHashSet();
        var remainingTagJunctions = tagJunctions.Where(t => !confidentialTagJunctionIds.Contains(t.Id)).ToList();

        return new SplitResult(
            confidentialCards, remainingCards,
            confidentialPrayers, remainingPrayers,
            confidentialInteractions, remainingInteractions,
            confidentialTagJunctions, remainingTagJunctions);
    }

    private static bool IsConfidential(PrayerCard card, Dictionary<int, CardBox> boxesById)
    {
        boxesById.TryGetValue(card.BoxId, out var box);
        return PrayerCard.GetEffectiveProtectionMode(card, box) != CardProtectionMode.None;
    }
}
