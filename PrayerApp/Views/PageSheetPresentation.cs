using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using NavigationPage = Microsoft.Maui.Controls.NavigationPage;
using Page = Microsoft.Maui.Controls.Page;

namespace PrayerApp.Views;

/// <summary>
/// Sets the iOS modal presentation style to PageSheet for pages marked
/// <see cref="IPageSheetModal"/> and for a <see cref="NavigationPage"/> whose root is marked.
/// MAUI builds the presented wrapper controller from this platform-specific property alone,
/// so it must be set before the push (Application.ModalPushing); styling the page's own
/// view controller has no effect. Other pages keep the default FullScreen.
/// </summary>
internal static class PageSheetPresentation
{
    internal static void Apply(Page modal)
    {
        if (modal is IPageSheetModal || modal is NavigationPage { RootPage: IPageSheetModal })
            modal.On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.PageSheet);
    }
}
