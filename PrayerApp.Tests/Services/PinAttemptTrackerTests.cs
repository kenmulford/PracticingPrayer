using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Direct unit coverage for the confidential-access PIN failed-attempt counter and
/// escalating cooldown (issue #251). Injects a controllable clock so the escalating
/// backoff can be asserted deterministically without real sleeps.
/// </summary>
public class PinAttemptTrackerTests
{
    [Fact]
    public void IsLockedOut_Initially_IsFalse()
    {
        var tracker = new PinAttemptTracker();

        Assert.False(tracker.IsLockedOut);
    }

    [Fact]
    public void RecordFailure_UpToFourTimes_DoesNotLockOut()
    {
        // 4 failures stay under the 5-failure threshold — the 5th attempt (using one of
        // these 4 recorded failures) must still be allowed through to try the PIN.
        var tracker = new PinAttemptTracker();

        for (var i = 0; i < 4; i++)
            tracker.RecordFailure();

        Assert.False(tracker.IsLockedOut);
    }

    [Fact]
    public void RecordFailure_FifthConsecutiveFailure_ArmsLockoutForTheNextAttempt()
    {
        // "After ~5 failed attempts" — the 5th failure arms the cooldown so the 6th
        // attempt is rejected outright, before it even reaches PIN comparison.
        var tracker = new PinAttemptTracker();

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure();

        Assert.True(tracker.IsLockedOut);
    }

    [Fact]
    public void RecordFailure_CooldownEscalatesWithMoreFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var trackerFewFailures = new PinAttemptTracker(() => now);
        var trackerManyFailures = new PinAttemptTracker(() => now);

        for (var i = 0; i < 5; i++)
            trackerFewFailures.RecordFailure(); // 1st failure at threshold

        for (var i = 0; i < 7; i++)
            trackerManyFailures.RecordFailure(); // 3rd failure at/past threshold

        Assert.True(trackerManyFailures.RemainingLockout > trackerFewFailures.RemainingLockout);
    }

    [Fact]
    public void IsLockedOut_AfterCooldownElapses_BecomesFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new PinAttemptTracker(() => now);

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure();
        Assert.True(tracker.IsLockedOut);

        now = now.AddMinutes(31); // past the max cooldown ceiling
        Assert.False(tracker.IsLockedOut);
    }

    [Fact]
    public void Reset_ClearsFailureCountAndLockout()
    {
        var tracker = new PinAttemptTracker();
        for (var i = 0; i < 5; i++)
            tracker.RecordFailure();
        Assert.True(tracker.IsLockedOut);

        tracker.Reset();

        Assert.False(tracker.IsLockedOut);

        // Confirms the counter itself was cleared, not just the cooldown expiring:
        // 4 more failures post-reset should still be under threshold.
        for (var i = 0; i < 4; i++)
            tracker.RecordFailure();
        Assert.False(tracker.IsLockedOut);
    }
}
