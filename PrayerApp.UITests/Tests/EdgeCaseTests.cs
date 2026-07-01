using OpenQA.Selenium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 12: Edge Cases
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "12-EdgeCases")]
public class EdgeCaseTests
{
    private readonly AppiumSetup _setup;
    public EdgeCaseTests(AppiumSetup setup) => _setup = setup;

    /// <summary>12.4: Rapid tab switching — no crash, no stale data.</summary>
    [Fact]
    public void EdgeCase_RapidTabSwitching_NoCrash()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.EnsureOnTab("Home", _setup);

        var tabs = new[] { "Home", "Prayer Cards", "Prayers", "Tags", "Settings" };
        var delay = TestConfig.IsIOS ? 500 : 200; // iOS Shell navigation is slower
        foreach (var tab in tabs)
        {
            driver.NavigateToTab(tab);
            Thread.Sleep(delay);
        }

        for (int i = tabs.Length - 1; i >= 0; i--)
        {
            driver.NavigateToTab(tabs[i]);
            Thread.Sleep(delay);
        }

        driver.NavigateToTab("Home");
        Assert.True(driver.IsDisplayed("Home_Btn_QuickAdd"),
            "App should still be functional after rapid tab switching");
    }
}
