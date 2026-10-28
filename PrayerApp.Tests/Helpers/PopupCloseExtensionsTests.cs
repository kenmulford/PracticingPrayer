using PrayerApp.Helpers;

namespace PrayerApp.Tests.Helpers;

public class PopupCloseExtensionsTests
{
    [Fact]
    public async Task TryCloseAsync_DelegateThrowsInvalidOperation_ReturnsFalse()
    {
        var closed = await PopupCloseExtensions.TryCloseAsync(() => throw new InvalidOperationException());

        Assert.False(closed);
    }

    [Fact]
    public async Task TryCloseAsync_DelegateCompletes_ReturnsTrue()
    {
        var closed = await PopupCloseExtensions.TryCloseAsync(() => Task.CompletedTask);

        Assert.True(closed);
    }
}
