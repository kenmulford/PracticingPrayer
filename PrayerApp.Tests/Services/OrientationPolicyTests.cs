using Microsoft.Maui.Devices;
using PrayerApp.Services;
using Xunit;

namespace PrayerApp.Tests.Services;

public class OrientationPolicyTests
{
    [Fact]
    public void AllowsRotation_Tablet_ReturnsTrue()
    {
        Assert.True(OrientationPolicy.AllowsRotation(DeviceIdiom.Tablet));
    }

    [Fact]
    public void AllowsRotation_Phone_ReturnsFalse()
    {
        Assert.False(OrientationPolicy.AllowsRotation(DeviceIdiom.Phone));
    }

    public static IEnumerable<object[]> OtherIdioms() => new[]
    {
        new object[] { DeviceIdiom.Desktop },
        new object[] { DeviceIdiom.TV },
        new object[] { DeviceIdiom.Watch },
        new object[] { DeviceIdiom.Unknown },
    };

    [Theory]
    [MemberData(nameof(OtherIdioms))]
    public void AllowsRotation_OtherIdiom_ReturnsFalse(DeviceIdiom idiom)
    {
        Assert.False(OrientationPolicy.AllowsRotation(idiom));
    }
}
