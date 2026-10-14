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

    /// <summary>
    /// Issue #338: a One-time reminder takes a date, not just a time. Picks next year on the date
    /// picker's year wheel (future in any month) and checks the read view shows "Once on ...".
    /// </summary>
    [SkippableFact]
    public void OneTimeReminder_FutureDate_ShowsOnceOnInReadView()
    {
        Skip.IfNot(TestConfig.IsIOS, "Date wheel selection is iOS-only (UIDatePicker wheels).");
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.EnsureOnTab("Prayers", _setup);
        driver.WaitForElement("List_List_Prayers", timeoutSeconds: 10);

        driver.ScrollToPrayerAndTap(TestSeedFixtures.ReminderDatePrayer);
        driver.TapToolbarItemById("Edit");
        driver.WaitForElement("Detail_Entry_Title", timeoutSeconds: 10);

        driver.ScrollDownTo("Detail_Switch_Reminders");
        driver.WaitAndTap("Detail_Switch_Reminders");
        driver.ScrollDownTo("Detail_Picker_Frequency");
        driver.WaitAndTap("Detail_Picker_Frequency");
        driver.SelectIOSPickerValue("OneTime");
        driver.CloseIOSPicker();
        Assert.Equal("OneTime", driver.IOSPickerFieldValue("Detail_Picker_Frequency"));

        var nextYear = (DateTime.Now.Year + 1).ToString();
        driver.ScrollDownTo("Detail_Picker_ReminderDate");
        driver.WaitAndTap("Detail_Picker_ReminderDate");
        driver.SelectIOSPickerWheelValue(3, nextYear);
        driver.CloseIOSPicker();
        Assert.EndsWith(nextYear, driver.IOSPickerFieldValue("Detail_Picker_ReminderDate"));

        // Save pops back to the Prayers list; the read view is reopened from there.
        driver.TapToolbarItemById("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);
        Assert.True(driver.IsDisplayed("List_List_Prayers", timeoutSeconds: 10), "Save did not return to the Prayers list.");

        driver.ScrollToPrayerAndTap(TestSeedFixtures.ReminderDatePrayer);
        Assert.NotNull(driver.FindByTextContains($"Once on {DateTime.Today.AddYears(1):d MMM yyyy}"));
    }
}
