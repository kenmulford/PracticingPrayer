using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// UAT Section 2.2-2.3: Quick Add flow (from Home tab).
///
/// The bespoke QuickAddPage was retired in issue #43. Home_Btn_QuickAdd now
/// opens ConfirmImportPage in Manual mode: page title "Quick Add",
/// ExistingCard mode preselected, Quick Add card pre-selected (collapsed to
/// summary), one empty prayer row ready to type.
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "2-Home")]
public class QuickAddTests
{
    private readonly AppiumSetup _setup;
    public QuickAddTests(AppiumSetup setup) => _setup = setup;

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Navigate to Home and open the Quick Add flow via Home_Btn_QuickAdd.
    /// Waits for ConfirmImport_Seg_ExistingCard (a body Border element, reliably
    /// located on both iOS and Android) as the page-ready probe.
    /// ToolbarItems are not locatable by AutomationId on Android, so
    /// ConfirmImport_Btn_Save cannot be used as a probe here.
    /// </summary>
    private void OpenQuickAdd()
    {
        var driver = _setup.Driver;
        driver.EnsureOnTab("Home", _setup);
        driver.WaitAndTap("Home_Btn_QuickAdd");
        driver.WaitForElement("ConfirmImport_Seg_ExistingCard", timeoutSeconds: 15);
        Thread.Sleep(TestConfig.DelayModalAnimation);
    }

    /// <summary>
    /// Enter text into the first (and, in Manual mode, only) prayer-title Entry
    /// inside ConfirmImport_List_Prayers. The Entry has no AutomationId; it is
    /// located via XPath on the list container, mirroring ImportFlowTests.
    ///
    /// iOS: XCUIElementTypeTextField; Android: android.widget.EditText.
    /// </summary>
    private static void EnterPrayerTitle(AppiumDriver driver, string text)
    {
        var prayersList = driver.WaitForElement("ConfirmImport_List_Prayers", timeoutSeconds: 10);

        var entryXPath = TestConfig.IsIOS
            ? ".//XCUIElementTypeTextField"
            : ".//android.widget.EditText[@hint='Prayer title' or contains(@content-desc,'Prayer title')]";

        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        IReadOnlyCollection<OpenQA.Selenium.IWebElement> entries;
        try
        {
            entries = prayersList.FindElements(By.XPath(entryXPath));
        }
        finally
        {
            driver.Manage().Timeouts().ImplicitWait = TestConfig.DefaultTimeout;
        }

        Assert.True(entries.Count > 0,
            $"Expected at least one prayer-title Entry in ConfirmImport_List_Prayers (XPath: {entryXPath})");

        var entry = entries.First();
        entry.Click();
        Thread.Sleep(TestConfig.DelayAfterTap);
        entry.SendKeys(text);
        driver.DismissKeyboardIfPresent();
    }

    // ── Tests ────────────────────────────────────────────────────

    /// <summary>2.3: Quick Add → Cards tab cross-tab nav — saving from the Quick Add
    /// flow lands on the Prayer Cards tab. The deeper "the saved prayer row materializes
    /// in the virtualized list" assertion was dropped in issue #169 (ConfirmImport save +
    /// the resulting row rendering are covered by unit tests); this test now guards only
    /// the post-save cross-tab navigation edge.</summary>
    [Fact]
    public void QuickAdd_PrayerAppearsOnCardsTab()
    {
        _setup.Driver.ResetAppUIState(_setup);
        var driver = _setup.Driver;

        OpenQuickAdd();

        var uniqueTitle = $"CrossTab UITest {DateTime.Now:HHmmss}";
        EnterPrayerTitle(driver, uniqueTitle);

        driver.TapToolbarItem("Save");
        Thread.Sleep(TestConfig.DelayAfterSave);

        // After save, ConfirmImport navigates to the Prayer Cards tab.
        Assert.True(driver.IsDisplayed("Cards_List_Cards", timeoutSeconds: 10),
            "Cards tab should be visible after Quick Add save");
    }
}
