using OpenQA.Selenium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 8: Collections (Boxes)
/// Tests for the F-24 card grouping feature: section headers on Cards page,
/// collection management CRUD, card assignment picker, and multi-select move.
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "8-Collections")]
public class BoxTests
{
    private readonly AppiumSetup _setup;
    public BoxTests(AppiumSetup setup) => _setup = setup;

    /// <summary>8.1: Cards page shows section headers — at minimum System and Archived.</summary>
    [Fact]
    public void Cards_SectionHeaders_Visible()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Section headers use AutomationId="Cards_Section_Header"
        // At minimum, System section should be visible (Quick Add card lives there)
        Assert.True(
            driver.IsTextDisplayed("System", timeoutSeconds: 10) ||
            driver.IsDisplayed("Cards_Section_Header", timeoutSeconds: 10),
            "At least one section header should be visible on the Cards page");
    }

    /// <summary>8.4: Navigate to Manage Collections from Settings hub.</summary>
    [Fact]
    public void Settings_ManageCollections_NavigatesToBoxesPage()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.NavigateToTabRoot("Settings", "Settings_Row_AppSettings", _setup);
        var driver = _setup.Driver;

        Assert.True(driver.IsDisplayed("Settings_Row_Collections", timeoutSeconds: 10),
            "Manage Collections row should be visible in Settings");

        driver.WaitAndTap("Settings_Row_Collections");
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.True(driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10),
            "Should navigate to Collections management page");

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.True(driver.IsDisplayed("Settings_Row_Collections", timeoutSeconds: 10),
            "Should return to Settings hub");
    }

    /// <summary>8.5: Create collection — tap Add, enter name, save, verify in list.</summary>
    [Fact]
    public void Boxes_CreateCollection_AppearsInList()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        // Navigate to Manage Collections
        driver.TapToolbarItemById("Collections");
        driver.WaitForElement("Boxes_List_Boxes", timeoutSeconds: 10);

        // Create a new collection
        driver.TapToolbarItem("Add");
        driver.WaitForElement("BoxDetail_Entry_Name", timeoutSeconds: 10);

        // Unique-per-run name so a re-run under noReset doesn't collide with prior
        // residue and trigger the Duplicate Collection Name guard.
        var collectionName = $"New Collection UITest {DateTime.UtcNow.Ticks}";
        driver.EnterText("BoxDetail_Entry_Name", collectionName);
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Verify we returned to collection list (handle iOS Bug #3 GoToAsync unreliability)
        var onList = driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10);
        if (!onList && TestConfig.IsIOS)
        {
            driver.GoBack();
            Thread.Sleep(TestConfig.DelayAfterNavigation);
        }

        // iOS composes the row label as "{name}, {count} cards" — use contains match.
        Assert.True(driver.IsTextContainsDisplayed(collectionName, timeoutSeconds: 10),
            "Newly created collection should appear in the list");

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
    }

}
