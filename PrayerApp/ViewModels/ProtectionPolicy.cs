using PrayerApp.Models;

namespace PrayerApp.ViewModels;

/// <summary>
/// Feature-internal helper centralizing the "blocked while locked" condition used across
/// Confidential Cards Wave 3 (issue #255): a card is access-blocked when its effective
/// protection mode (own <see cref="PrayerCard.ProtectionMode"/> or a box cascade — see
/// <see cref="PrayerCard.GetEffectiveProtectionMode"/>) is anything other than
/// <see cref="CardProtectionMode.None"/> AND the confidential-access session is locked.
/// Mirrors the condition shape already private to
/// <c>PrayerCardViewModel.EnsureUnlockedForAccessAsync</c> (issue #254) so the five call
/// sites in #255 (Prayer Time search-scoping/re-auth, card search, prayer-list search, two
/// share gates) don't each restate it. Deliberately kept internal to the ViewModels
/// namespace — not a member of <see cref="Services.IConfidentialAccessService"/> — because
/// it composes two already-public primitives (the model predicate + the service's
/// <see cref="Services.IConfidentialAccessService.IsSessionUnlocked"/> flag) rather than
/// adding new service surface.
/// </summary>
internal static class ProtectionPolicy
{
    /// <summary>
    /// True when <paramref name="card"/> is effectively protected (via its own mode or
    /// <paramref name="box"/>'s cascade) and <paramref name="isSessionUnlocked"/> is false.
    /// </summary>
    public static bool IsAccessBlocked(PrayerCard card, CardBox? box, bool isSessionUnlocked) =>
        PrayerCard.GetEffectiveProtectionMode(card, box) != CardProtectionMode.None
        && !isSessionUnlocked;
}
