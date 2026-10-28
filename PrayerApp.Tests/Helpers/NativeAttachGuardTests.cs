using PrayerApp.Helpers;

namespace PrayerApp.Tests.Helpers;

public class NativeAttachGuardTests
{
    [Fact]
    public void TryMark_FirstCallForInstance_ReturnsTrue()
    {
        var guard = new NativeAttachGuard();

        Assert.True(guard.TryMark(new object()));
    }

    [Fact]
    public void TryMark_SecondCallSameInstance_ReturnsFalse()
    {
        var guard = new NativeAttachGuard();
        var view = new object();
        guard.TryMark(view);

        Assert.False(guard.TryMark(view));
    }

    [Fact]
    public void TryMark_DifferentInstance_ReturnsTrue()
    {
        var guard = new NativeAttachGuard();
        guard.TryMark(new object());

        Assert.True(guard.TryMark(new object()));
    }
}
