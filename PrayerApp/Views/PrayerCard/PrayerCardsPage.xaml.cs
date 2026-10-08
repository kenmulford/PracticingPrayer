using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using PrayerApp.Helpers;
using PrayerApp.Services;
using PrayerApp.ViewModels;
#if ANDROID
using Android.Views;
#endif

namespace PrayerApp.Views.PrayerCard;

public partial class PrayerCardsPage : ContentPage
{
    public PrayerCardsPage(PrayerCardsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // PERF-9 probes — confirm whether Shell pop re-handlers the page or the CollectionView.
        // Page-level firings without CV-level firings → page handler torn down + recreated.
        // CV-level firings without page-level → only the CollectionView is re-handlered.
        // Both → full view-tree teardown. Neither during Save→Cards → re-inflation has a different cause.
        // Strip with Slice 5 PerfLog cleanup.
        // HandlerChanging += (_, e) => PerfLog.Log($"PrayerCardsPage.HandlerChanging old={(e.OldHandler != null ? "set" : "null")} new={(e.NewHandler != null ? "set" : "null")}");
        // HandlerChanged  += (_, _) => PerfLog.Log($"PrayerCardsPage.HandlerChanged handler={(Handler != null ? "set" : "null")}");
        // cardCollection.HandlerChanging += (_, e) => PerfLog.Log($"cardCollection.HandlerChanging old={(e.OldHandler != null ? "set" : "null")} new={(e.NewHandler != null ? "set" : "null")}");
        // cardCollection.HandlerChanged  += (_, _) => PerfLog.Log($"cardCollection.HandlerChanged handler={(cardCollection.Handler != null ? "set" : "null")}");

        // PERF-10 probes — find the parent-layout-invalidation source that triggers the
        // RecyclerView re-inflate cascade. PERF-9 ruled out handler recycling; the cascade
        // must come from a layout-pass invalidation on a parent that survives. Candidates:
        // Shell pop animation re-measuring the page root, BoxSections reassignment hitting
        // a frame contended with animation, or a child SizeChanged propagating up. These
        // probes timestamp every relevant signal so the device log can show what fires
        // immediately before each OnCardBorderLoaded burst.
        // Strip with Slice 5 PerfLog cleanup.
        // SizeChanged += (_, _) => PerfLog.Log($"PrayerCardsPage.SizeChanged w={Width:F0} h={Height:F0}");
        // cardCollection.SizeChanged += (_, _) => PerfLog.Log($"cardCollection.SizeChanged w={cardCollection.Width:F0} h={cardCollection.Height:F0}");
        // cardCollection.MeasureInvalidated += (_, _) => PerfLog.Log("cardCollection.MeasureInvalidated");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PrayerCardsViewModel.IsMultiSelectMode)) return;
        ApplyMultiSelectToolbarState((PrayerCardsViewModel)sender!);
    }

    /// <summary>
    /// Mutates the single overflow toolbar item between "More" (opens popup) and
    /// "Cancel" (exits multi-select). <b>AutomationId is never reassigned</b> — MAUI's
    /// `BindableProperty` enforces set-once on AutomationId and throws on mutation.
    /// Only Text/Icon/SemanticProperties change between modes. Semantics are set BEFORE
    /// Text and Icon: Android refreshes a toolbar item's accessibility description only on a
    /// Text, Icon, or IsEnabled change (Microsoft.Maui.Controls 10.0.110
    /// `ToolbarExtensions.OnToolbarItemPropertyChanged`), so the Text/Icon change must come last.
    /// Icon files are selected per-theme because Shell.ForegroundColor doesn't
    /// reliably tint ToolbarItem bitmaps on Android (the SVG-rasterized PNG is
    /// baked black); the *_dark variants have explicit light fills.
    /// </summary>
    private void ApplyMultiSelectToolbarState(PrayerCardsViewModel vm)
    {
        var item = ToolbarItems.FirstOrDefault();
        if (item is null) return;

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        if (vm.IsMultiSelectMode)
        {
            SemanticProperties.SetDescription(item, "Cancel");
            SemanticProperties.SetHint(item, "Exit multi-select mode");
            item.Text = "Cancel";
            item.IconImageSource = isDark ? "xmark_solid_full_dark.png" : "xmark_solid_full.png";
        }
        else
        {
            SemanticProperties.SetDescription(item, "More actions");
            SemanticProperties.SetHint(item, "Opens a menu with Add Card, Manage Collections, and Select");
            item.Text = "More";
            item.IconImageSource = isDark
                ? "ellipsis_vertical_solid_full_dark.png"
                : "ellipsis_vertical_solid_full.png";
        }
    }

    /// <summary>
    /// Scroll to a freshly-saved (or move-target) card. Must yield two dispatcher
    /// ticks first so the platform CollectionView adapter has committed the
    /// BoxSections rebuild — calling ScrollTo while the adapter snapshot is stale
    /// throws IllegalArgumentException "Invalid target position" on Android.
    /// </summary>
    /// <remarks>
    /// Issue #42 (shape (i)): the cell's expanded subtree is now inlined in the
    /// DataTemplate and always inflated (hidden via IsVisible when collapsed), so
    /// there is no realization signal to await — once the adapter snapshot is
    /// committed the target cell's content is already present, wherever it sits in
    /// the list. This retired the prior _savedCardRealizedTcs wait + 2 s fallback
    /// delay, which never fired for an off-screen system-card target (sort ~900)
    /// and was the #42 root cause. Slice 6g split is preserved: this method does
    /// the awaited part (layout drain + scroll + announce) so OnAppearing can lower
    /// IsAwaitingSavedCard immediately after; the 2.5 s highlight fade runs in the
    /// background via FadeHighlightAfterDelayAsync and must NOT block the
    /// overlay-off transition.
    /// </remarks>
    private async Task ScrollToSavedCardAsync(PrayerCardsViewModel vm, PrayerCardViewModel card)
    {
        await Dispatcher.DrainLayoutPassAsync();

        try
        {
            var section = vm.BoxSections.FirstOrDefault(s => s.Contains(card));
            if (section != null)
                cardCollection.ScrollTo(card, section, ScrollToPosition.Center, animate: true);
            else
                cardCollection.ScrollTo(card, position: ScrollToPosition.Center, animate: true);

            SemanticScreenReader.Announce($"New card: {card.Title}");
        }
        catch (Exception ex)
        {
            Diagnostics.ResolveLog()?.Log("PrayerCardsPage.ScrollToSavedCardAsync", ex);
        }
    }

    private static async Task FadeHighlightAfterDelayAsync(PrayerCardViewModel card)
    {
        await Task.Delay(2500);
        card.IsHighlighted = false;
        // PerfLog.Log("FadeHighlightAfterDelayAsync.exit");
    }

    private void OnSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.BindingContext is BoxSectionViewModel section)
        {
            section.IsExpanded = !section.IsExpanded;

            SemanticScreenReader.Announce(section.IsExpanded
                ? $"Expanded {section.Name}"
                : $"Collapsed {section.Name}");

            // Persist expansion state so it survives app restarts
            if (BindingContext is PrayerCardsViewModel vm)
                vm.SaveSectionExpansionState();
        }
    }

    private void OnCardBorderLoaded(object? sender, EventArgs e)
    {
        if (sender is not Border border) return;

        // Issue #42 (shape (i)): the expanded subtree is inlined in the cell
        // DataTemplate and eagerly realized (hidden via IsVisible="{Binding
        // IsExpanded}" when collapsed), so the prior lazy realization machinery
        // (Rebind / RealizeExpandedSubtree / ExpandedSubtreeHost) is gone — it was
        // the recurring root of the off-screen-target stale-cell bug. The only
        // responsibility left here is the iOS long-press TouchBehavior install
        // (Android handles long-press natively via CardGestureListener).

#if !ANDROID
        // iOS: TouchBehavior on the Border coexists with child tap gestures natively.
        if (BindingContext is not PrayerCardsViewModel vm) return;
        var behavior = border.Behaviors.OfType<CommunityToolkit.Maui.Behaviors.TouchBehavior>().FirstOrDefault();
        if (behavior is null)
        {
            behavior = new CommunityToolkit.Maui.Behaviors.TouchBehavior
            {
                LongPressDuration = 500,
                ShouldMakeChildrenInputTransparent = false
            };
            border.Behaviors.Add(behavior);
        }
        behavior.LongPressCommand = vm.LongPressCardCommand;
        behavior.SetBinding(CommunityToolkit.Maui.Behaviors.TouchBehavior.LongPressCommandParameterProperty,
            new Binding("BindingContext", source: border));
#endif
    }

    // BUG-60: On Android, MAUI TapGestureRecognizer and native GestureDetector conflict.
    // Handle both tap and long-press natively via GestureDetector on the header Grid,
    // following the proven pattern from TagDetailPage.
    private void OnCardHeaderLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Grid grid) return;
        // Handler may not be set at Loaded time — use HandlerChanged fallback
        if (grid.Handler is not null)
            AttachNativeCardGestures(grid);
        else
            grid.HandlerChanged += OnCardHeaderHandlerChanged;
#endif
    }

#if ANDROID
    private void OnCardHeaderHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Grid grid && grid.Handler is not null)
        {
            grid.HandlerChanged -= OnCardHeaderHandlerChanged;
            AttachNativeCardGestures(grid);
        }
    }

    private void AttachNativeCardGestures(Grid grid)
    {
        if (grid.Handler?.PlatformView is not Android.Views.View nativeView) return;
        if (BindingContext is not PrayerCardsViewModel vm) return;

        // Remove MAUI's TapGestureRecognizer — it conflicts with native GestureDetector.
        // Both tap and long-press are handled natively via CardGestureListener.
        grid.GestureRecognizers.Clear();

        var listener = new CardGestureListener(grid, vm);
        var detector = new GestureDetector(nativeView.Context, listener);
        nativeView.Touch += (s, args) =>
        {
            args.Handled = detector.OnTouchEvent(args.Event!);
        };
    }

    /// <summary>
    /// Handles both tap and long-press natively on Android.
    /// MAUI's TapGestureRecognizer and native GestureDetector conflict when both are
    /// active, so we own the full touch sequence at the native level.
    /// The XAML TapGestureRecognizer on the header Grid is effectively bypassed on Android.
    /// </summary>
    private sealed class CardGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly Grid _grid;
        private readonly PrayerCardsViewModel _vm;

        public CardGestureListener(Grid grid, PrayerCardsViewModel vm)
        {
            _grid = grid;
            _vm = vm;
        }

        public override bool OnDown(MotionEvent? e) => true;

        public override bool OnSingleTapUp(MotionEvent? e)
        {
            if (_grid.BindingContext is PrayerCardViewModel card)
            {
                if (_vm.IsMultiSelectMode)
                    _vm.ToggleCardSelection(card);
                else
                    card.ToggleExpandedCommand.Execute(null);
            }
            return true;
        }

        public override void OnLongPress(MotionEvent? e)
        {
            if (_grid.BindingContext is PrayerCardViewModel card)
                MainThread.BeginInvokeOnMainThread(() => _vm.LongPressCardCommand.Execute(card));
        }
    }
#endif

    private void OnCardHeaderTapped(object? sender, TappedEventArgs e)
    {
        // On Android, tap is handled natively by CardGestureListener (BUG-60).
        // This handler still fires on iOS where the XAML TapGestureRecognizer is active.
        if (sender is not Grid grid || grid.BindingContext is not PrayerCardViewModel card) return;
        if (BindingContext is PrayerCardsViewModel vm && vm.IsMultiSelectMode)
            vm.ToggleCardSelection(card);
        else
            card.ToggleExpandedCommand.Execute(null);
    }

    /// <summary>
    /// The check circle (left of the card in multi-select mode) toggles selection.
    /// Card body tap already toggles via OnCardHeaderTapped / CardGestureListener; this
    /// handler covers users who aim at the circle specifically (iOS Mail pattern).
    /// </summary>
    private void OnCheckCircleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Image img && img.BindingContext is PrayerCardViewModel card
            && BindingContext is PrayerCardsViewModel vm && vm.IsMultiSelectMode)
        {
            vm.ToggleCardSelection(card);
        }
    }

    /// <summary>
    /// Overflow toolbar handler. In multi-select mode, tapping exits multi-select
    /// directly (fast path, no popup). Otherwise opens the CardsOverflowPopup with
    /// the three actions (Add Card / Manage Collections / Select Cards).
    /// </summary>
    private async void OnOverflowTapped(object? sender, EventArgs e)
    {
        if (BindingContext is not PrayerCardsViewModel vm) return;

        if (vm.IsMultiSelectMode)
        {
            vm.CancelMultiSelectCommand.Execute(null);
            return;
        }

        // Shape = null disables CT.Maui v14's default white-filled RoundRectangle
        // frame — our inner Border owns the rounded themed surface instead.
        var options = new PopupOptions { Shape = null };
        await this.ShowPopupAsync(new CardsOverflowPopup(vm), options, CancellationToken.None);
    }

    private void OnSearchButtonPressed(object? sender, EventArgs e)
        => searchBar.Unfocus();

    private void OnBackgroundTapped(object? sender, TappedEventArgs e)
    {
        if (searchBar.IsFocused) searchBar.Unfocus();
    }

    protected override async void OnAppearing()
    {
        // PerfLog.Log("PrayerCardsPage.OnAppearing.entry");
        base.OnAppearing();
        if (BindingContext is not PrayerCardsViewModel vm) return;

        // Slice 6g — assert the cross-page busy flag immediately so the overlay
        // is up the moment the Cards page becomes the foreground page, masking
        // the SyncAsync→ScrollTo gap. Cleared in finally after ScrollTo (or
        // immediately if no save was pending). The 2.5s highlight fade runs in
        // the background and does NOT block the overlay-off transition.
        var hadPendingSave = !string.IsNullOrEmpty(vm.PendingSavedIdentifier);
        var overlayShownAt = DateTime.UtcNow;
        if (hadPendingSave) vm.IsAwaitingSavedCard = true;

        try
        {
            // BUG-79: see vm.SuppressNextOnAppearingSync. ConsumePendingSavedAsync
            // below still runs (highlight + scroll on save).
            var suppressSync = vm.SuppressNextOnAppearingSync;
            vm.SuppressNextOnAppearingSync = false;
            if (!suppressSync)
                await PageSync.OnAppearingAsync(vm);

            // PerfLog.Log("OnAppearing.before ConsumePendingSavedAsync");
            var savedCard = await vm.ConsumePendingSavedAsync();
            // PerfLog.Log($"OnAppearing.after ConsumePendingSavedAsync (savedCard null? {savedCard == null})");
            if (savedCard != null)
            {
                await ScrollToSavedCardAsync(vm, savedCard);
                FadeHighlightAfterDelayAsync(savedCard).SafeFireAndForget();
            }
        }
        finally
        {
            if (hadPendingSave)
            {
                // Slice 6g hardening (#1): minimum visible duration. On a fast
                // path (warm cache, fast device) the entire flow above can run
                // in <50 ms — short enough that MAUI's binding propagation +
                // layout pass for the visible overlay never schedules between
                // IsAwaitingSavedCard true and false, and the overlay never
                // visually renders. Hold the flag true for at least 250 ms so
                // the user perceives a deliberate cue, not a flicker — and so
                // the contract holds across device speeds.
                const int MinOverlayMs = 250;
                var elapsedMs = (int)(DateTime.UtcNow - overlayShownAt).TotalMilliseconds;
                if (elapsedMs < MinOverlayMs)
                {
                    // PerfLog.Log($"OnAppearing.minOverlay holding +{MinOverlayMs - elapsedMs}ms (elapsed={elapsedMs}ms)");
                    await Task.Delay(MinOverlayMs - elapsedMs);
                }
                vm.IsAwaitingSavedCard = false;
            }
            // PerfLog.Log("OnAppearing.exit");
        }
    }
}
