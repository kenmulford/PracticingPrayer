namespace PrayerApp.Services;

/// <summary>
/// Abstracts Shell navigation and user-facing dialogs for testability.
/// ViewModels inject this instead of calling Shell.Current directly.
/// </summary>
public interface INavigationService
{
    Task GoToAsync(string route);
    Task PushModalAsync(Page page);
    Task PopModalAsync();

    /// <summary>
    /// Push a modal page from a non-UI-thread caller (deep-link intent /
    /// open-url / iOS Window.Activated callback). Awaits Shell-realized,
    /// hops to UI thread via IDispatcher, awaits the push to completion.
    /// On iOS, calling raw <see cref="PushModalAsync"/> off the main thread
    /// or before AppShell appears throws or no-ops.
    /// </summary>
    Task PushModalOnUiThreadAsync(Page page);

    /// <summary>
    /// Push a modal page wrapped in a <see cref="NavigationPage"/> so its
    /// ToolbarItems (Cancel/Save) render in the wrapper's nav bar. A bare
    /// modal Page has nowhere to display ToolbarItems — bug observed on-device
    /// where the ConfirmImportPage Cancel/Save buttons were invisible until
    /// wrapped. <see cref="Views.PageSheetPresentation"/> treats a NavigationPage whose root
    /// is <see cref="Views.IPageSheetModal"/> as the same target, so PageSheet
    /// presentation is preserved. Goes through the cold-start gate +
    /// UI-thread hop the same way <see cref="PushModalOnUiThreadAsync"/> does.
    /// </summary>
    Task PushModalWithNavigationBarAsync(Page page);

    Task<bool> DisplayConfirmAsync(string title, string message, string accept, string cancel);
    Task DisplayAlertAsync(string title, string message, string ok);
    Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons);

    /// <summary>
    /// Completes once <see cref="Shell.Current"/> is realized. Used
    /// internally by <see cref="PushModalOnUiThreadAsync"/>; exposed for
    /// any caller that needs the same gate without the dispatch hop.
    /// </summary>
    Task WhenShellReadyAsync();
}
