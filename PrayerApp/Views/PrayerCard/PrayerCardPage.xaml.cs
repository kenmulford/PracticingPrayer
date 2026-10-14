using PrayerApp.Helpers;
using PrayerApp.ViewModels;

namespace PrayerApp.Views.PrayerCard;

public partial class PrayerCardPage : ContentPage
{
    private readonly ToolbarItem _saveToolbarItem;
    private readonly ToolbarItem _saveAndNewToolbarItem;

    public PrayerCardPage(PrayerCardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        _saveToolbarItem = new ToolbarItem { Text = "Save", AutomationId = "Save" };
        _saveToolbarItem.SetBinding(ToolbarItem.CommandProperty, nameof(PrayerCardViewModel.SaveCommand));
        SemanticProperties.SetHint(_saveToolbarItem, "Save prayer card changes");

        _saveAndNewToolbarItem = new ToolbarItem { Text = "Save +", AutomationId = "Save +" };
        _saveAndNewToolbarItem.SetBinding(ToolbarItem.CommandProperty, nameof(PrayerCardViewModel.SaveAndNewCommand));
        SemanticProperties.SetDescription(_saveAndNewToolbarItem, "Save and add another card");
        SemanticProperties.SetHint(_saveAndNewToolbarItem, "Save this card and create a new one");
#if IOS || MACCATALYST
        // Secondary on iOS keeps Save as the sole Primary; UIKit auto-overflow would
        // drop a11y labels on the overflowed buttons.
        _saveAndNewToolbarItem.Order = ToolbarItemOrder.Secondary;
#endif

        ToolbarItems.Add(_saveToolbarItem);
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if IOS
        Platforms.iOS.Helpers.SwipeBackHelper.DisableSwipeBack(this);
#endif
        if (BindingContext is PrayerCardViewModel vm)
        {
            UpdateToolbarItems(vm);

            // BUG-75 safety net: retry once on the next dispatcher tick if the platform
            // layer silently dropped the toolbar items during a layout race.
            _ = Dispatcher.DispatchAsync(() =>
            {
                if (ToolbarItems.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[BUG-75] Rebuilding empty ToolbarItems");
                    UpdateToolbarItems(vm);
                }
            });

            // Load boxes for the picker (new cards won't have ApplyQueryAttributes trigger this)
            if (vm.AvailableBoxes.Count == 0)
                await vm.LoadBoxPickerAsync();
            if (vm.IsNew && !vm.IsLoadRequested)
            {
                // Drain the Shell push layout pass before Focus(); a single dispatcher
                // tick resolves before the platform Entry view is stable and Focus()
                // silently no-ops mid-animation (BUG-70).
                await FocusTitleAsync();
            }
        }
    }

    private async Task FocusTitleAsync()
    {
        await Dispatcher.DrainLayoutPassAsync();
        try { TitleEntry.Focus(); }
        catch (Exception ex)
        {
            Diagnostics.ResolveLog()?.Log("PrayerCardPage.FocusTitleAsync", ex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not PrayerCardViewModel vm) return;

        if (e.PropertyName == nameof(PrayerCardViewModel.ShowSaveAndNew))
        {
            UpdateToolbarItems(vm);
        }
        else if (e.PropertyName == nameof(PrayerCardViewModel.PendingFocusTitle)
                 && vm.PendingFocusTitle)
        {
            _ = FocusAfterResetAsync(vm);
        }
    }

    private async Task FocusAfterResetAsync(PrayerCardViewModel vm)
    {
        try
        {
            await FocusTitleAsync();
        }
        finally
        {
            vm.ConsumePendingFocusTitle();
        }
    }

    // Diff-update so an unchanged set causes no platform mutation (Shell chrome race, BUG-75).
    private void UpdateToolbarItems(PrayerCardViewModel vm)
    {
        var desired = new List<ToolbarItem>(2);
        if (vm.ShowSaveAndNew)
            desired.Add(_saveAndNewToolbarItem);
        desired.Add(_saveToolbarItem);

        if (ToolbarItems.SequenceEqual(desired)) return;

        ToolbarItems.Clear();
        foreach (var item in desired)
            ToolbarItems.Add(item);
    }

    protected override void OnDisappearing()
    {
#if IOS
        Platforms.iOS.Helpers.SwipeBackHelper.EnableSwipeBack(this);
#endif
        base.OnDisappearing();
    }

    private void OnBackgroundTapped(object? sender, TappedEventArgs e)
    {
        // Dismiss keyboard when tapping outside input fields (iOS)
        if (TitleEntry.IsFocused)
            TitleEntry.Unfocus();
    }
}
