namespace PrayerApp.Services.Confidential;

/// <summary>
/// In-memory failed-PIN-attempt counter with escalating cooldown, internal to
/// <see cref="ConfidentialAccessService"/>. Policy: PIN entry is allowed freely for the
/// first <see cref="LockoutThreshold"/> - 1 (4) failures; the 5th consecutive failure arms
/// a cooldown so the very next (6th) attempt is locked out — regardless of whether that
/// next PIN would have been correct. Each additional failure while still locked out doubles
/// the cooldown (30s, 60s, 120s, ...), capped at <see cref="MaxCooldown"/>. A successful
/// verify (<see cref="Reset"/>) clears the counter and any active cooldown. Not persisted —
/// resets on app relaunch, matching the session-scoped nature of <see cref="IConfidentialAccessService.IsSessionUnlocked"/>.
/// </summary>
internal sealed class PinAttemptTracker
{
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan BaseCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(30);

    private readonly Func<DateTimeOffset> _now;
    private int _consecutiveFailures;
    private DateTimeOffset? _lockedUntil;

    public PinAttemptTracker(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True while a cooldown from a prior failure run is still in effect.</summary>
    public bool IsLockedOut => _lockedUntil is { } until && _now() < until;

    /// <summary>Remaining cooldown, or <see cref="TimeSpan.Zero"/> when not locked out.</summary>
    public TimeSpan RemainingLockout => IsLockedOut ? _lockedUntil!.Value - _now() : TimeSpan.Zero;

    /// <summary>Records a failed PIN attempt. At and past the threshold, arms/extends the escalating cooldown.</summary>
    public void RecordFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures < LockoutThreshold)
            return;

        var failuresAtOrPastThreshold = _consecutiveFailures - LockoutThreshold + 1; // 1, 2, 3, ...
        var cooldownSeconds = BaseCooldown.TotalSeconds * Math.Pow(2, failuresAtOrPastThreshold - 1);
        var cooldown = TimeSpan.FromSeconds(Math.Min(cooldownSeconds, MaxCooldown.TotalSeconds));
        _lockedUntil = _now() + cooldown;
    }

    /// <summary>Clears the failure count and any active cooldown after a successful verify.</summary>
    public void Reset()
    {
        _consecutiveFailures = 0;
        _lockedUntil = null;
    }
}
