using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using NavigationPage = Microsoft.Maui.Controls.NavigationPage;
using Page = Microsoft.Maui.Controls.Page;

namespace PrayerApp.Views;

/// <summary>
/// Sets the iOS modal presentation style to PageSheet for pages marked
/// <see cref="IPageSheetModal"/> and for a <see cref="NavigationPage"/> whose root is marked.
/// Set before the push (Application.ModalPushing): MAUI builds the presented wrapper
/// controller from this property alone. Other pages keep the default FullScreen.
/// </summary>
internal static class PageSheetPresentation
{
    internal static void Apply(Page modal)
    {
        if (modal is IPageSheetModal or NavigationPage { RootPage: IPageSheetModal })
            modal.On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.PageSheet);
    }
}
