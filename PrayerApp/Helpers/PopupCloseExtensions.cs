using CommunityToolkit.Maui.Views;

namespace PrayerApp.Helpers;

public static class PopupCloseExtensions
{
    // Toolkit throws internal PopupBlockedException / PopupNotFoundException (both InvalidOperationException)
    // when the popup is not the top modal or is already gone; escaping an async-void handler kills the process.
    public static async Task TryCloseAsync(this Popup popup, CancellationToken token = default)
    {
        try
        {
            await popup.CloseAsync(token);
        }
        catch (InvalidOperationException ex)
        {
            Diagnostics.ResolveLog()?.Log($"{popup.GetType().Name}.Close", ex);
        }
    }
}
