using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using PrayerApp.Views;
using NavigationPage = Microsoft.Maui.Controls.NavigationPage;
using Page = Microsoft.Maui.Controls.Page;

namespace PrayerApp.Tests.Views;

public class PageSheetPresentationTests
{
    private sealed class SheetPage : ContentPage, IPageSheetModal { }

    private static UIModalPresentationStyle StyleOf(Page page) =>
        page.On<iOS>().ModalPresentationStyle();

    [Fact]
    public void Apply_PageSheetModalPage_SetsPageSheet()
    {
        var page = new SheetPage();

        PageSheetPresentation.Apply(page);

        Assert.Equal(UIModalPresentationStyle.PageSheet, StyleOf(page));
    }

    [Fact]
    public void Apply_NavigationPageWrappingPageSheetModal_SetsPageSheet()
    {
        var page = new NavigationPage(new SheetPage());

        PageSheetPresentation.Apply(page);

        Assert.Equal(UIModalPresentationStyle.PageSheet, StyleOf(page));
    }

    [Fact]
    public void Apply_UnmarkedPage_LeavesFullScreen()
    {
        var page = new ContentPage();

        PageSheetPresentation.Apply(page);

        Assert.Equal(UIModalPresentationStyle.FullScreen, StyleOf(page));
    }

    [Fact]
    public void Apply_NavigationPageWrappingUnmarkedPage_LeavesFullScreen()
    {
        var page = new NavigationPage(new ContentPage());

        PageSheetPresentation.Apply(page);

        Assert.Equal(UIModalPresentationStyle.FullScreen, StyleOf(page));
    }
}
