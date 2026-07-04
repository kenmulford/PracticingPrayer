using System.Collections.Generic;
using System.Threading.Tasks;

using PrayerApp.Models;

namespace PrayerApp.Services;

public interface IBoxService
{
    /// <summary>Returns all boxes sorted by SortOrder then Name.</summary>
    Task<IReadOnlyList<CardBox>> GetBoxesAsync();

    /// <summary>Returns a system box by key ("system" or "archived"), or null if not found.</summary>
    Task<CardBox?> GetSystemBoxAsync(string systemKey);

    Task<CardBox> SaveBoxAsync(CardBox box);

    /// <summary>
    /// Deletes a box. If <paramref name="deleteCards"/> is false, cards are unassigned (moved to Unboxed).
    /// If true, all cards in the box and their prayer requests are cascade-deleted.
    /// </summary>
    Task DeleteBoxAsync(int boxId, bool deleteCards);

    /// <summary>Ensures System and Archived boxes exist. Called at app startup as a resilience fallback.</summary>
    Task SeedSystemBoxesAsync();

    /// <summary>
    /// True if the DB currently contains at least one card whose effective protection mode
    /// (own mode, or box-cascade — see <see cref="Models.PrayerCard.GetEffectiveProtectionMode"/>)
    /// is not <see cref="Models.CardProtectionMode.None"/>. Drives the background privacy-screen
    /// gate (#257): platform snapshot-blanking (Android FLAG_SECURE / iOS resign-active overlay)
    /// only activates for users who actually have confidential content to protect.
    /// </summary>
    Task<bool> HasConfidentialCardsAsync();

    void InvalidateCache();
}
