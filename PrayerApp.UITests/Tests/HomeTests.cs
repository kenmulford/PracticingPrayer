using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 2: Home Tab
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "2-Home")]
public class HomeTests
{
    private readonly AppiumSetup _setup;
    public HomeTests(AppiumSetup setup) => _setup = setup;

    /// <summary>2.3: Tap Active Cards metric — navigates to Prayer Cards tab.</summary>
    [Fact]
    public void Home_TapActiveCards_NavigatesToCardsTab()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Home", _setup);
        var driver = _setup.Driver;

        driver.WaitAndTap("Home_Metric_Cards", timeoutSeconds: 10);
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.True(
            driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10)
            || driver.IsDisplayed("Cards_Search", timeoutSeconds: 3),
            "Should navigate to Prayer Cards tab after tapping Active Cards metric");

        // Return to Home for subsequent tests
        driver.NavigateToTab("Home");
    }
}
