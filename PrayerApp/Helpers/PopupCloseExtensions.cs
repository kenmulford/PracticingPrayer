using CommunityToolkit.Maui.Views;

namespace PrayerApp.Helpers;

public static class PopupCloseExtensions
{
    // Toolkit throws internal PopupBlockedException / PopupNotFoundException (both InvalidOperationException)
    // when the popup is not the top modal or is already gone; escaping an async-void handler kills the process.
    // Returns false when the close was refused, so the caller skips its follow-up.
    public static Task<bool> TryCloseAsync(this Popup popup, CancellationToken token = default) =>
        TryCloseAsync(() => popup.CloseAsync(token), popup.GetType().Name);

    public static async Task<bool> TryCloseAsync(Func<Task> close, string source = "Popup")
    {
        try
        {
            await close();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Diagnostics.ResolveLog()?.Log($"{source}.Close", ex);
            return false;
        }
    }
}
