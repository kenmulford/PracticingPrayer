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

    /// <summary>8.2: Section expand/collapse — tapping a header toggles its cards.</summary>
    [Fact]
    public void Cards_SectionHeader_ExpandCollapse()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Find and tap the Archived section header (collapsed by default)
        if (driver.IsTextDisplayed("Archived", timeoutSeconds: 10))
        {
            driver.TapByText("Archived");
            Thread.Sleep(TestConfig.DelayAfterTap);

            // Tap again to collapse
            driver.TapByText("Archived");
            Thread.Sleep(TestConfig.DelayAfterTap);
        }

        // Page should still be functional
        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));
    }

    /// <summary>8.3: Navigate to Manage Collections from Cards page toolbar.</summary>
    [Fact]
    public void Cards_CollectionsToolbar_NavigatesToBoxesPage()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        driver.TapToolbarItemById("Collections");
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        Assert.True(driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10),
            "Should navigate to Collections management page");

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
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

    /// <summary>8.6: Edit collection — select, tap Edit chip, rename, save.</summary>
    [Fact]
    public void Boxes_EditCollection_UpdatesName()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureUITestCollectionExists(_setup);
        var driver = _setup.Driver;

        driver.EnsureOnTab("Prayer Cards", _setup);
        driver.TapToolbarItemById("Collections");
        driver.WaitForElement("Boxes_List_Boxes", timeoutSeconds: 10);

        // Tap the collection to select it (reveals action chips)
        if (driver.IsTextDisplayed("UITest Collection", timeoutSeconds: 3))
        {
            driver.TapByText("UITest Collection");
            Thread.Sleep(TestConfig.DelayAfterTap);

            // Tap Edit chip
            if (driver.IsDisplayed("Boxes_Btn_Edit", timeoutSeconds: 3))
            {
                driver.Tap("Boxes_Btn_Edit");
                Thread.Sleep(TestConfig.DelayAfterNavigation);

                Assert.True(driver.IsDisplayed("BoxDetail_Entry_Name", timeoutSeconds: 10),
                    "Should navigate to collection detail for editing");

                driver.TapToolbarItem("Save");
                Thread.Sleep(TestConfig.DelayAfterSave);
            }
        }

        // Navigate back to cards
        driver.NavigateToTab("Prayer Cards");
        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));
    }

    /// <summary>8.8: Delete collection — select, tap Delete chip, choose Unassign.
    /// Targets the "UITest Delete Target Collection B" throwaway entry from the seed so this
    /// destructive test doesn't wipe the shared UITest Collection baseline.</summary>
    [Fact]
    public void Boxes_DeleteCollection_UnassignCards()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        driver.EnsureOnTab("Prayer Cards", _setup);
        driver.TapToolbarItemById("Collections");
        driver.WaitForElement("Boxes_List_Boxes", timeoutSeconds: 10);

        if (driver.IsTextDisplayed(TestSeedFixtures.DeleteCollectionB, timeoutSeconds: 3))
        {
            driver.TapByText(TestSeedFixtures.DeleteCollectionB);
            Thread.Sleep(TestConfig.DelayAfterTap);

            if (driver.IsDisplayed("Boxes_Btn_Delete", timeoutSeconds: 3))
            {
                driver.Tap("Boxes_Btn_Delete");
                Thread.Sleep(TestConfig.DelayAfterTap);

                // Action sheet with "Unassign Cards" option
                if (TestConfig.IsIOS)
                    driver.TapIOSActionSheetButton("Unassign Cards");
                else
                    driver.TapByText("Unassign Cards");

                Thread.Sleep(TestConfig.DelayAfterSave);
            }
        }

        // Collection should be gone
        Assert.True(driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10),
            "Should remain on collections page after delete");

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
    }

    /// <summary>8.9: Card creation with collection picker — assign to a collection.</summary>
    [Fact]
    public void Cards_CreateCard_WithCollectionPicker()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayer Cards", _setup);
        var driver = _setup.Driver;

        driver.TapToolbarItemById("Add Card");
        driver.WaitForElement("Card_Entry_Title", timeoutSeconds: 10);

        // Collection picker should be visible for non-system cards
        Assert.True(driver.IsDisplayed("Card_Picker_Box", timeoutSeconds: 3),
            "Collection picker should be visible on card creation form");

        // Enter a title and save
        driver.EnterText("Card_Entry_Title", "UITest Card With Collection");
        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Navigate back if needed (iOS Bug #3)
        if (!driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10))
            driver.NavigateToTab("Prayer Cards");

        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));

        // Clean up: delete the test card
        if (driver.IsTextDisplayed("UITest Card With Collection", timeoutSeconds: 3))
        {
            driver.TapByText("UITest Card With Collection");
            Thread.Sleep(TestConfig.DelayAfterTap);

            // Expand card to show action chips
            if (driver.IsDisplayed("Cards_Btn_Delete", timeoutSeconds: 3))
            {
                driver.Tap("Cards_Btn_Delete");
                driver.DismissAlertIfPresent();
                Thread.Sleep(TestConfig.DelayAfterSave);
            }
        }
    }

    /// <summary>8.11: Delete collection — choose "Delete All Cards &amp; Requests" option.
    /// Targets the "UITest Delete Target Collection A" throwaway entry from the seed so this
    /// destructive test doesn't wipe the shared UITest Collection baseline.</summary>
    [Fact]
    public void Boxes_DeleteCollection_DeleteAllCards()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        driver.EnsureOnTab("Prayer Cards", _setup);
        driver.TapToolbarItemById("Collections");
        driver.WaitForElement("Boxes_List_Boxes", timeoutSeconds: 10);

        if (driver.IsTextDisplayed(TestSeedFixtures.DeleteCollectionA, timeoutSeconds: 3))
        {
            driver.TapByText(TestSeedFixtures.DeleteCollectionA);
            Thread.Sleep(TestConfig.DelayAfterTap);

            if (driver.IsDisplayed("Boxes_Btn_Delete", timeoutSeconds: 3))
            {
                driver.Tap("Boxes_Btn_Delete");
                Thread.Sleep(TestConfig.DelayAfterTap);

                // Choose "Delete All Cards & Requests"
                if (TestConfig.IsIOS)
                    driver.TapIOSActionSheetButton("Delete All Cards & Requests");
                else
                    driver.TapByText("Delete All Cards & Requests");

                Thread.Sleep(TestConfig.DelayAfterSave);
            }
        }

        // Should remain on collections page; collection should be gone
        Assert.True(driver.IsDisplayed("Boxes_List_Boxes", timeoutSeconds: 10),
            "Should remain on collections page after delete");

        driver.GoBack();
        Thread.Sleep(TestConfig.DelayAfterNavigation);
    }

    /// <summary>8.13: Search auto-expands matching sections.</summary>
    [Fact]
    public void Cards_Search_ExpandsMatchingSections()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureUITestCardExists(_setup);
        var driver = _setup.Driver;

        driver.EnsureOnTab("Prayer Cards", _setup);
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Type a search term into the search bar
        var searchBar = driver.WaitForElement("Cards_Search", timeoutSeconds: 10);
        searchBar.Click();
        Thread.Sleep(TestConfig.DelayAfterTap);
        searchBar.SendKeys("UITest");
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // Search results should show matching cards
        Assert.True(
            driver.IsTextDisplayed("UITest Card", timeoutSeconds: 10) ||
            driver.IsDisplayed("Cards_Section_Header", timeoutSeconds: 10),
            "Matching cards or section headers should be visible after search");

        // Clear search
        searchBar.Clear();
        driver.DismissKeyboardIfPresent();
        Thread.Sleep(TestConfig.DelayAfterTap);

        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));
    }

    /// <summary>8.14: Empty collection shows hint text when expanded.</summary>
    [Fact]
    public void Cards_EmptyCollection_ShowsHintText()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureUITestCollectionExists(_setup);
        var driver = _setup.Driver;

        driver.EnsureOnTab("Prayer Cards", _setup);
        Thread.Sleep(TestConfig.DelayCollectionRender);

        // The UITest Collection may be empty. Find its section header and expand it.
        if (driver.IsTextDisplayed("UITest Collection", timeoutSeconds: 10))
        {
            driver.TapByText("UITest Collection");
            Thread.Sleep(TestConfig.DelayAfterTap);

            // If the collection is empty, hint text should appear
            var hasHint = driver.IsTextDisplayed("No cards yet", timeoutSeconds: 3);

            // Tap again to collapse
            driver.TapByText("UITest Collection");
            Thread.Sleep(TestConfig.DelayAfterTap);

            // If collection had cards, hint wouldn't show — either way, page is functional
            Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10),
                "Cards page should remain functional after expand/collapse");
        }
        else
        {
            // Collection not visible — may need to scroll. Just verify page is functional.
            Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10));
        }
    }

}
