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

    [Fact]
    public async Task CompleteAsync_CloseSucceeds_KeepsResultAndBlocksSecondCall()
    {
        var gate = new PopupResultGate();
        string? result = null;

        await gate.CompleteAsync(() => Task.FromResult(true), () => result = "first", () => result = null);
        await gate.CompleteAsync(() => Task.FromResult(true), () => result = "second", () => result = null);

        Assert.Equal("first", result);
    }

    [Fact]
    public async Task CompleteAsync_CloseRefused_ClearsResultAndRearmsGate()
    {
        var gate = new PopupResultGate();
        string? result = null;

        await gate.CompleteAsync(() => Task.FromResult(false), () => result = "first", () => result = null);

        Assert.Null(result);
        Assert.True(gate.TryBegin());
    }
}
