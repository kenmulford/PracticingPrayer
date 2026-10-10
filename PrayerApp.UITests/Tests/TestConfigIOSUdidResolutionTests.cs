using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// Issue #330: unit coverage for resolving the iOS simulator UDID from
/// <c>IOS_UDID</c>, else <c>IOS_SIMULATOR</c> + <c>IOS_VERSION</c> via <c>simctl list devices -j</c>.
/// Runs with no simulator or Appium; the simctl output is an inline fixture.
/// </summary>
[Trait("Category", "Unit")]
public class TestConfigIOSUdidResolutionTests
{
    private const string OneMatchJson = """
        {
          "devices": {
            "com.apple.CoreSimulator.SimRuntime.iOS-27-0": [
              { "name": "Test iPhone", "udid": "IPHONE-27", "isAvailable": true },
              { "name": "Test iPad", "udid": "IPAD-27", "isAvailable": true }
            ]
          }
        }
        """;

    private const string SeveralMatchesJson = """
        {
          "devices": {
            "com.apple.CoreSimulator.SimRuntime.iOS-27-0": [
              { "name": "Test iPad", "udid": "IPAD-A", "isAvailable": true },
              { "name": "Test iPad", "udid": "IPAD-B", "isAvailable": true }
            ]
          }
        }
        """;

    private const string UnavailableTwinJson = """
        {
          "devices": {
            "com.apple.CoreSimulator.SimRuntime.iOS-27-0": [
              { "name": "Test iPad", "udid": "IPAD-LIVE", "isAvailable": true },
              { "name": "Test iPad", "udid": "IPAD-DEAD", "isAvailable": false }
            ]
          }
        }
        """;

    private const string OtherRuntimeJson = """
        {
          "devices": {
            "com.apple.CoreSimulator.SimRuntime.iOS-26-0": [
              { "name": "Test iPad", "udid": "IPAD-26", "isAvailable": true }
            ]
          }
        }
        """;

    private static string ThrowingReader() =>
        throw new InvalidOperationException("simctl must not be listed when IOS_UDID is set");

    [Fact]
    public void ResolveIOSUdid_OverrideSet_ReturnsTrimmedWithoutListingDevices()
    {
        var udid = TestConfig.ResolveIOSUdid("  ABC-123  ", "Test iPad", "27.0", ThrowingReader);

        Assert.Equal("ABC-123", udid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveIOSUdid_BlankOverride_ResolvesFromSimctl(string? udidOverride)
    {
        var udid = TestConfig.ResolveIOSUdid(udidOverride, "Test iPad", "27.0", () => OneMatchJson);

        Assert.Equal("IPAD-27", udid);
    }

    [Fact]
    public void ResolveIOSUdid_OneMatch_ReturnsIt()
    {
        var udid = TestConfig.ResolveIOSUdid(null, "Test iPad", "27.0", () => OneMatchJson);

        Assert.Equal("IPAD-27", udid);
    }

    [Fact]
    public void ResolveIOSUdid_ZeroMatches_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestConfig.ResolveIOSUdid(null, "Missing iPad", "27.0", () => OneMatchJson));

        Assert.Contains("IOS_SIMULATOR", ex.Message);
        Assert.Contains("IOS_VERSION", ex.Message);
        Assert.Contains("IOS_UDID", ex.Message);
    }

    [Fact]
    public void ResolveIOSUdid_SeveralMatches_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestConfig.ResolveIOSUdid(null, "Test iPad", "27.0", () => SeveralMatchesJson));

        Assert.Contains("IPAD-A", ex.Message);
        Assert.Contains("IPAD-B", ex.Message);
        Assert.Contains("IOS_UDID", ex.Message);
    }

    [Fact]
    public void MatchSimulatorUdids_UnavailableDevice_Excluded()
    {
        var udids = TestConfig.MatchSimulatorUdids(UnavailableTwinJson, "Test iPad", "27.0");

        Assert.Equal(new[] { "IPAD-LIVE" }, udids);
    }

    [Fact]
    public void MatchSimulatorUdids_OtherRuntime_Excluded()
    {
        var udids = TestConfig.MatchSimulatorUdids(OtherRuntimeJson, "Test iPad", "27.0");

        Assert.Empty(udids);
    }
}
