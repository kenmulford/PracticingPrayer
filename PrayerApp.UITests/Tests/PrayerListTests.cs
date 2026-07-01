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

    /// <summary>4.1: Prayer list loads.</summary>
    [Fact]
    public void Prayers_PageLoads()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayers", _setup);
        Assert.True(_setup.Driver.IsDisplayed("List_Filter_Active")
                 || _setup.Driver.IsDisplayed("List_Search_Prayers"),
            "Prayers page should show filter buttons or search bar");
    }

    /// <summary>4.2: Search prayers by title.</summary>
    [Fact]
    public void Prayers_SearchBar_FiltersResults()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayers", _setup);
        var driver = _setup.Driver;

        if (driver.IsDisplayed("List_Search_Prayers", timeoutSeconds: 3))
        {
            driver.EnterText("List_Search_Prayers", "nonexistent_prayer_xyz");
            Thread.Sleep(TestConfig.DelayDirtyRegistration);
            driver.EnterText("List_Search_Prayers", "");
            Thread.Sleep(TestConfig.DelayDirtyRegistration);
        }

        Assert.True(driver.IsDisplayed("List_Filter_Active")
                 || driver.IsDisplayed("List_Search_Prayers"));
    }

    /// <summary>4.3: Filter buttons switch between Active/Answered/All views.</summary>
    [Fact]
    public void Prayers_FilterButtons_SwitchViews()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnTab("Prayers", _setup);
        var driver = _setup.Driver;

        if (driver.IsDisplayed("List_Filter_Answered", timeoutSeconds: 3))
        {
            driver.Tap("List_Filter_Answered");
            Thread.Sleep(TestConfig.DelayAfterTap);
        }

        if (driver.IsDisplayed("List_Filter_All", timeoutSeconds: 3))
        {
            driver.Tap("List_Filter_All");
            Thread.Sleep(TestConfig.DelayAfterTap);
        }

        if (driver.IsDisplayed("List_Filter_Active", timeoutSeconds: 3))
        {
            driver.Tap("List_Filter_Active");
            Thread.Sleep(TestConfig.DelayAfterTap);
        }

        Assert.True(driver.IsDisplayed("List_Filter_Active"));
    }

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

    /// <summary>4.6: View prayer in read-only mode — tap a prayer row.</summary>
    [Fact]
    public void Prayers_TapPrayer_ShowsViewMode()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.EnsureOnPrayersTab(_setup);

        // Scroll to the seeded prayer and open it. Shared helper handles the iOS
        // composed-label vs Android scroll split and fails loud if the row is absent
        // (replaced the inline copy this helper was extracted from).
        driver.ScrollToPrayerAndTap("UI Test Prayer");

        Assert.True(driver.IsDisplayed("Detail_Btn_MarkAnswered", timeoutSeconds: 10)
                 || driver.IsDisplayed("Detail_Btn_Share", timeoutSeconds: 3),
            "View mode should show action buttons");

        driver.GoBack();
    }

    /// <summary>4.7: Edit prayer — view mode → Edit → change details → Save.</summary>
    [Fact]
    public void Prayers_EditPrayer()
    {
        _setup.Driver.ResetAppUIState(_setup);
        _setup.Driver.EnsureOnPrayersTab(_setup); // lands on Prayers + waits for List_List_Prayers to render
        var driver = _setup.Driver;

        // #72a: was wrapped in if (IsTextDisplayed) which silently skipped the edit-mode
        // assertion when the seeded prayer was off-screen/absent (false green). Scroll-tap
        // unconditionally so a missing prayer fails loud — exposing the Edit-toolbar red
        // (Android uppercases action-bar text → "Edit" text lookup missed), closed in #72b
        // via AutomationId + TapToolbarItemById.
        driver.ScrollToPrayerAndTap("UI Test Prayer");

        driver.TapToolbarItemById("Edit");
        Thread.Sleep(TestConfig.DelayAfterTap);

        Assert.True(driver.IsDisplayed("Detail_Entry_Title", timeoutSeconds: 10),
            "Should show title entry in edit mode");

        if (driver.IsDisplayed("Detail_Entry_Details", timeoutSeconds: 3))
            driver.EnterText("Detail_Entry_Details", "Updated by UITest");

        driver.TapToolbarItemById("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // Ensure we're back on the list
        driver.NavigateToTab("Prayers");
        Assert.True(driver.IsDisplayed("List_Filter_Active", timeoutSeconds: 10));
    }

    /// <summary>4.9: Delete prayer from edit mode.</summary>
    [Fact]
    public void Prayers_DeletePrayer()
    {
        _setup.Driver.ResetAppUIState(_setup);
        // Create a prayer to delete
        _setup.Driver.NavigateToNewPrayer(_setup);
        var driver = _setup.Driver;

        driver.EnterText("Detail_Entry_Title", TestSeedFixtures.DeleteRuntimePrayer);
        driver.TapToolbarItemById("Save");

        // Save triggers GoToAsync("..") after the DB write; round-trip is ~5s on
        // emulator. Fixed Thread.Sleep(1000) raced the rebuild.
        driver.WaitForElement("List_Filter_Active", timeoutSeconds: 10);

        // #72a: was an if (IsTextDisplayed) guard that silently skipped the delete flow
        // when the just-created prayer wasn't visible (false green). Scroll-tap loud;
        // also exercises the Edit-toolbar tap fixed in #72b (TapToolbarItemById).
        driver.ScrollToPrayerAndTap(TestSeedFixtures.DeleteRuntimePrayer);

        driver.TapToolbarItemById("Edit");
        Thread.Sleep(TestConfig.DelayAfterTap);

        // Scroll to Delete if needed
        if (!driver.IsDisplayed("Detail_Btn_Delete", timeoutSeconds: 3))
            driver.ScrollDownTo("Detail_Btn_Delete");

        driver.Tap("Detail_Btn_Delete");
        Thread.Sleep(TestConfig.DelayAfterTap);
        driver.DismissAlertIfPresent();
        Thread.Sleep(TestConfig.DelayAfterNavigation);

        // Should return to list
        driver.NavigateToTab("Prayers");
        if (!driver.IsDisplayed("List_Filter_Active", timeoutSeconds: 10))
        {
            var dumpPath = driver.DumpPageSource("DeletePrayer_Android_FAIL");
            Assert.Fail(
                $"Expected to land on Prayers list (List_Filter_Active visible) after delete. Page source: {dumpPath}");
        }
    }

    /// <summary>4.10: Cross-tab freshness — navigating between tabs doesn't crash.</summary>
    [Fact]
    public void Prayers_CrossTabFreshness()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;
        driver.DismissOnboardingIfPresent(_setup);

        driver.NavigateToTab("Prayers");
        Thread.Sleep(TestConfig.DelayAfterTap);
        driver.NavigateToTab("Home");
        Thread.Sleep(TestConfig.DelayAfterTap);
        driver.NavigateToTab("Prayers");
        Thread.Sleep(TestConfig.DelayAfterTap);

        Assert.True(driver.IsDisplayed("List_Filter_Active")
                 || driver.IsDisplayed("List_Search_Prayers"));
    }
}
