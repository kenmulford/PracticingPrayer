using System.ComponentModel;
using PrayerApp.Helpers;
using PrayerApp.ViewModels;

namespace PrayerApp.Views;

public partial class ConfirmImportPage : ContentPage, IPageSheetModal
{
    private bool _animating;

    // View-model changes that arrived while _animating; replayed against the view
    // model's current state when the animation ends.
    private bool _modePending;
    private bool _selectionPending;

    // The card the summary last showed. A re-point to the same card (the relock reload)
    // must not collapse a list the user reopened with "Change".
    private int? _summaryCardId;

    // #122: the prayer Title Entry lives inside a BindableLayout ItemTemplate, so
    // it has no x:Name and code-behind has no compile-time handle. The first row's
    // Entry captures itself here via its Loaded event so OnAppearing can focus it
    // (Quick Add: keyboard up, ready to type). Cleared on disappear to avoid
    // holding a stale recycled-Entry reference across page lifecycles.
    private Entry? _firstPrayerTitleEntry;

    public ConfirmImportPage(ConfirmImportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnPrayerTitleEntryLoaded(object? sender, EventArgs e)
    {
        // Capture only the FIRST prayer row's Entry. Manual (Quick Add) seeds
        // exactly one row, so the first Loaded Entry is the one to focus. Import
        // mode can load many rows; we still only keep the first and never focus
        // it (the focus call below is gated to Manual mode).
        _firstPrayerTitleEntry ??= sender as Entry;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is not ConfirmImportViewModel vm) return;

        if (vm.EntryMode == EntryMode.Manual)
        {
            // Manual (Quick Add) path — runs on the UI thread so ObservableCollection
            // mutations in LoadManualCardGroupsAsync are safe. ConsumePending is a
            // no-op (InitializeManualEntry already set _consumed = true). Load is
            // done here, not fire-and-forget in the caller, so SelectedCard is set
            // before the collapse-to-summary check below runs.
            Title = "Quick Add";
            // _consumed is already true from InitializeManualEntry — no-op.
            await vm.LoadBoxesAsync();
            await vm.LoadManualCardGroupsAsync();

            // #122: focus the prayer Title field so the keyboard is up and the
            // user can type immediately. Drain the modal-present layout pass
            // first — a single dispatcher tick resolves before the platform Entry
            // view is stable and Focus() silently no-ops mid-animation (BUG-70
            // family; same pattern as PrayerCardPage / PrayerDetailPage). Guarded
            // by try/catch because Focus() can throw if the handler isn't ready.
            await Dispatcher.DrainLayoutPassAsync();
            if (_firstPrayerTitleEntry is not null)
            {
                try { _firstPrayerTitleEntry.Focus(); }
                catch (Exception ex)
                {
                    Diagnostics.ResolveLog()?.Log("ConfirmImportPage.OnAppearing focus", ex);
                }
            }
        }
        else
        {
            // Import path — unchanged behavior.
            // Sync work first so card title + prayer rows paint on the first
            // frame; the Collection picker populates a tick later. Both calls
            // are idempotent — modal OnAppearing fires on initial show AND on
            // resume from background; ConsumePending guards via _consumed and
            // LoadBoxesAsync via _boxesLoaded so the user's mid-flow Collection
            // pick survives backgrounding.
            Title = "Confirm Import";
            vm.ConsumePending();
            await vm.LoadBoxesAsync();
        }

        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;

        // Restore collapsed state when resuming with an existing selection
        // (e.g. backgrounded mid-flow, or Quick Add with Quick Add card preselected).
        // No animation — page is just appearing.
        if (vm.IsExistingCardMode && vm.SelectedCard is not null)
        {
            cardGroupsList.IsVisible = false;
            selectedCardSummary.IsVisible = true;
            _summaryCardId = vm.SelectedCard.CardId;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Swipe-dismiss on iOS PageSheet modals does not fire CancelCommand;
        // this is the only drain path for that case.
        // #122: drop the captured first-row Entry so a recycled handle from a
        // prior lifecycle isn't reused on the next appear.
        _firstPrayerTitleEntry = null;
        if (BindingContext is ConfirmImportViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.DrainIfNotConsumed();
            // Dispose() is idempotent — safe even if OnDisappearing fires
            // more than once across the page lifecycle (modal pop is the
            // expected single-fire case, but defence-in-depth).
            vm.Dispose();
        }
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var mode = e.PropertyName == nameof(ConfirmImportViewModel.IsExistingCardMode);
        var selection = e.PropertyName == nameof(ConfirmImportViewModel.SelectedCard);
        if (!mode && !selection) return;

        _modePending |= mode;
        _selectionPending |= selection;
        if (_animating) return;

        _animating = true;
        try
        {
            await ApplyPendingChangesAsync();
        }
        finally
        {
            _animating = false;
        }
    }

    // Applies every change recorded while an animation ran, against the view model's
    // current state, so a change that arrived mid-animation still reaches its end state.
    private async Task ApplyPendingChangesAsync()
    {
        while (BindingContext is ConfirmImportViewModel vm && (_modePending || _selectionPending))
        {
            var (mode, selection) = (_modePending, _selectionPending);
            _modePending = _selectionPending = false;
            if (mode) await ApplyModeAsync(vm);
            if (selection) await ApplySelectionAsync(vm);
        }
    }

    private async Task ApplyModeAsync(ConfirmImportViewModel vm)
    {
        if (vm.IsExistingCardMode)
        {
            selectedCardSummary.IsVisible = false;
            await ShowCardListEntranceAsync();
        }
        else
        {
            // Switching to New Card mode — hide both the list and any visible summary
            await cardGroupsList.FadeToAsync(0, 150, Easing.CubicIn);
            cardGroupsList.IsVisible = false;
            cardGroupsList.Opacity = 1;
            cardGroupsList.TranslationY = 0;
            selectedCardSummary.IsVisible = false;
        }
    }

    private async Task ApplySelectionAsync(ConfirmImportViewModel vm)
    {
        if (vm.SelectedCard is not { } card)
        {
            _summaryCardId = null;
            if (selectedCardSummary.IsVisible)
            {
                // Selection cleared while summary is showing — collection filter changed
                await CollapseSummaryAndShowListAsync();
            }
        }
        else if (cardGroupsList.IsVisible && card.CardId != _summaryCardId)
        {
            _summaryCardId = card.CardId;
            await cardGroupsList.FadeToAsync(0, 180, Easing.CubicIn);
            cardGroupsList.IsVisible = false;
            cardGroupsList.Opacity = 1;
            selectedCardSummary.Opacity = 0;
            selectedCardSummary.IsVisible = true;
            await selectedCardSummary.FadeToAsync(1, 220, Easing.CubicOut);
        }
        else if (selectedCardSummary.IsVisible)
        {
            _summaryCardId = card.CardId;
        }
    }

    private void OnRemovePrayerClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: EditablePrayer row } &&
            BindingContext is ConfirmImportViewModel vm)
        {
            vm.RemovePrayerCommand.Execute(row);
        }
    }

    private void OnAddDuplicateClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: EditablePrayer row } &&
            BindingContext is ConfirmImportViewModel vm)
        {
            vm.AddDuplicateCommand.Execute(row);
        }
    }

    private void OnCardItemTapped(object? sender, TappedEventArgs e)
    {
        if (_animating) return;
        if (sender is Border { BindingContext: CardPickerItem item } &&
            BindingContext is ConfirmImportViewModel vm)
            vm.SelectCardCommand.Execute(item);
    }

    private async void OnChangeCardTapped(object? sender, TappedEventArgs e)
    {
        if (_animating) return;
        _animating = true;
        try
        {
            // Does NOT clear SelectedCard — checkmark stays on the prior selection
            // so the user can confirm or pick a different row.
            await CollapseSummaryAndShowListAsync();
            await ApplyPendingChangesAsync();
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CollapseSummaryAndShowListAsync()
    {
        await selectedCardSummary.FadeToAsync(0, 150, Easing.CubicIn);
        selectedCardSummary.IsVisible = false;
        selectedCardSummary.Opacity = 1;
        await ShowCardListEntranceAsync();
    }

    private async Task ShowCardListEntranceAsync()
    {
        cardGroupsList.Opacity = 0;
        cardGroupsList.TranslationY = 20;
        cardGroupsList.IsVisible = true;
        await Task.WhenAll(
            cardGroupsList.FadeToAsync(1, 280, Easing.CubicOut),
            cardGroupsList.TranslateToAsync(0, 0, 280, Easing.CubicOut)
        );
    }
}
