using PrayerApp.Models;
using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services.Confidential;

/// <summary>
/// Unit coverage for the confidential-card detection + scrub-split logic that
/// BackupService.ExportAsync (issue #256) uses to decide whether to prompt, and to build the
/// scrubbed <c>prayer_app.db</c> entry (must contain ZERO confidential card rows) plus the
/// <c>confidential.enc</c> sidecar payload. Effective-protection detection itself
/// (PrayerCard.GetEffectiveProtectionMode) is issue #250 and already covered elsewhere — these
/// tests exercise the split/scrub behavior built on top of it.
/// </summary>
public class ConfidentialExportSplitterTests
{
    [Fact]
    public void HasAnyConfidentialCard_NoCardsProtected_ReturnsFalse()
    {
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, ProtectionMode = CardProtectionMode.None, BoxId = 0 }
        };
        var boxes = new List<CardBox>();

        Assert.False(ConfidentialExportSplitter.HasAnyConfidentialCard(cards, boxes));
    }

    [Fact]
    public void HasAnyConfidentialCard_CardWithOwnProtectionMode_ReturnsTrue()
    {
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, ProtectionMode = CardProtectionMode.Hidden, BoxId = 0 }
        };
        var boxes = new List<CardBox>();

        Assert.True(ConfidentialExportSplitter.HasAnyConfidentialCard(cards, boxes));
    }

    [Fact]
    public void HasAnyConfidentialCard_CardInheritsFromProtectAllCardsBox_ReturnsTrue()
    {
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, ProtectionMode = CardProtectionMode.None, BoxId = 10 }
        };
        var boxes = new List<CardBox>
        {
            new() { Id = 10, ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible }
        };

        Assert.True(ConfidentialExportSplitter.HasAnyConfidentialCard(cards, boxes));
    }

    [Fact]
    public void Split_ScrubbedRemainder_ContainsZeroConfidentialCardRows()
    {
        var confidentialCard = new PrayerCard { Id = 1, Title = "Secret", ProtectionMode = CardProtectionMode.Hidden, BoxId = 0 };
        var normalCard = new PrayerCard { Id = 2, Title = "Public", ProtectionMode = CardProtectionMode.None, BoxId = 0 };
        var cards = new List<PrayerCard> { confidentialCard, normalCard };
        var boxes = new List<CardBox>();

        var result = ConfidentialExportSplitter.Split(cards, boxes,
            prayers: new List<Prayer>(),
            interactions: new List<PrayerInteraction>(),
            tagJunctions: new List<PrayerCardTag>());

        Assert.DoesNotContain(result.RemainingCards, c => c.Id == confidentialCard.Id);
        Assert.Contains(result.RemainingCards, c => c.Id == normalCard.Id);
        Assert.Single(result.ConfidentialCards);
        Assert.Equal(confidentialCard.Id, result.ConfidentialCards[0].Id);
    }

    [Fact]
    public void Split_PrayersOnConfidentialCard_MoveToConfidentialSide()
    {
        var confidentialCard = new PrayerCard { Id = 1, ProtectionMode = CardProtectionMode.LockedVisible, BoxId = 0 };
        var normalCard = new PrayerCard { Id = 2, ProtectionMode = CardProtectionMode.None, BoxId = 0 };
        var cards = new List<PrayerCard> { confidentialCard, normalCard };

        var confidentialPrayer = new Prayer { Id = 100, PrayerCardId = 1, Title = "Secret prayer" };
        var normalPrayer = new Prayer { Id = 101, PrayerCardId = 2, Title = "Public prayer" };
        var prayers = new List<Prayer> { confidentialPrayer, normalPrayer };

        var result = ConfidentialExportSplitter.Split(cards, new List<CardBox>(), prayers,
            interactions: new List<PrayerInteraction>(),
            tagJunctions: new List<PrayerCardTag>());

        Assert.DoesNotContain(result.RemainingPrayers, p => p.Id == confidentialPrayer.Id);
        Assert.Contains(result.RemainingPrayers, p => p.Id == normalPrayer.Id);
        Assert.Single(result.ConfidentialPrayers);
        Assert.Equal(confidentialPrayer.Id, result.ConfidentialPrayers[0].Id);
    }

    [Fact]
    public void Split_InteractionsOnConfidentialPrayer_MoveToConfidentialSide()
    {
        var confidentialCard = new PrayerCard { Id = 1, ProtectionMode = CardProtectionMode.Hidden, BoxId = 0 };
        var cards = new List<PrayerCard> { confidentialCard };
        var confidentialPrayer = new Prayer { Id = 100, PrayerCardId = 1 };
        var prayers = new List<Prayer> { confidentialPrayer };
        var interaction = new PrayerInteraction { Id = 500, PrayerId = 100 };
        var interactions = new List<PrayerInteraction> { interaction };

        var result = ConfidentialExportSplitter.Split(cards, new List<CardBox>(), prayers, interactions,
            tagJunctions: new List<PrayerCardTag>());

        Assert.Empty(result.RemainingInteractions);
        Assert.Single(result.ConfidentialInteractions);
        Assert.Equal(interaction.Id, result.ConfidentialInteractions[0].Id);
    }

    [Fact]
    public void Split_TagJunctionsOnConfidentialPrayer_MoveToConfidentialSide()
    {
        var confidentialCard = new PrayerCard { Id = 1, ProtectionMode = CardProtectionMode.Hidden, BoxId = 0 };
        var cards = new List<PrayerCard> { confidentialCard };
        var confidentialPrayer = new Prayer { Id = 100, PrayerCardId = 1 };
        var prayers = new List<Prayer> { confidentialPrayer };
        var junction = new PrayerCardTag { Id = 900, PrayerRequestId = 100, PrayerTagId = 5 };
        var junctions = new List<PrayerCardTag> { junction };

        var result = ConfidentialExportSplitter.Split(cards, new List<CardBox>(), prayers,
            interactions: new List<PrayerInteraction>(), tagJunctions: junctions);

        Assert.Empty(result.RemainingTagJunctions);
        Assert.Single(result.ConfidentialTagJunctions);
    }

    [Fact]
    public void Split_LegacyTagJunctionWithZeroRequestId_StaysInRemainder()
    {
        // Legacy junction rows (PrayerRequestId == 0) predate the request-level tag schema and
        // cannot be attributed to any prayer — they are left alone, matching how the existing
        // BUG-21 migration treats them.
        var legacyJunction = new PrayerCardTag { Id = 1, PrayerRequestId = 0, PrayerCardId = 1 };
        var junctions = new List<PrayerCardTag> { legacyJunction };

        var result = ConfidentialExportSplitter.Split(
            new List<PrayerCard>(), new List<CardBox>(),
            new List<Prayer>(), new List<PrayerInteraction>(), junctions);

        Assert.Single(result.RemainingTagJunctions);
        Assert.Empty(result.ConfidentialTagJunctions);
    }
}
