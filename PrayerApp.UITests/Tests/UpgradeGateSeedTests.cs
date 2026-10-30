using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// Sets PIN 135790 and grants notification permission on the shipped 1.7.2 build for ios/scripts/upgrade-gate.sh.
/// Runs only when UPGRADE_GATE=1: the shared suite must not set a PIN (CLAUDE.md, E2E test isolation).
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "UpgradeGate")]
public class UpgradeGateSeedTests
{
    private readonly AppiumSetup _setup;
    public UpgradeGateSeedTests(AppiumSetup setup) => _setup = setup;

    [SkippableFact]
    public void UpgradeGate_SetPinAndGrantNotifications_OnShippedBuild()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("UPGRADE_GATE") == "1",
            "Upgrade gate only: set UPGRADE_GATE=1 (ios/scripts/upgrade-gate.sh)");

        var driver = _setup.Driver;
        driver.ResetAppUIState(_setup);
        driver.EnsureOnTab("Home", _setup);
        driver.EnsureOnTab("Prayer Cards", _setup);
        driver.WaitAndTap("More");
        driver.WaitAndTap("Cards_Btn_ShowConfidential");
        DismissSystemPasscodeSheet(driver);
        driver.WaitForElement("PinPrompt_Entry_Pin");
        driver.EnterText("PinPrompt_Entry_Pin", "135790");
        driver.WaitAndTap("PinPrompt_Btn_Confirm");
        driver.WaitForElementGone("PinPrompt_Entry_Pin");

        GrantNotificationPermission(driver);

        driver.EnsureOnTab("Home", _setup);
    }

    // The seed writes reminders straight into the database, so nothing else asks iOS for permission. The preference
    // defaults to on, so the toggle is cycled off and on: switching it on is what makes 1.7.2 request authorization.
    private void GrantNotificationPermission(OpenQA.Selenium.Appium.AppiumDriver driver)
    {
        driver.EnsureOnTab("Settings", _setup);
        driver.WaitAndTap("Settings_Row_AppSettings");
        driver.WaitForElement("AppSettings_Switch_Notifications", timeoutSeconds: 10);
        var toggle = driver.FindByAutomationId("AppSettings_Switch_Notifications");
        if (toggle.GetAttribute("value") == "1")
        {
            toggle.Click();
            Thread.Sleep(TestConfig.DelayAfterTap);
        }
        toggle.Click();

        // A re-run on a simulator that kept the earlier grant shows no alert; 1.7.2 turns the toggle off on a denial,
        // so the toggle staying on is the grant check in both cases.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!driver.IsAlertPresent() && DateTime.UtcNow < deadline) Thread.Sleep(TestConfig.DelayShortSettle);
        if (driver.IsAlertPresent())
        {
            driver.SwitchTo().Alert().Accept();
            Thread.Sleep(TestConfig.DelayAfterDismiss);
        }
        Assert.Equal("1", driver.FindByAutomationId("AppSettings_Switch_Notifications").GetAttribute("value"));
    }

    // The simulator shows the system "Enter iPad Passcode" sheet for the biometric attempt before the
    // app falls back to its PIN prompt. The sheet is outside the app's accessibility tree, so its close
    // button is tapped by screen point (the "Test iPad" iPad (A16) is 820x1180 pt; the button sits at 191,328).
    private static void DismissSystemPasscodeSheet(OpenQA.Selenium.Appium.AppiumDriver driver)
    {
        if (driver.IsDisplayed("PinPrompt_Entry_Pin", timeoutSeconds: 8)) return;
        driver.ExecuteScript("mobile: tap", new Dictionary<string, object> { { "x", 191 }, { "y", 328 } });
        Thread.Sleep(TestConfig.DelayModalAnimation);
    }
}
