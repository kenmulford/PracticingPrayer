using System.IO;
using System.Xml.Linq;

namespace PrayerApp.Tests.Views;

/// <summary>
/// Locks the root-page <c>SafeAreaEdges</c> values that keep content clear of the
/// system bars and the keyboard under Android 15+ edge-to-edge (#357).
/// </summary>
public class SafeAreaEdgesContractTests
{
    private const string AndroidKeyboardNoneElsewhere = "{OnPlatform Android='Container, Container, Container, All', Default=None}";
    private const string KeyboardAware = "{OnPlatform Android='Container, Container, Container, All', Default=Container}";

    [Theory]
    [InlineData("PrayerApp/Views/PrayerCard/PrayerCardPage.xaml", "Container, Container, Container, All")]
    [InlineData("PrayerApp/Views/Prayer/PrayerDetailPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/Boxes/BoxDetailPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/Tags/TagDetailPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/Settings/AppSettingsPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/ConfirmImportPage.xaml", AndroidKeyboardNoneElsewhere)]
    [InlineData("PrayerApp/Views/Backup/RestoreProgressPage.xaml", "{OnPlatform Android=Container, Default=None}")]
    [InlineData("PrayerApp/Views/Prayer/PrayerListPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/PrayerCard/PrayerCardsPage.xaml", KeyboardAware)]
    [InlineData("PrayerApp/Views/Tags/TagPickerPage.xaml", "All")]
    [InlineData("PrayerApp/Views/PrayerTime/PrayerTimePage.xaml", "Container")]
    public void RootPage_DeclaresSafeAreaEdges(string relativeXamlPath, string expected)
    {
        Assert.Equal(expected, LoadPageSafeAreaEdges(relativeXamlPath));
    }

    private static string? LoadPageSafeAreaEdges(string relativeXamlPath)
    {
        // The test assembly runs from bin/<Config>/net10.0/, so a few parents reach the repo root.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, relativeXamlPath);
            if (File.Exists(candidate))
                return XDocument.Load(candidate).Root?.Attribute("SafeAreaEdges")?.Value;
            current = current.Parent;
        }
        throw new FileNotFoundException($"Could not locate {relativeXamlPath} above {AppContext.BaseDirectory}");
    }
}
