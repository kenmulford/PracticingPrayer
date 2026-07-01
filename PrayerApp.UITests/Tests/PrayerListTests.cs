using OpenQA.Selenium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 4: Prayers Tab
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "4-Prayers")]
public class PrayerListTests
{
    private readonly AppiumSetup _setup;
    public PrayerListTests(AppiumSetup setup) => _setup = setup;

    /// <summary>4.5: Add new prayer via toolbar "Add" button.</summary>
    [Fact]
    public void Prayers_AddNewPrayer()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.NavigateToNewPrayer(_setup);
        var driver = _setup.Driver;

        driver.EnterText("Detail_Entry_Title", "Prayer List UITest");
        driver.TapToolbarItemById("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Save navigates back to list automatically
        Assert.True(driver.IsDisplayed("List_Filter_Active", timeoutSeconds: 10)
                 || driver.IsDisplayed("List_List_Prayers", timeoutSeconds: 3));
    }
}
