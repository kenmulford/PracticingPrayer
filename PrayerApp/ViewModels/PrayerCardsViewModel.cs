using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PrayerApp.Helpers;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace PrayerApp.ViewModels
{
    public class PrayerCardsViewModel : ObservableObject, IQueryAttributable, ISyncableViewModel
    {
        private readonly ICardService _cardService;
        private readonly IPrayerService _prayerService;
        private readonly IOnboardingService _onboardingService;
        private readonly ITagService _tagService;
        private readonly IBoxService _boxService;
        private readonly INavigationService _navigationService;
        private readonly IAccessibilityService _accessibilityService;
        private readonly ISettings _settings;
        private readonly IConfidentialAccessService _confidentialAccessService;
        private readonly IMessenger _messenger;
        private Dictionary<int, HashSet<int>> _cardTagIds = new();
        public ObservableCollection<PrayerCardViewModel> AllPrayerCards { get; }

        private ObservableCollection<TagFilterChipViewModel> _availableTags = new();
        /// <summary>
        /// Chip-strip filter tags bound to the cards-page header CollectionView.
        /// Replaced (not mutated) on each rebuild to avoid an Add-storm of
        /// CollectionChanged events into the CollectionView each forcing
        /// per-chip measure/arrange + XAML inflation. Mirrors the BoxSections
        /// replace-vs-mutate pattern; reference-equality short-circuit in
        /// <see cref="SyncCoreAsync"/> keeps messenger-driven syncs O(0) when
        /// chip identity hasn't changed.
        /// </summary>
        public ObservableCollection<TagFilterChipViewModel> AvailableTags
        {
            get => _availableTags;
            private set => SetProperty(ref _availableTags, value);
        }
        public bool HasTags => AvailableTags.Count > 0;

        private ObservableCollection<BoxSectionViewModel> _boxSections = new();
        /// <summary>
        /// Grouped sections bound to the CollectionView. Each section is a BoxSectionViewModel
        /// that contains the PrayerCardViewModels for cards in that box.
        /// Replaced (not mutated) on each rebuild to avoid iOS UICollectionView layout desync.
        /// </summary>
        public ObservableCollection<BoxSectionViewModel> BoxSections
        {
            get => _boxSections;
            private set => SetProperty(ref _boxSections, value);
        }

        private bool _isSorting;
        private bool _suppressFilterAnnounce;
        // True after the first SyncAsync completes. The first sync suppresses the
        // filter-count announce so screen reader users don't get "Loading" + "Content
        // loaded" + "Showing N cards" all at once on cold load. Subsequent syncs
        // (messenger-driven) do announce — the count is the user-visible signal that
        // a CRUD elsewhere refreshed this page.
        private bool _hasSyncedOnce;

        // Slice 6a / PERF-10: collapses bursts of concurrent SyncAsync triggers (messenger
        // broadcast + PageSync.OnAppearingAsync + sibling rebroadcasts) into one in-flight
        // sync plus one coalesced follow-up. Each SyncAsync tail replaces BoxSections,
        // which on Android triggers a full RecyclerView re-inflate cascade (~330 ms per
        // visible cell). Pre-Slice-6a a single Save fired 3 cascades back-to-back.
        private readonly Helpers.SingleFlightGate _syncGate = new();
        private readonly Dictionary<PrayerCardViewModel, System.ComponentModel.PropertyChangedEventHandler> _cardHandlers = new();
        private CancellationTokenSource? _filterAnnounceCts;

        /// <summary>Cached box list for section building. Refreshed on SyncAsync.</summary>
        private IReadOnlyList<CardBox> _boxes = Array.Empty<CardBox>();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _accessibilityService.Announce(value ? "Loading" : "Content loaded");
                    OnPropertyChanged(nameof(IsBusyOverall));
                }
            }
        }

        // Slice 6g — Cross-page save indicator continuity.
        // The post-save flow spans: edit-page Save → GoToAsync → Cards.OnAppearing
        // → SyncAsync (toggles IsLoading internally) → ConsumePendingSavedAsync →
        // ScrollTo. Without this flag, IsLoading flips off the moment SyncAsync's
        // finally runs (mid-flow), the LoadingOverlay disappears, and the user sees
        // half-rendered cells + the new card popping in late. View code-behind sets
        // IsAwaitingSavedCard=true at OnAppearing entry when PendingSavedIdentifier
        // is non-empty, and clears it after ScrollTo completes. The LoadingOverlay
        // binds to IsBusyOverall (= IsLoading || IsAwaitingSavedCard) so either
        // flag keeps it visible.
        private bool _isAwaitingSavedCard;
        public bool IsAwaitingSavedCard
        {
            get => _isAwaitingSavedCard;
            set
            {
                if (SetProperty(ref _isAwaitingSavedCard, value))
                    OnPropertyChanged(nameof(IsBusyOverall));
            }
        }

        public bool IsBusyOverall => _isLoading || _isAwaitingSavedCard;

        // Single source of truth for the per-card expand state. Per-card
        // PrayerCardViewModel.IsExpanded is a read-only projection over this:
        // `_parent.ExpandedCardId == this.Id`. The setter manually re-raises
        // PropertyChanged on the before-and-after card so bindings refresh, and
        // emits the accessibility announce + the post-collapse RebuildSections
        // that used to live in the deleted cascade-collapse handler.
        // Because the singleton invariant is enforced structurally (one int? slot,
        // no fan-out), there is no `_suppressIsExpandedRebuild` flag to maintain.
        private int? _expandedCardId;
        public int? ExpandedCardId
        {
            get => _expandedCardId;
            set
            {
                if (_expandedCardId == value) return;
                var previousId = _expandedCardId;
                _expandedCardId = value;
                OnPropertyChanged();

                // Resolve once — same VM is used for IsExpanded re-raise AND announce.
                var previousCard = previousId is int prev
                    ? AllPrayerCards.FirstOrDefault(c => c.Id == prev) : null;
                var nextCard = value is int next
                    ? AllPrayerCards.FirstOrDefault(c => c.Id == next) : null;

                previousCard?.RaiseIsExpandedChanged();
                nextCard?.RaiseIsExpandedChanged();

                // Accessibility announce — moved here from the deleted cascade handler.
                if (nextCard != null)
                    _accessibilityService.Announce($"Expanded {nextCard.Title}");
                else if (previousCard != null)
                    _accessibilityService.Announce($"Collapsed {previousCard.Title}");

                // Re-sort sections whenever a previously-expanded card transitions away
                // (collapse OR switch-expand A→B). Favorite changes made while a card is
                // expanded land in the new section order at the next natural transition.
                // First-expand (previousId == null) leaves sort alone — same as before.
                if (previousId is not null) RebuildSections();
            }
        }

        /// <summary>True when no sections exist (no data loaded). Used to control EmptyView visibility.</summary>
        public bool HasNoSections => BoxSections.Count == 0;

        private bool _showCollectionsBanner;
        public bool ShowCollectionsBanner
        {
            get => _showCollectionsBanner;
            private set => SetProperty(ref _showCollectionsBanner, value);
        }

        public ICommand DismissCollectionsBannerCommand { get; }

        /// <summary>
        /// True once the confidential-access session is unlocked (issue #253). Cards page
        /// consults this (via each card's <see cref="PrayerCardViewModel.IsLockedVisible"/>
        /// projection) to decide whether LockedVisible cards render masked, and
        /// <see cref="RebuildSections"/> consults it directly to decide whether
        /// effectively-Hidden cards are included in the list at all.
        /// </summary>
        public bool IsSessionUnlocked => _confidentialAccessService.IsSessionUnlocked;

        public ICommand ShowConfidentialCommand { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        public ICommand NewCommand { get; }
        public ICommand CancelMultiSelectCommand { get; }
        public ICommand MoveSelectedCommand { get; }
        public ICommand LongPressCardCommand { get; }
        public ICommand EnterMultiSelectCommand { get; }
        public ICommand OpenCollectionsCommand { get; }

        private bool _isMultiSelectMode;
        public bool IsMultiSelectMode
        {
            get => _isMultiSelectMode;
            set
            {
                if (SetProperty(ref _isMultiSelectMode, value))
                {
                    OnPropertyChanged(nameof(SelectedCardCount));
                    OnPropertyChanged(nameof(SelectedCountText));
                    // Propagate to sections for header dimming
                    foreach (var section in BoxSections)
                        section.IsMultiSelectMode = value;
                    // Propagate to cards so the check slot in the DataTemplate can bind directly
                    foreach (var card in AllPrayerCards)
                        card.IsMultiSelectMode = value;
                }
            }
        }

        public int SelectedCardCount => AllPrayerCards.Count(c => c.IsMultiSelected);

        public string SelectedCountText
        {
            get
            {
                var count = SelectedCardCount;
                return count switch { 0 => "None selected", 1 => "1 selected", _ => $"{count} selected" };
            }
        }

        /// <summary>
        /// Lifecycle-gated post-save signal. ApplyQueryAttributes("saved") stages the
        /// identifier here; PrayerCardsPage.OnAppearing consumes it via ConsumePendingSavedAsync.
        /// Direct C# event handlers crashed on Android because the MauiRecyclerView adapter
        /// snapshot lagged the BoxSections rebuild — the property/lifecycle pattern lets
        /// the View gate the scroll on the dispatcher instead.
        /// </summary>
        private string? _pendingSavedIdentifier;
        public string? PendingSavedIdentifier
        {
            get => _pendingSavedIdentifier;
            private set => SetProperty(ref _pendingSavedIdentifier, value);
        }

        /// <summary>
        /// Captured at ApplyQueryAttributes time (before OnAppearing's SyncAsync runs):
        /// was the saved-id already present in AllPrayerCards? True = edit (don't highlight),
        /// false = newly created (highlight + scroll, even if SyncAsync's diff loop
        /// happens to add the row before ConsumePendingSavedAsync runs).
        /// </summary>
        private bool _pendingSavedWasAlreadyInList;
        // True when PendingSavedIdentifier was staged by the move-prayer path rather
        // than the new-card path. ConsumePendingSavedAsync scrolls without highlighting.
        private bool _pendingSavedIsMoveTarget;

        /// <summary>
        /// Set by <see cref="IQueryAttributable.ApplyQueryAttributes"/> when it handled an in-place
        /// single-row mutation (PrayerSaved → <see cref="PrayerCardViewModel.AddOrUpdatePrayerAsync"/>;
        /// PrayerDeleted → <see cref="PrayerCardViewModel.RemovePrayer"/>). Consumed
        /// (and cleared) by <c>PrayerCardsPage.OnAppearing</c> to skip the page-pop's
        /// full <see cref="SyncAsync()"/>. Without this, <c>ReloadPrayers</c> runs
        /// against an expanded 50+ row non-virtualized <c>BindableLayout</c> and tips
        /// the process into a memory-pressure jetsam. See
        /// <c>docs/research/BUG-80-prayer-card-realize-storm-handoff.md</c>.
        /// </summary>
        public bool SuppressNextOnAppearingSync { get; set; }

        #region Constructors

        // Optional override for unit tests that need to swap the per-card VM
        // (e.g. to inject a stub PrayerRowFactory for LoadPrayersAsync coverage).
        // Production leaves this null and uses the default CreateCardViewModel path.
        private readonly Func<PrayerCard, PrayerCardViewModel>? _cardVmFactory;

        public PrayerCardsViewModel(ICardService cardService, IPrayerService prayerService,
            IOnboardingService onboardingService, INavigationService navigationService,
            IAccessibilityService accessibilityService, ITagService tagService, ISettings settings,
            IBoxService boxService, IConfidentialAccessService confidentialAccessService, IMessenger messenger,
            Func<PrayerCard, PrayerCardViewModel>? cardVmFactory = null)
        {
            _cardService = cardService;
            _prayerService = prayerService;
            _onboardingService = onboardingService;
            _navigationService = navigationService;
            _accessibilityService = accessibilityService;
            _tagService = tagService;
            _settings = settings;
            _boxService = boxService;
            _confidentialAccessService = confidentialAccessService;
            _messenger = messenger;
            _cardVmFactory = cardVmFactory;

            AllPrayerCards = new ObservableCollection<PrayerCardViewModel>();
            _showCollectionsBanner = !settings.CollectionsBannerDismissed;

            // register commands
            NewCommand = new AsyncRelayCommand(NewPrayerCardAsync);
            CancelMultiSelectCommand = new RelayCommand(ExitMultiSelectMode);
            MoveSelectedCommand = new AsyncRelayCommand(MoveSelectedAsync);
            LongPressCardCommand = new RelayCommand<PrayerCardViewModel>(card =>
            {
                if (card != null) EnterMultiSelectMode(card);
            });
            EnterMultiSelectCommand = new RelayCommand(EnterMultiSelectModeFromToolbar);
            OpenCollectionsCommand = new AsyncRelayCommand(() => _navigationService.GoToAsync(Routes.BoxesPage));
            DismissCollectionsBannerCommand = new RelayCommand(() =>
            {
                _settings.CollectionsBannerDismissed = true;
                ShowCollectionsBanner = false;
            });
            ShowConfidentialCommand = new AsyncRelayCommand(ShowConfidentialAsync);

            // Cross-page CRUD signals — Cards displays counts and tag chips that derive
            // from every entity type, so subscribe to all of them. Weak refs auto-clean
            // on GC.
            //
            // BUG-80 (realize-storm class): card-, tag-, and box-level broadcasts pass
            // skipExpandedPrayerReload:true because none of them mutate any card's
            // prayer set — re-realizing rows on a 50+ row non-virtualized BindableLayout
            // is pure waste at scale and tips the process into jetsam. The card list,
            // tag chips, box sections, and active-prayer counts still update.
            // PrayerChangedMessage forwards Kind so SyncCoreAsync can apply the BUG-79
            // single-row skip (ApplyQueryAttributes already patched the row in place).
            // BulkChangedMessage stays unguarded — backup restore / deep-link import is
            // a full data replacement that legitimately requires per-card reload.
            _messenger.Register<PrayerCardsViewModel, PrayerCardChangedMessage>(this, (vm, _) => vm.SyncAsync(null, skipExpandedPrayerReload: true).SafeFireAndForget());
            _messenger.Register<PrayerCardsViewModel, PrayerChangedMessage>(this, (vm, msg) => vm.OnPrayerChangedAsync(msg).SafeFireAndForget());
            _messenger.Register<PrayerCardsViewModel, TagChangedMessage>(this, (vm, _) => vm.SyncAsync(null, skipExpandedPrayerReload: true).SafeFireAndForget());
            _messenger.Register<PrayerCardsViewModel, CardBoxChangedMessage>(this, (vm, _) => vm.SyncAsync(null, skipExpandedPrayerReload: true).SafeFireAndForget());
            _messenger.Register<PrayerCardsViewModel, BulkChangedMessage>(this, (vm, _) => vm.SyncAsync().SafeFireAndForget());
        }

        public PrayerCardsViewModel() : this(
            IPlatformApplication.Current!.Services.GetRequiredService<ICardService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IPrayerService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IOnboardingService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ITagService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ISettings>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IBoxService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IConfidentialAccessService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IMessenger>())
        { }

        #endregion

        #region Private Methods

        private async Task BuildCardTagLookupAsync()
        {
            var allJunctions = await PrayerCardTag.LoadAllAsync();
            // PerfLog.Log($"BuildCardTagLookupAsync.afterLoadAllJunctions junctions={allJunctions.Count}");
            var allPrayers = await _prayerService.GetAllPrayersAsync();

            // Build set of unanswered prayer IDs
            var unansweredIds = allPrayers
                .Where(p => !p.IsAnswered)
                .Select(p => p.Id)
                .ToHashSet();

            // Build prayer-to-card lookup
            var prayerToCard = allPrayers.ToDictionary(p => p.Id, p => p.PrayerCardId);

            // Build cardId → Set<tagId> from unanswered prayers only
            var lookup = new Dictionary<int, HashSet<int>>();
            foreach (var row in allJunctions.Where(r => r.PrayerRequestId > 0 && unansweredIds.Contains(r.PrayerRequestId)))
            {
                if (prayerToCard.TryGetValue(row.PrayerRequestId, out var cardId))
                {
                    if (!lookup.ContainsKey(cardId))
                        lookup[cardId] = new HashSet<int>();
                    lookup[cardId].Add(row.PrayerTagId);
                }
            }

            _cardTagIds = lookup;
        }

        private PrayerCardViewModel CreateCardViewModel(PrayerCard pc)
        {
            var vm = _cardVmFactory?.Invoke(pc)
                ?? new PrayerCardViewModel(pc, _cardService, _prayerService, _onboardingService,
                    _navigationService, _accessibilityService, _boxService, _settings);
            // Wire the back-reference so per-card IsExpanded can project over
            // ExpandedCardId, and ToggleExpandedAsync can write back through.
            vm.Parent = this;
            return vm;
        }

        private async Task NewPrayerCardAsync()
        {
            _onboardingService.Advance(); // CreateCard → NameCard (no-op if not at CreateCard)
            await _navigationService.GoToAsync(Routes.PrayerCardPage);
        }

        /// <summary>
        /// "Show confidential" overflow action (issue #253). Authenticates via the injected
        /// <see cref="IConfidentialAccessService"/>; on success the session unlocks and
        /// <see cref="RebuildSections"/> re-runs so effectively-Hidden cards reveal and
        /// effectively-LockedVisible cards unmask immediately. Re-locking (Window.Deactivated)
        /// is #254 — out of scope here.
        /// </summary>
        private async Task ShowConfidentialAsync()
        {
            var unlocked = await _confidentialAccessService.AuthenticateAsync("Show confidential cards");
            if (!unlocked) return;

            OnPropertyChanged(nameof(IsSessionUnlocked));
            foreach (var card in AllPrayerCards)
                card.RaiseLockStateChanged();
            RebuildSections();
        }

        #endregion

        #region Implemented Contract Methods

        void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.ContainsKey(Routes.QueryKeys.Deleted))
            {
                string? PrayerCardString = query[Routes.QueryKeys.Deleted].ToString();
                PrayerCardViewModel? matched = AllPrayerCards.FirstOrDefault<PrayerCardViewModel>(pc => pc.Identifier == PrayerCardString);

                if (matched != null)
                {
                    // Clear the singleton expand slot if it pointed at this card —
                    // otherwise IsExpanded stays "true" against a removed-from-list VM.
                    if (_expandedCardId == matched.Id) ExpandedCardId = null;
                    UnsubscribeFromPropertyChanges(matched);
                    AllPrayerCards.Remove(matched);
                    RebuildSections();
                }
            }
            else if (query.ContainsKey(Routes.QueryKeys.Saved))
            {
                // Stage the identifier; ConsumePendingSavedAsync (called from OnAppearing)
                // does the actual work on the lifecycle channel — keeps the View free of
                // the VM→View C# event whose handler raced the MauiRecyclerView adapter
                // snapshot on Galaxy Ultra.
                var id = query[Routes.QueryKeys.Saved].ToString();
                PendingSavedIdentifier = id;
                // Snapshot now: SyncAsync runs after this and may add the new card to
                // AllPrayerCards via its diff loop. We need to know whether the card was
                // *already* in the list before that, so the highlight decision in
                // ConsumePendingSavedAsync isn't fooled into the matched-edit branch.
                _pendingSavedWasAlreadyInList = AllPrayerCards.Any(c => c.Identifier == id);
            }
            else if (query.ContainsKey(Routes.QueryKeys.PrayerSaved) && query.ContainsKey(Routes.QueryKeys.ParentCardId))
            {
                if (int.TryParse(query[Routes.QueryKeys.PrayerSaved].ToString(), out int prayerId)
                    && int.TryParse(query[Routes.QueryKeys.ParentCardId].ToString(), out int parentCardId))
                {
                    // Remove from old card if this is a move.
                    if (query.TryGetValue(Routes.QueryKeys.OldCardId, out var oldVal)
                        && int.TryParse(oldVal?.ToString(), out int oldCardId))
                    {
                        var oldCard = AllPrayerCards.FirstOrDefault(card => card.Id == oldCardId);
                        oldCard?.RemovePrayer(prayerId);
                    }

                    var matched = AllPrayerCards.FirstOrDefault(card => card.Id == parentCardId);
                    if (matched != null)
                    {
                        // Always auto-expand the target so the user sees their saved
                        // prayer in context. The pre-refactor R-1 race (cascade
                        // collapsing unrelated cards) is structurally impossible under
                        // the ExpandedCardId design — only prev+next get signaled.
                        ExpandedCardId = matched.Id;
                        matched.AddOrUpdatePrayerAsync(prayerId).SafeFireAndForget();
                        matched.RefreshActivePrayerCount();
                        SuppressNextOnAppearingSync = true;
                        // Stage scroll-to machinery so OnAppearing scrolls to the target.
                        // The card is always already in the list, so wasAlreadyInList=true.
                        PendingSavedIdentifier = matched.Identifier;
                        _pendingSavedWasAlreadyInList = true;
                        _pendingSavedIsMoveTarget = true;
                    }
                }
            }
            else if (query.ContainsKey(Routes.QueryKeys.ImportedToExisting))
            {
                if (int.TryParse(query[Routes.QueryKeys.ImportedToExisting].ToString(), out int cardId))
                {
                    var matched = AllPrayerCards.FirstOrDefault(c => c.Id == cardId);
                    if (matched != null)
                    {
                        ExpandedCardId = matched.Id;
                        matched.LoadPrayersAsync().SafeFireAndForget();
                        matched.RefreshActivePrayerCount();
                        PendingSavedIdentifier = matched.Identifier;
                        _pendingSavedWasAlreadyInList = true;
                        _pendingSavedIsMoveTarget = true;
                        // Suppress the OnAppearing sync race; BulkChangedMessage drives the actual list refresh.
                        SuppressNextOnAppearingSync = true;
                    }
                }
            }
            else if (query.ContainsKey(Routes.QueryKeys.PrayerDeleted) && query.ContainsKey(Routes.QueryKeys.ParentCardId))
            {
                if (int.TryParse(query[Routes.QueryKeys.PrayerDeleted].ToString(), out int prayerId)
                    && int.TryParse(query[Routes.QueryKeys.ParentCardId].ToString(), out int parentCardId))
                {
                    var matched = AllPrayerCards.FirstOrDefault(card => card.Id == parentCardId);
                    matched?.RemovePrayer(prayerId);
                    SuppressNextOnAppearingSync = true;
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Consumes the saved-identifier signal staged by ApplyQueryAttributes. Returns
        /// the loaded/added card so the View can scroll/highlight, or null to mean
        /// "do not scroll" (no save pending, parse failure, DB miss, or matched-existing
        /// card — already in the rendered list, no scroll needed).
        /// </summary>
        public async Task<PrayerCardViewModel?> ConsumePendingSavedAsync()
        {
            // PerfLog.Log($"ConsumePendingSavedAsync.entry id={PendingSavedIdentifier} wasInList={_pendingSavedWasAlreadyInList}");
            var id = PendingSavedIdentifier;
            if (string.IsNullOrEmpty(id)) return null;
            var wasAlreadyInList = _pendingSavedWasAlreadyInList;
            var isMoveTarget = _pendingSavedIsMoveTarget;
            PendingSavedIdentifier = null;
            _pendingSavedWasAlreadyInList = false;
            _pendingSavedIsMoveTarget = false;

            var matched = AllPrayerCards.FirstOrDefault(c => c.Identifier == id);

            // Card already existed when ApplyQueryAttributes ran — either an edit or a move.
            if (matched != null && wasAlreadyInList)
            {
                if (isMoveTarget)
                {
                    // Move-prayer: scroll to the target so the user sees where their prayer
                    // landed. No highlight — the card isn't new. AddOrUpdatePrayerAsync
                    // (fire-and-forget in ApplyQueryAttributes) handles prayer-list refresh.
                    // PerfLog.Log("ConsumePendingSavedAsync move-target (matched, was-in-list, isMoveTarget)");
                    EnsureSectionExpandedFor(matched);
                    return matched;
                }
                // Edit case: just reload + rebuild; don't scroll (Galaxy Ultra adapter race).
                // PerfLog.Log("ConsumePendingSavedAsync edit-path (matched, was-in-list)");
                matched.Reload();
                RebuildSections();
                return null;
            }

            // New-card case where SyncAsync's diff loop already added the row before
            // we got here. The matched VM is the one we want to highlight + scroll to.
            // SyncAsync's tail RebuildSections already placed the new card into the
            // correct BoxSection — no second rebuild needed here. The cascade-collapse
            // of any previously-expanded card still happens via the IsExpanded handler;
            // _suppressIsExpandedRebuild only suppresses *that* handler's own rebuild,
            // and BoxSections doesn't need to be replaced just because IsExpanded
            // toggled on cards already in their final sections.
            if (matched != null && !wasAlreadyInList)
            {
                // PerfLog.Log("ConsumePendingSavedAsync new-via-sync (matched, NOT was-in-list)");
                // Load eagerly: writing ExpandedCardId doesn't go through the
                // lazy ToggleExpandedAsync path, so import-with-prayers would
                // reveal empty without an explicit load here.
                await matched.LoadPrayersAsync();
                ExpandedCardId = matched.Id;
                matched.IsHighlighted = true;
                EnsureSectionExpandedFor(matched);
                return matched;
            }

            // New-card case where AllPrayerCards still doesn't have the row (e.g., cold-load
            // path with a saved=Id in the query string). Load the card from DB and add it.
            if (!int.TryParse(id, out var cardId)) return null;
            try
            {
                // PerfLog.Log("ConsumePendingSavedAsync new-via-db before LoadAsync");
                var card = await PrayerCard.LoadAsync(cardId);
                if (card is null) return null;
                var newCard = CreateCardViewModel(card);
                SubscribeToPropertyChanges(newCard);
                AllPrayerCards.Add(newCard);
                // Eager load — same reason as the new-via-sync branch above.
                await newCard.LoadPrayersAsync();
                ExpandedCardId = newCard.Id;
                newCard.IsHighlighted = true;
                RebuildSections();
                EnsureSectionExpandedFor(newCard);
                return newCard;
            }
            catch (Exception ex)
            {
                Diagnostics.ResolveLog()?.Log("PrayerCardsViewModel.ConsumePendingSavedAsync", ex);
                return null;
            }
        }

        /// <summary>
        /// BUG-76: After a save, if the freshly-saved card belongs to a collapsed
        /// parent section the card is hidden inside the collapsed group even
        /// though `card.IsExpanded` is true. Auto-expand the parent and persist.
        /// Section is matched by BoxId — `BoxSectionViewModel.Contains()` returns
        /// false on a collapsed section because `ApplyExpansionState` clears the
        /// observable when collapsed.
        /// </summary>
        private void EnsureSectionExpandedFor(PrayerCardViewModel card)
        {
            var section = BoxSections.FirstOrDefault(s => s.BoxId == card.BoxId);
            if (section is null || section.IsExpanded) return;
            section.IsExpanded = true;
            SaveSectionExpansionState();
        }

        /// <summary>
        /// Single primitive that brings the VM in line with the data store. Diff-based —
        /// idempotent across first-call and Nth-call. Replaces the prior LoadAsync /
        /// RefreshAsync split. Triggered by <see cref="Helpers.PageSync.OnAppearingAsync"/>
        /// and by entity-change messenger broadcasts. Bursts coalesce via
        /// <see cref="_syncGate"/>; user-driven UI state (multi-select, search, tag chips)
        /// is preserved across syncs. The parameterless overload satisfies
        /// <see cref="ISyncableViewModel"/>; the parameterized overload accepts hints from
        /// entity-change handlers so <see cref="SyncCoreAsync"/> can skip the per-card
        /// prayer-row reload (BUG-79 / BUG-80 — see the comment at the foreach).
        /// </summary>
        public Task SyncAsync() => SyncAsync(changeKind: null, skipExpandedPrayerReload: false);

        public async Task SyncAsync(ChangeKind? changeKind, bool skipExpandedPrayerReload = false)
        {
            // Burst-scoped state: capture once before the gate runs. Per-iteration reset
            // would let a coalesced follow-up announce filter counts on the very first
            // cold load, defeating the suppress-on-first-sync contract.
            IsLoading = true;
            _suppressFilterAnnounce = !_hasSyncedOnce;
            try
            {
                await _syncGate.RunAsync(() => SyncCoreAsync(changeKind, skipExpandedPrayerReload));
                _hasSyncedOnce = true;
            }
            finally
            {
                _suppressFilterAnnounce = false;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Handles a single-prayer change broadcast. For a <see cref="ChangeKind.Created"/>
        /// prayer whose owning card is currently expanded, patch the new row into that card
        /// in place before reconciling the rest of the list.
        /// #82: "Save &amp; Add Another" saves without navigating, so
        /// <see cref="IQueryAttributable.ApplyQueryAttributes"/> never runs its
        /// <see cref="PrayerCardViewModel.AddOrUpdatePrayerAsync"/>, and the final
        /// navigating Save sets <see cref="SuppressNextOnAppearingSync"/> — so without this
        /// the intermediate Save+ rows stay invisible until a later full sync. Mirroring the
        /// in-place insert here keeps every Save+ row live. AddOrUpdatePrayerAsync is
        /// idempotent (existing-id check + re-entrancy gate), so the regular navigating-save
        /// path that also calls it still inserts exactly once. The BUG-79/80 skipReload guard
        /// in <see cref="SyncCoreAsync"/> stays intact — this is the cheap single-row patch,
        /// never a full ReloadPrayers().
        /// </summary>
        private async Task OnPrayerChangedAsync(PrayerChangedMessage msg)
        {
            if (msg.Kind == ChangeKind.Created)
            {
                var matched = AllPrayerCards.FirstOrDefault(c => c.Id == msg.CardId);
                if (matched is { IsExpanded: true })
                {
                    // Guard the in-place patch so a single prayer-load failure can't
                    // skip the reconciliation below — SyncAsync must always run (the
                    // pre-#82 handler called it unconditionally). On failure the card
                    // list still reconciles and the next OnAppearing full sync recovers
                    // the missed row.
                    try { await matched.AddOrUpdatePrayerAsync(msg.PrayerId); }
                    catch (Exception ex)
                    {
                        Diagnostics.ResolveLog()?.Log("PrayerCardsViewModel.OnPrayerChangedAsync", ex);
                    }
                }
            }
            await SyncAsync(msg.Kind);
        }

        private async Task SyncCoreAsync(ChangeKind? changeKind, bool skipExpandedPrayerReload)
        {
            // PerfLog.Log("SyncCoreAsync.entry");
            // Service caches are auto-invalidated by their own mutation methods (Slice 2);
            // no defensive InvalidateCache call needed here.
            // Cards + boxes are independent reads — parallelize to cut sequential await
            // latency on cold launch (each is a SQLite + EF round-trip).
            var cardsTask = _cardService.GetCardsAsync();
            var boxesTask = _boxService.GetBoxesAsync();
            await Task.WhenAll(cardsTask, boxesTask);
            var cards = cardsTask.Result;
            _boxes = boxesTask.Result;
            // PerfLog.Log($"SyncCoreAsync.afterGetCards count={cards.Count}");
            // PerfLog.Log($"SyncCoreAsync.afterGetBoxes count={_boxes.Count}");
            var freshIds = cards.Select(c => c.Id).ToHashSet();
            var currentIds = AllPrayerCards.Select(c => c.Id).ToHashSet();

            // Remove deleted cards
            var toRemove = AllPrayerCards.Where(c => !freshIds.Contains(c.Id)).ToList();
            foreach (var vm in toRemove)
            {
                // Clear the singleton expand slot if it pointed at a removed card.
                if (_expandedCardId == vm.Id) ExpandedCardId = null;
                UnsubscribeFromPropertyChanges(vm);
                AllPrayerCards.Remove(vm);
            }

            // Add new cards
            foreach (var card in cards.Where(c => !currentIds.Contains(c.Id)))
            {
                var vm = CreateCardViewModel(card);
                SubscribeToPropertyChanges(vm);
                AllPrayerCards.Add(vm);
                // PerfLog.Log($"SyncCore.addNewCard id={vm.Id} title=\"{vm.Title}\" IsExpanded={vm.IsExpanded}");
            }
            // PerfLog.Log("SyncCoreAsync.afterDiffLoop");

            // Per-card prayer-row reload is the realize-storm engine: a 50+ row
            // non-virtualized BindableLayout re-realized inside the foreach tips
            // the process into jetsam. Skip it whenever we know the broadcast
            // can't have left an expanded card stale.
            //
            // BUG-79: any single-row PrayerChangedMessage. ApplyQueryAttributes
            // (PrayerSaved → AddOrUpdatePrayerAsync; PrayerDeleted → RemovePrayer)
            // has already patched the row in place on navigating saves/deletes. On
            // the non-navigating "Save & Add Another" path (#82) the patch is done by
            // OnPrayerChangedAsync — the same in-place AddOrUpdatePrayerAsync for a
            // Created prayer on the expanded card. For Created broadcasts to a
            // non-expanded card (QuickAdd writes to the system "Quick Add" card from
            // the Home tab), CardsPage's next OnAppearing runs SyncAsync() with no
            // skip and reconciles fresh.
            //
            // BUG-80: sibling broadcasts that don't mutate any card's prayer set
            // (PrayerCardChangedMessage, TagChangedMessage, CardBoxChangedMessage)
            // pass skipExpandedPrayerReload:true at registration. BulkChangedMessage
            // is intentionally NOT in that set — backup restore / deep-link import
            // is a full data replacement that legitimately requires the reload.
            //
            // RefreshActivePrayerCount is a cheap projection over already-loaded
            // prayers and runs unconditionally. BuildCardTagLookupAsync below also
            // runs unconditionally so tag chips stay correct on TagChangedMessage.
            //
            // See docs/research/BUG-80-prayer-card-realize-storm-handoff.md.
            var skipReload = changeKind != null || skipExpandedPrayerReload;
            foreach (var vm in AllPrayerCards)
            {
                vm.RefreshActivePrayerCount();
                if (vm.IsExpanded && !skipReload)
                    vm.ReloadPrayers();
            }

            // Rebuild tag filter data
            await BuildCardTagLookupAsync();

            var tags = await _tagService.GetTagsAsync();
            // PerfLog.Log($"SyncCoreAsync.afterGetTagsAsync tags={tags.Count}");

            // Build the proposed chip sequence in fresh-tags order, reusing existing
            // chip instances by tag-id so user selection state (IsSelected) survives
            // unchanged across a sync. Then ref-equality short-circuit: when the
            // proposed sequence's tag-ids match the current sequence's tag-ids in
            // order, take the early-out — no replace, no chip-strip re-inflation.
            //
            // Without this short-circuit, a single ObservableCollection assignment
            // on every messenger-driven sync regresses Mark-Answered (#10) from its
            // 63ms baseline: the CollectionView re-realizes every chip Border +
            // nested template tree each cycle.
            var existingChipsByTagId = AvailableTags.ToDictionary(c => c.Tag.Id);
            var proposedChips = new List<TagFilterChipViewModel>(tags.Count);
            var addedCount = 0;
            foreach (var tag in tags)
            {
                if (existingChipsByTagId.TryGetValue(tag.Id, out var existing))
                    proposedChips.Add(existing);
                else
                {
                    proposedChips.Add(new TagFilterChipViewModel(tag, _ => ApplyFilter()));
                    addedCount++;
                }
            }
            // existing.Count - reused-chip count = deleted-tag count.
            var removedCount = AvailableTags.Count - (proposedChips.Count - addedCount);
            // PerfLog.Log($"SyncCoreAsync.afterChipRemove removed={removedCount}");
            // PerfLog.Log($"SyncCoreAsync.afterChipAddLoop added={addedCount}");

            if (!IsSameTagSequence(AvailableTags, proposedChips))
            {
                AvailableTags = new ObservableCollection<TagFilterChipViewModel>(proposedChips);
                OnPropertyChanged(nameof(HasTags));
            }
            // PerfLog.Log("SyncCoreAsync.afterChipDiff");

            RebuildSections();
            // PerfLog.Log("SyncCoreAsync.exit");
        }

        /// <summary>
        /// Returns true iff <paramref name="existing"/> and <paramref name="fresh"/> contain
        /// the same tag IDs in the same order. Used as the messenger-driven-sync early-out
        /// guard before replacing the <see cref="AvailableTags"/> collection.
        /// </summary>
        private static bool IsSameTagSequence(
            IList<TagFilterChipViewModel> existing,
            IReadOnlyList<TagFilterChipViewModel> fresh)
        {
            if (existing.Count != fresh.Count) return false;
            for (int i = 0; i < existing.Count; i++)
                if (existing[i].Tag.Id != fresh[i].Tag.Id) return false;
            return true;
        }

        private static IOrderedEnumerable<PrayerCardViewModel> SortCards(IEnumerable<PrayerCardViewModel> cards) =>
            cards.OrderByDescending(c => c.IsFavorite).ThenBy(c => c.Title);

        /// <summary>
        /// Parses the persisted expanded-section setting into a HashSet of BoxIds.
        /// Empty string → empty set → all collapsed by default.
        /// </summary>
        private HashSet<int> GetSavedExpandedIds()
        {
            var raw = _settings.ExpandedSectionIds;
            if (string.IsNullOrEmpty(raw)) return new HashSet<int>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                       .Where(id => id.HasValue)
                       .Select(id => id!.Value)
                       .ToHashSet();
        }

        /// <summary>
        /// Persists the current expanded-section state to settings.
        /// Called from the code-behind when a section header is tapped.
        /// </summary>
        public void SaveSectionExpansionState()
        {
            var expandedIds = BoxSections
                .Where(s => s.IsExpanded)
                .Select(s => s.BoxId.ToString());
            _settings.ExpandedSectionIds = string.Join(",", expandedIds);
        }

        /// <summary>
        /// Rebuilds sections from AllPrayerCards grouped by BoxId.
        /// Preserves existing BoxSectionViewModel instances (and their user expansion state)
        /// when the same BoxId is present. New sections use persisted expansion state
        /// (collapsed by default on first launch).
        /// Sort order: Unboxed → user boxes (A→Z by name) → System → Archived.
        /// </summary>
        private void RebuildSections()
        {
            if (_isSorting) return;
            _isSorting = true;
            // PerfLog.Log($"RebuildSections.entry cards={AllPrayerCards.Count}");
            try
            {
                // Issue #253: thread each card's owning box in (Box drives
                // GetEffectiveProtectionMode for box-cascaded protection), then exclude
                // effectively-Hidden cards from the list entirely while the confidential
                // session is locked. LockedVisible cards stay in the list — DisplayTitle
                // (on PrayerCardViewModel) handles their masked rendering.
                foreach (var card in AllPrayerCards)
                    card.Box = _boxes.FirstOrDefault(b => b.Id == card.BoxId);

                var renderableCards = IsSessionUnlocked
                    ? AllPrayerCards
                    : AllPrayerCards.Where(c =>
                        PrayerCard.GetEffectiveProtectionMode(c.Card, c.Box) != CardProtectionMode.Hidden);

                var cardsByBox = renderableCards
                    .GroupBy(c => c.BoxId)
                    .ToDictionary(g => g.Key, g => SortCards(g).ToList());

                // Preserve existing sections to retain user expansion state
                var existingSections = BoxSections.ToDictionary(s => s.BoxId);
                var savedExpandedIds = GetSavedExpandedIds();
                var sections = new List<BoxSectionViewModel>();

                // Helper: reuse existing section or create new one
                BoxSectionViewModel GetOrCreate(int boxId, Func<BoxSectionViewModel> factory)
                {
                    if (existingSections.TryGetValue(boxId, out var existing))
                        return existing;
                    return factory();
                }

                // 1. Unboxed section (BoxId == 0)
                var unboxedCards = cardsByBox.GetValueOrDefault(0);
                if (unboxedCards is { Count: > 0 })
                {
                    var unboxed = GetOrCreate(0, () => new BoxSectionViewModel(
                        defaultExpanded: savedExpandedIds.Contains(0)));
                    unboxed.SetCards(unboxedCards);
                    sections.Add(unboxed);
                }

                // 2. User boxes (not system, sorted by name) — always shown, even when empty
                foreach (var box in _boxes.Where(b => !b.IsSystem).OrderBy(b => b.Name))
                {
                    var boxCards = cardsByBox.GetValueOrDefault(box.Id) ?? new List<PrayerCardViewModel>();
                    var section = GetOrCreate(box.Id, () => new BoxSectionViewModel(box,
                        defaultExpanded: savedExpandedIds.Contains(box.Id)));
                    section.SetCards(boxCards);
                    sections.Add(section);
                }

                // 3. System box
                var systemBox = _boxes.FirstOrDefault(b => b.SystemKey == CardBox.SystemKeySystem);
                if (systemBox != null)
                {
                    var systemCards = cardsByBox.GetValueOrDefault(systemBox.Id);
                    if (systemCards is { Count: > 0 })
                    {
                        var systemSection = GetOrCreate(systemBox.Id, () => new BoxSectionViewModel(systemBox,
                            defaultExpanded: savedExpandedIds.Contains(systemBox.Id)));
                        systemSection.SetCards(systemCards);
                        sections.Add(systemSection);
                    }
                }

                // 4. Archived box (always shown even when empty, collapsed by default)
                var archivedBox = _boxes.FirstOrDefault(b => b.SystemKey == CardBox.SystemKeyArchived);
                if (archivedBox != null)
                {
                    var archivedSection = GetOrCreate(archivedBox.Id, () => new BoxSectionViewModel(archivedBox,
                        defaultExpanded: savedExpandedIds.Contains(archivedBox.Id)));
                    archivedSection.SetCards(cardsByBox.GetValueOrDefault(archivedBox.Id) ?? new List<PrayerCardViewModel>());
                    sections.Add(archivedSection);
                }

                // Slice 6b: skip the BoxSections replacement when GetOrCreate reused every
                // section (no box added or removed — the typical save-flow case). Replacement
                // raises PropertyChanged on the grouped ItemsSource and forces a RecyclerView
                // re-inflate cascade on Android. Per-section card mutations surface via SetCards.
                // Hole: a CardBox.Name rename keeps the same section instance, so the section
                // header won't update unless BoxSectionViewModel observes box-property changes.
                // No rename UI today; revisit if one is added.
                if (!SectionListsReferenceEqual(sections, BoxSections))
                {
                    BoxSections = new ObservableCollection<BoxSectionViewModel>(sections);
                    OnPropertyChanged(nameof(HasNoSections));
                }
            }
            finally
            {
                _isSorting = false;
            }

            // PerfLog.Log("RebuildSections.before ApplyFilter");
            ApplyFilter();
            // PerfLog.Log("RebuildSections.exit");
        }

        private static bool SectionListsReferenceEqual(
            IList<BoxSectionViewModel> proposed,
            IList<BoxSectionViewModel> current)
        {
            if (proposed.Count != current.Count) return false;
            for (int i = 0; i < proposed.Count; i++)
                if (!ReferenceEquals(proposed[i], current[i])) return false;
            return true;
        }

        private void ApplyFilter()
        {
            var hasSearch = !string.IsNullOrWhiteSpace(_searchText);
            var searchQuery = _searchText?.Trim() ?? string.Empty;

            var selectedTagIds = AvailableTags
                .Where(c => c.IsSelected)
                .Select(c => c.Tag.Id)
                .ToHashSet();
            var hasTagFilter = selectedTagIds.Count > 0;
            var hasAnyFilter = hasSearch || hasTagFilter;

            // Issue #253: same hidden-exclusion as RebuildSections — a search/tag filter
            // must not surface an effectively-Hidden card while the session is locked.
            var renderableCards = IsSessionUnlocked
                ? AllPrayerCards
                : AllPrayerCards.Where(c =>
                    PrayerCard.GetEffectiveProtectionMode(c.Card, c.Box) != CardProtectionMode.Hidden);

            // Group once, look up per section — O(cards + sections) instead of O(sections × cards)
            var cardsByBox = renderableCards
                .GroupBy(c => c.BoxId)
                .ToDictionary(g => g.Key, g => SortCards(g).ToList());

            var totalVisible = 0;

            foreach (var section in BoxSections)
            {
                IEnumerable<PrayerCardViewModel> sectionCards =
                    cardsByBox.GetValueOrDefault(section.BoxId) ?? new List<PrayerCardViewModel>();

                if (hasSearch)
                    sectionCards = sectionCards.Where(c =>
                        c.Title?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ?? false);

                if (hasTagFilter)
                    sectionCards = sectionCards.Where(c =>
                        _cardTagIds.TryGetValue(c.Id, out var tagIds) &&
                        selectedTagIds.Overlaps(tagIds));

                var filteredCards = sectionCards.ToList();
                section.SetCards(filteredCards);
                totalVisible += filteredCards.Count;

                if (hasAnyFilter && filteredCards.Count > 0)
                    section.FilterExpand();
                else if (!hasAnyFilter)
                    section.RestoreUserExpansionState();
            }

            if (!_suppressFilterAnnounce)
            {
                _accessibilityService.NotifyLayoutChanged();
                AnnounceFilterCountDebounced(totalVisible);
            }
        }

        private void AnnounceFilterCountDebounced(int count)
        {
            _filterAnnounceCts?.Cancel();
            _filterAnnounceCts?.Dispose();
            _filterAnnounceCts = new CancellationTokenSource();
            var token = _filterAnnounceCts.Token;
            Task.Delay(400, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    _accessibilityService.Announce($"Showing {count} cards");
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        private void SubscribeToPropertyChanges(PrayerCardViewModel card)
        {
            // The IsExpanded cascade-collapse handler that used to live here was
            // deleted in Commit 3 — the singleton invariant is enforced structurally
            // by ExpandedCardId. Accessibility announce + post-collapse RebuildSections
            // moved into the ExpandedCardId setter.
            void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(PrayerCardViewModel.Title))
                {
                    RebuildSections();
                }
            }

            card.PropertyChanged += Handler;
            _cardHandlers[card] = Handler;
        }

        private void UnsubscribeFromPropertyChanges(PrayerCardViewModel card)
        {
            if (_cardHandlers.Remove(card, out var handler))
                card.PropertyChanged -= handler;
            // Break the parent ↔ card back-reference so a removed VM doesn't keep
            // the page VM rooted via in-flight SafeFireAndForget continuations.
            card.Parent = null;
        }

        #endregion

        #region Multi-Select

        private DateTime _multiSelectEnteredAt;

        /// <summary>Enters multi-select mode from the toolbar without pre-selecting a card.
        /// Provides an accessible alternative to long-press for screen reader users.</summary>
        private void EnterMultiSelectModeFromToolbar()
        {
            if (IsMultiSelectMode) return;
            _multiSelectEnteredAt = DateTime.UtcNow;
            IsMultiSelectMode = true;
            NotifySelectionCount();
            _accessibilityService.Announce("Selection mode. Tap cards to select them.");
        }

        /// <summary>Enters multi-select mode with the given card pre-selected.</summary>
        public void EnterMultiSelectMode(PrayerCardViewModel card)
        {
            if (IsMultiSelectMode) return;
            _multiSelectEnteredAt = DateTime.UtcNow;
            IsMultiSelectMode = true;
            card.IsMultiSelected = true;
            NotifySelectionCount();
            _accessibilityService.Announce("Selection mode. Tap cards to select them.");
        }

        /// <summary>Toggles a card's selection state while in multi-select mode.</summary>
        public void ToggleCardSelection(PrayerCardViewModel card)
        {
            if (!IsMultiSelectMode) return;
            // iOS fires tap on finger-up after long-press — suppress the immediate
            // deselect of the card that just triggered multi-select entry.
            if ((DateTime.UtcNow - _multiSelectEnteredAt).TotalMilliseconds < 300) return;
            card.IsMultiSelected = !card.IsMultiSelected;
            NotifySelectionCount();
            _accessibilityService.Announce(SelectedCountText);
        }

        private void ExitMultiSelectMode()
        {
            foreach (var card in AllPrayerCards)
                card.IsMultiSelected = false;
            IsMultiSelectMode = false;
            _accessibilityService.Announce("Selection cancelled");
        }

        private const string _archiveOptionLabel = "Archive";

        private async Task MoveSelectedAsync()
        {
            var selected = AllPrayerCards.Where(c => c.IsMultiSelected).ToList();
            if (selected.Count == 0) return;

            // Build picker options: user boxes + "Loose Cards" + "Archive"
            var boxes = await _boxService.GetBoxesAsync();
            var options = new List<string> { BoxStrings.Unorganized };
            var userBoxes = boxes.Where(b => !b.IsSystem).OrderBy(b => b.Name).ToList();
            options.AddRange(userBoxes.Select(b => b.Name));
            options.Add(_archiveOptionLabel);

            var result = await _navigationService.DisplayActionSheetAsync(
                $"Move {selected.Count} card{(selected.Count == 1 ? "" : "s")} to…",
                "Cancel", null, options.ToArray());

            if (result is null or "Cancel") return;

            // Resolve the selected box ID
            int targetBoxId;
            if (result == BoxStrings.Unorganized)
            {
                targetBoxId = 0;
            }
            else if (result == _archiveOptionLabel)
            {
                targetBoxId = _settings.ArchivedFolderId;
                // System cards cannot be archived
                selected = selected.Where(c => !c.IsSystem).ToList();
                if (selected.Count == 0) return;

                var confirmed = await _navigationService.DisplayConfirmAsync(
                    "Archive Cards?",
                    $"Archive {selected.Count} card{(selected.Count == 1 ? "" : "s")}? You can find them in your Archived Cards collection.",
                    "Archive",
                    "Cancel");
                if (!confirmed) return;
            }
            else
            {
                var targetBox = userBoxes.FirstOrDefault(b => b.Name == result);
                if (targetBox == null) return;
                targetBoxId = targetBox.Id;
            }

            // Batch assign
            foreach (var card in selected)
                await _cardService.AssignBoxAsync(card.Card, targetBoxId);

            if (result == _archiveOptionLabel)
                _accessibilityService.Announce(
                    $"Archived {selected.Count} card{(selected.Count == 1 ? "" : "s")}");
            else
                _accessibilityService.Announce(
                    $"Moved {selected.Count} card{(selected.Count == 1 ? "" : "s")} to {result}");

            ExitMultiSelectMode();
            RebuildSections();
        }

        private void NotifySelectionCount()
        {
            OnPropertyChanged(nameof(SelectedCardCount));
            OnPropertyChanged(nameof(SelectedCountText));
        }

        #endregion
    }
}
