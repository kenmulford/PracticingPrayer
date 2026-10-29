using PrayerApp.UITests.Helpers;
using PrayerApp.UITests.Infrastructure;
using Xunit;

namespace PrayerApp.UITests.Tests;

/// <summary>
/// Sets PIN 135790 on the shipped 1.7.2 build for ios/scripts/upgrade-gate.sh. Runs only when
/// UPGRADE_GATE=1: the shared suite must not set a PIN (CLAUDE.md, E2E test isolation).
/// </summary>
[Collection("Appium")]
[Trait("Platform", "CrossPlatform")]
[Trait("Section", "UpgradeGate")]
public class UpgradeGateSeedTests
{
    private readonly AppiumSetup _setup;
    public UpgradeGateSeedTests(AppiumSetup setup) => _setup = setup;

    [SkippableFact]
    public void UpgradeGate_SetPin_OnShippedBuild()
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

        driver.EnsureOnTab("Home", _setup);
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
