using System.Runtime.InteropServices;
using OpenQA.Selenium.Appium;
using PrayerApp.Helpers;

namespace PrayerApp.UITests.Infrastructure;

/// <summary>
/// Platform-aware configuration for Appium test sessions.
/// Auto-detects Windows (Android) vs macOS (iOS) and builds
/// the appropriate AppiumOptions.
/// </summary>
public static class TestConfig
{
    // Optional explicit target-platform override. Lets one host run UITests for a
    // platform other than its OS default — e.g. drive the Android suite from macOS.
    // Declared BEFORE the IsAndroid/IsIOS flags because static readonly fields
    // initialise top-to-bottom, and those flags read this. When UITEST_PLATFORM is
    // unset this stays null and the flags fall back to the host OS, byte-for-byte
    // unchanged from before the override existed.
    private static readonly string? PlatformOverride =
        Environment.GetEnvironmentVariable("UITEST_PLATFORM");

    private static readonly (bool isAndroid, bool isIOS) Platform = ResolvePlatform(
        PlatformOverride,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

    public static readonly bool IsAndroid = Platform.isAndroid;
    public static readonly bool IsIOS = Platform.isIOS;

    /// <summary>
    /// Pure platform-resolution decision, extracted so it is unit-testable without the
    /// process-once <c>static readonly</c> init. An explicit <paramref name="overrideValue"/>
    /// (<c>UITEST_PLATFORM</c> = "android" | "ios", case/whitespace-insensitive) forces the
    /// target platform; when null or blank (empty/whitespace-only) it falls back to the host
    /// OS (Windows → Android, macOS → iOS), preserving the original behaviour exactly. An unrecognised override
    /// with no host match returns <c>(false, false)</c> so <see cref="GetOptions"/> throws
    /// <see cref="PlatformNotSupportedException"/> (fail-loud) rather than guessing.
    /// </summary>
    internal static (bool isAndroid, bool isIOS) ResolvePlatform(
        string? overrideValue, bool hostIsWindows, bool hostIsOSX)
    {
        // Coalesce blank (unset, empty, or whitespace-only) to null so a declared-but-empty
        // UITEST_PLATFORM (`UITEST_PLATFORM=` or a blank CI variable) takes the same host-OS
        // fallback as a genuinely-unset var — not the (false,false) fail-loud path.
        var normalized = string.IsNullOrWhiteSpace(overrideValue) ? null : overrideValue.Trim().ToLowerInvariant();
        bool isAndroid = normalized is "android" || (normalized is null && hostIsWindows);
        bool isIOS = normalized is "ios" || (normalized is null && hostIsOSX);
        return (isAndroid, isIOS);
    }

    public static readonly Uri AppiumServerUri = new(
        Environment.GetEnvironmentVariable("APPIUM_SERVER_URL") ?? "http://127.0.0.1:4723");

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan IOSSessionTimeout = TimeSpan.FromSeconds(240);

    // ── Named delay constants (milliseconds) ─────────────────────
    // Centralised so values can be tuned per-platform in one place.

    /// <summary>UI settle after tap/click.</summary>
    public const int DelayAfterTap = 300;
    /// <summary>Navigation after a save completes.</summary>
    public const int DelayAfterSave = 1000;
    /// <summary>Shell tab / page transition.</summary>
    public const int DelayAfterNavigation = 500;
    /// <summary>Alert / modal dismiss animation.</summary>
    public const int DelayAfterDismiss = 300;
    /// <summary>IsDirty property-change propagation.</summary>
    public const int DelayDirtyRegistration = 500;
    /// <summary>Full app restart in RecreateDriver.</summary>
    public const int DelayAppRelaunch = 5000;
    /// <summary>CollectionView item materialisation.</summary>
    public const int DelayCollectionRender = 1500;
    /// <summary>Modal/action sheet present or dismiss animation.</summary>
    public const int DelayModalAnimation = 1000;
    /// <summary>Short post-action settle that doesn't fit other categories.</summary>
    public const int DelayShortSettle = 200;
    /// <summary>Longer settle for heavyweight transitions.</summary>
    public const int DelayLongSettle = 2000;

    /// <summary>The Android app package name (from csproj ApplicationId).</summary>
    public const string AndroidPackage = "com.multithreadedllc.prayercards";

    /// <summary>The Android MainActivity class name (MAUI emits a CRC-prefixed Java class).
    /// Sourced from <see cref="AndroidComponentNames.MainActivity"/> so the production-side
    /// DebugProcessTextShim and this test-side constant cannot drift apart.</summary>
    public const string AndroidMainActivity = AndroidComponentNames.MainActivity;

    /// <summary>App-data-relative path to the SQLite DB on Android. Used by TestDataSeed.</summary>
    public const string AndroidAppDbRelativePath = "files/prayer_app.db";

    /// <summary>Staging path on Android used when pushing a seed DB (writable by adb).</summary>
    public const string AndroidTmpSeedPath = "/data/local/tmp/prayer_app_seed.db";

    /// <summary>UiAutomator2 instrumentation server package Appium installs on the device.
    /// Uninstalling it (with <see cref="UiAutomator2ServerTestPackage"/>) forces Appium to
    /// redeploy fresh instrumentation on the next session — the #191/#192 wedge recovery.
    /// Fixed package id from the appium-uiautomator2-driver; NOT the app under test
    /// (<see cref="AndroidPackage"/>), so uninstalling it does not touch the MAUI APK.</summary>
    public const string UiAutomator2ServerPackage = "io.appium.uiautomator2.server";

    /// <summary>UiAutomator2 instrumentation *test* server package (the companion to
    /// <see cref="UiAutomator2ServerPackage"/>). Appium reinstalls both on the next connect.</summary>
    public const string UiAutomator2ServerTestPackage = "io.appium.uiautomator2.server.test";

    /// <summary>The iOS bundle identifier.</summary>
    public const string IOSBundleId = "com.multithreadedllc.prayercards";

    /// <summary>App-container-relative path to the SQLite DB on iOS. Used by TestDataSeed.</summary>
    /// <remarks>
    /// MAUI's <c>FileSystem.AppDataDirectory</c> maps to <c>Library/</c> on iOS
    /// (not <c>Documents/</c>).
    /// </remarks>
    public const string IOSAppDbRelativePath = "Library/prayer_app.db";

    // Lazy, not static readonly: type init would run simctl on Android hosts too.
    private static readonly Lazy<string> IOSUdidLazy = new(() => ResolveIOSUdid(
        Environment.GetEnvironmentVariable("IOS_UDID"),
        Environment.GetEnvironmentVariable("IOS_SIMULATOR") ?? "Test iPad",
        Environment.GetEnvironmentVariable("IOS_VERSION") ?? "27.0",
        ReadSimctlDevicesJson));

    /// <summary>UDID of the target iOS simulator, resolved once per process from <c>IOS_UDID</c>, else the one available <c>IOS_SIMULATOR</c> on <c>IOS_VERSION</c>.</summary>
    public static string IOSUdid => IOSUdidLazy.Value;

    internal static string ResolveIOSUdid(
        string? udidOverride, string simulatorName, string iosVersion, Func<string> readSimctlDevicesJson)
    {
        if (!string.IsNullOrWhiteSpace(udidOverride))
            return udidOverride.Trim();

        var udids = MatchSimulatorUdids(readSimctlDevicesJson(), simulatorName, iosVersion);
        return udids.Count switch
        {
            1 => udids[0],
            0 => throw new InvalidOperationException(
                $"No available iOS simulator named '{simulatorName}' on iOS {iosVersion} (IOS_SIMULATOR / IOS_VERSION). " +
                "List them with 'xcrun simctl list devices available', fix IOS_SIMULATOR / IOS_VERSION, or set IOS_UDID to override."),
            _ => throw new InvalidOperationException(
                $"Several available iOS simulators named '{simulatorName}' on iOS {iosVersion} (IOS_SIMULATOR / IOS_VERSION): " +
                $"{string.Join(", ", udids)}. Rename one, or set IOS_UDID to one of them."),
        };
    }

    internal static IReadOnlyList<string> MatchSimulatorUdids(
        string simctlDevicesJson, string simulatorName, string iosVersion)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(simctlDevicesJson);
        var runtimeKey = "com.apple.CoreSimulator.SimRuntime.iOS-" + iosVersion.Replace('.', '-');
        if (!doc.RootElement.GetProperty("devices").TryGetProperty(runtimeKey, out var devices))
            return [];

        return devices.EnumerateArray()
            .Where(d => string.Equals(d.GetProperty("name").GetString(), simulatorName, StringComparison.Ordinal)
                && d.GetProperty("isAvailable").GetBoolean())
            .Select(d => d.GetProperty("udid").GetString()!)
            .ToList();
    }

    private static string ReadSimctlDevicesJson()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("xcrun", "simctl list devices -j")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start xcrun simctl. Are Xcode CLI tools installed?");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"'xcrun simctl list devices -j' failed: exit={proc.ExitCode}, stderr='{stderr.Trim()}'.");

        return stdout;
    }

    public static AppiumOptions GetOptions()
    {
        if (IsAndroid)
            return GetAndroidOptions();
        if (IsIOS)
            return GetIOSOptions();

        throw new PlatformNotSupportedException(
            "UITests need a target platform: run on Windows (→ Android) or macOS (→ iOS), " +
            "or set UITEST_PLATFORM to 'android' or 'ios' to override the host default. " +
            $"UITEST_PLATFORM was '{PlatformOverride}'.");
    }

    private static AppiumOptions GetAndroidOptions()
    {
        var options = new AppiumOptions();
        options.PlatformName = "Android";
        options.AutomationName = "UiAutomator2";

        // Prefer appPackage/appActivity (app must be pre-installed via adb).
        // This avoids Appium's Java-dependent APK signature verification.
        // Fall back to APK path if PRAYER_APK_PATH is explicitly set.
        var apkPath = Environment.GetEnvironmentVariable("PRAYER_APK_PATH");
        if (!string.IsNullOrEmpty(apkPath))
        {
            options.App = apkPath;
        }
        else
        {
            options.AddAdditionalAppiumOption("appPackage", AndroidPackage);
            options.AddAdditionalAppiumOption("appActivity", AndroidMainActivity);
            options.AddAdditionalAppiumOption("noReset", true);
        }

        options.DeviceName = Environment.GetEnvironmentVariable("ANDROID_AVD") ?? "pixel_9_-_api_36_0";
        options.AddAdditionalAppiumOption("appWaitActivity", "crc*");
        options.AddAdditionalAppiumOption("autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 300);
        // Debug .NET MAUI cold start (un-AOT'd assemblies) runs ~17-23s on the
        // emulator and Appium force-stops (-S) before each launch, so the default
        // 20s adbExecTimeout on `am start-activity -W` flakes at the boundary.
        // 45s is ~2x the measured max cold-start and sits below the 60s SessionTimeout,
        // so the adb-level timeout fires first (clean, retryable error) rather than
        // racing the HTTP session timeout (session-killing WebDriverTimeoutException).
        // Release AOT launches fast, but Release strips the #if DEBUG shim these
        // tests need, so Debug is mandatory here.
        options.AddAdditionalAppiumOption("adbExecTimeout", 45000);

        return options;
    }

    private static AppiumOptions GetIOSOptions()
    {
        var options = new AppiumOptions();
        options.PlatformName = "iOS";
        options.AutomationName = "XCUITest";
        options.AddAdditionalAppiumOption("bundleId", IOSBundleId);
        // Preserve app data (incl. the DB seeded by TestDataSeed) across driver creation.
        options.AddAdditionalAppiumOption("noReset", true);
        // Do NOT set autoDismissAlerts: it auto-taps Cancel, trapping us in
        // Discard/Cancel dialogs. Tests dismiss alerts via DismissAlertIfPresent.
        // Hardware keyboard hides software keyboard — prevents SendKeys from
        // hitting dictation/emoji buttons. Only works when Appium boots the
        // simulator itself (not pre-booted).
        options.AddAdditionalAppiumOption("connectHardwareKeyboard", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 300);
        // Sits below IOSSessionTimeout (240s) so the WDA launch error fires before the HTTP timeout.
        options.AddAdditionalAppiumOption("wdaLaunchTimeout", 180000);

        // iPad: mobile: hideKeyboard works reliably on tablets (has "Done" button).
        // iPhone keyboard has no dismiss button, causing cascade failures.
        options.DeviceName = Environment.GetEnvironmentVariable("IOS_SIMULATOR") ?? "Test iPad";
        options.PlatformVersion = Environment.GetEnvironmentVariable("IOS_VERSION") ?? "27.0";
        options.AddAdditionalAppiumOption("udid", IOSUdid);

        return options;
    }
}
