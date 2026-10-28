using PrayerApp.Helpers;

namespace PrayerApp.Tests.Helpers;

public class PopupResultGateTests
{
    [Fact]
    public void TryBegin_FirstCall_ReturnsTrue()
    {
        Assert.True(new PopupResultGate().TryBegin());
    }

    [Fact]
    public void TryBegin_SecondCall_ReturnsFalse()
    {
        var gate = new PopupResultGate();
        gate.TryBegin();

        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void TryBegin_AfterReset_ReturnsTrue()
    {
        var gate = new PopupResultGate();
        gate.TryBegin();
        gate.Reset();

        Assert.True(gate.TryBegin());
    }
}
