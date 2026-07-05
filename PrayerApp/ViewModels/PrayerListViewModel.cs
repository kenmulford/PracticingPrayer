using PrayerApp.Helpers;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace PrayerApp.ViewModels
{
    public enum FilterStatus { Active, Answered, All, Overdue }

    public class PrayerListViewModel : ObservableObject, IQueryAttributable, ISyncableViewModel
    {
        private List<Prayer> _prayerList = new();
        private readonly IPrayerService _prayerService;
        private readonly ICardService _cardService;
        private readonly ITagService _tagService;
        private readonly INavigationService _navigationService;
        private readonly IAccessibilityService _accessibilityService;
        private readonly ISettings _settings;
        private readonly IPrayerSelectionService _selectionService;
        private readonly IMessenger _messenger;
        private readonly IBoxService _boxService;
        private readonly IConfidentialAccessService _confidentialAccessService;
        private Dictionary<int, string> _cardTitleLookup = new();
        // Issue #255: cardId → owning PrayerCard, refreshed each SyncAsync alongside
        // _cardTitleLookup, so ApplyFilter can resolve GetEffectiveProtectionMode without
        // an extra per-filter service round-trip.
        private Dictionary<int, PrayerCard> _cardLookup = new();
        private IReadOnlyList<CardBox> _boxes = Array.Empty<CardBox>();
        private CancellationTokenSource? _filterAnnounceCts;

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                    _accessibilityService.Announce(value ? "Loading" : "Content loaded");
            }
        }

        // requestId → set of tagIds assigned to that request (for chip filter)
        private Dictionary<int, HashSet<int>> _requestTagIds = new();

        // prayer IDs considered overdue (not prayed in 30+ days)
        private HashSet<int> _overdueIds = new();

        // Suppress screen reader announcements during bulk loads
        private bool _suppressAnnounce;
        private bool _suppressFilter;
        private readonly Dictionary<PrayerRequestDetailViewModel, System.ComponentModel.PropertyChangedEventHandler> _prayerHandlers = new();

        private readonly Helpers.SingleFlightGate _syncGate = new();

        // Pre-selected tag from tag detail navigation (F6)
        private int _preselectedTagId;

        // Full unfiltered backing store — manipulated by IQueryAttributable
        public ObservableCollection<PrayerRequestDetailViewModel> AllPrayers { get; } = new();

        private ObservableCollection<PrayerRequestDetailViewModel> _filteredPrayers = new();
        /// <summary>
        /// Filtered + sorted view bound to the CollectionView. Replaced (not mutated) on each
        /// filter pass to avoid iOS UICollectionView layout desync from rapid CollectionChanged events.
        /// </summary>
        public ObservableCollection<PrayerRequestDetailViewModel> FilteredPrayers
        {
            get => _filteredPrayers;
            private set => SetProperty(ref _filteredPrayers, value);
        }

        // True when the filtered view holds at least one active (unanswered) prayer —
        // gates StartPrayerTimeCommand. Recomputed in ApplyFilter (the single place
        // FilteredPrayers is rebuilt).
        private bool HasActiveInView => FilteredPrayers.Any(p => !p.IsAnswered);

        // Tag chips
        public ObservableCollection<TagFilterChipViewModel> AvailableTags { get; } = new();
        public bool HasTags => AvailableTags.Count > 0;

        // Search
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        // Status toggle
        private FilterStatus _statusFilter = FilterStatus.Active;
        public FilterStatus StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value))
                {
                    OnPropertyChanged(nameof(IsActiveSelected));
                    OnPropertyChanged(nameof(IsAnsweredSelected));
                    OnPropertyChanged(nameof(IsAllSelected));
                    OnPropertyChanged(nameof(IsOverdueSelected));
                    ApplyFilter();
                }
            }
        }
        public bool IsActiveSelected   => StatusFilter == FilterStatus.Active;
        public bool IsAnsweredSelected => StatusFilter == FilterStatus.Answered;
        public bool IsAllSelected      => StatusFilter == FilterStatus.All;
        public bool IsOverdueSelected  => StatusFilter == FilterStatus.Overdue;

        public ICommand NewCommand { get; }
        public ICommand SetStatusCommand { get; }
        public ICommand StartPrayerTimeCommand { get; }

        /// <summary>
        /// Factory for the prayer-row view models built in <see cref="BuildViewModel"/>.
        /// Default chains through <c>IPlatformApplication.Current.Services</c> (via the
        /// <c>PrayerRequestDetailViewModel(Prayer)</c> ctor); unit tests override with a
        /// stub-services factory so rows can be built without a live DI container.
        /// Mirrors <c>PrayerCardViewModel.PrayerRowFactory</c>.
        /// </summary>
        public Func<Prayer, PrayerRequestDetailViewModel> PrayerRowFactory { get; set; }
            = prayer => new PrayerRequestDetailViewModel(prayer);

        public PrayerListViewModel(IPrayerService prayerService, ICardService cardService, ITagService tagService,
            INavigationService navigationService, IAccessibilityService accessibilityService, ISettings settings,
            IPrayerSelectionService selectionService, IMessenger messenger,
            IBoxService boxService, IConfidentialAccessService confidentialAccessService)
        {
            _prayerService = prayerService;
            _cardService   = cardService;
            _tagService    = tagService;
            _navigationService = navigationService;
            _accessibilityService = accessibilityService;
            _settings = settings;
            _selectionService = selectionService;
            _messenger = messenger;
            _boxService = boxService;
            _confidentialAccessService = confidentialAccessService;

            // Any change to the backing store re-runs the filter (suppressed during bulk loads)
            AllPrayers.CollectionChanged += (_, _) => { if (!_suppressFilter) ApplyFilter(); };

            // Commands
            NewCommand = new AsyncRelayCommand(NewPrayerAsync);
            StartPrayerTimeCommand = new AsyncRelayCommand(StartPrayerTimeAsync, () => HasActiveInView);
            SetStatusCommand = new RelayCommand<string>(s =>
            {
                StatusFilter = s switch
                {
                    "Answered" => FilterStatus.Answered,
                    "All"      => FilterStatus.All,
                    "Overdue"  => FilterStatus.Overdue,
                    _          => FilterStatus.Active
                };
            });

            // Prayers list reflects prayer + card title + tag chip state. Any of those
            // changing elsewhere should trigger a sync.
            _messenger.Register<PrayerListViewModel, PrayerChangedMessage>(this, (vm, _) => vm.SyncAsync().SafeFireAndForget());
            _messenger.Register<PrayerListViewModel, PrayerCardChangedMessage>(this, (vm, _) => vm.SyncAsync().SafeFireAndForget());
            _messenger.Register<PrayerListViewModel, TagChangedMessage>(this, (vm, _) => vm.SyncAsync().SafeFireAndForget());
            _messenger.Register<PrayerListViewModel, BulkChangedMessage>(this, (vm, _) => vm.SyncAsync().SafeFireAndForget());

            // Issue #297: re-lock while this page is on screen must re-exclude effectively
            // protected prayers immediately. ApplyFilter already reads
            // _confidentialAccessService.IsSessionUnlocked live, and no underlying data
            // changed — a synchronous re-filter (no DB round-trip) mirrors
            // PrayerCardsViewModel.OnSessionRelocked (PrayerCardsViewModel.cs:332).
            _messenger.Register<PrayerListViewModel, SessionRelockedMessage>(this, (vm, _) => vm.ApplyFilter());
        }

        public PrayerListViewModel() : this(
            IPlatformApplication.Current!.Services.GetRequiredService<IPrayerService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ICardService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ITagService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ISettings>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IPrayerSelectionService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IMessenger>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IBoxService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IConfidentialAccessService>())
        { }

        public async Task SyncAsync()
        {
            IsLoading = true;
            try { await _syncGate.RunAsync(SyncCoreAsync); }
            finally { IsLoading = false; }
        }

        private async Task SyncCoreAsync()
        {
            _suppressAnnounce = true;
            _suppressFilter = true;
            try
            {
                var cardsTask = _cardService.GetCardsAsync();
                var boxesTask = _boxService.GetBoxesAsync();
                await Task.WhenAll(cardsTask, boxesTask);
                var cards = cardsTask.Result;
                _boxes = boxesTask.Result;
                _cardTitleLookup = cards.ToDictionary(c => c.Id, c => c.Title ?? string.Empty);
                // Issue #255: cardId → PrayerCard, so ApplyFilter can resolve
                // GetEffectiveProtectionMode(card, box) per prayer without a service call.
                _cardLookup = cards.ToDictionary(c => c.Id);

                _prayerList = (await _prayerService.GetAllPrayersAsync()).ToList();
                _requestTagIds = await BuildRequestTagLookupAsync();

                var overdue = await _prayerService.GetOverduePrayersAsync(_settings.OverdueDayThreshold);
                _overdueIds = overdue.Select(p => p.Id).ToHashSet();

                // Diff AllPrayers — works for first call (empty → "add everything") and Nth call.
                var freshIds = _prayerList.Select(p => p.Id).ToHashSet();
                var toRemove = AllPrayers.Where(p => !freshIds.Contains(p.Id)).ToList();
                foreach (var vm in toRemove)
                {
                    UnsubscribeFromPropertyChanges(vm);
                    AllPrayers.Remove(vm);
                }

                var prayerLookup = _prayerList.ToDictionary(p => p.Id);
                foreach (var vm in AllPrayers)
                {
                    if (prayerLookup.TryGetValue(vm.Id, out var fresh))
                    {
                        vm.Title = fresh.Title;
                        vm.IsAnswered = fresh.IsAnswered;
                        vm.CardTitle = _cardTitleLookup.TryGetValue(fresh.PrayerCardId, out var t) ? t : string.Empty;
                    }
                }

                var currentIds = AllPrayers.Select(p => p.Id).ToHashSet();
                foreach (var p in _prayerList.Where(p => !currentIds.Contains(p.Id)))
                    AllPrayers.Add(BuildViewModel(p));

                // Diff AvailableTags (chip filter)
                var allTags = (await _tagService.GetTagsAsync()).ToList();
                var freshTagIds = allTags.Select(t => t.Id).ToHashSet();
                var existingTagIds = AvailableTags.Select(c => c.Tag.Id).ToHashSet();

                var chipsToRemove = AvailableTags.Where(c => !freshTagIds.Contains(c.Tag.Id)).ToList();
                foreach (var chip in chipsToRemove)
                    AvailableTags.Remove(chip);

                foreach (var tag in allTags.Where(t => !existingTagIds.Contains(t.Id)))
                    AvailableTags.Add(new TagFilterChipViewModel(tag, _ => ApplyFilter()));
                OnPropertyChanged(nameof(HasTags));

                ApplyPreselectedTag();
                ApplyFilter();
            }
            finally
            {
                _suppressFilter = false;
                _suppressAnnounce = false;
            }
        }

        #region IQueryAttributable

        void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.ContainsKey("tagId"))
            {
                if (int.TryParse(query["tagId"].ToString(), out int tagId))
                {
                    _preselectedTagId = tagId;
                    ApplyPreselectedTag();
                    ApplyFilter();
                }
            }
            else if (query.ContainsKey("filter"))
            {
                var filter = query["filter"].ToString();
                StatusFilter = filter switch
                {
                    "overdue" => FilterStatus.Overdue,
                    _         => FilterStatus.Active
                };
            }
            else if (query.ContainsKey("deleted"))
            {
                var id = query["deleted"].ToString();
                var matched = AllPrayers.FirstOrDefault(p => p.Identifier == id);
                if (matched != null)
                {
                    UnsubscribeFromPropertyChanges(matched);
                    AllPrayers.Remove(matched); // CollectionChanged → ApplyFilter
                }
            }
            else if (query.ContainsKey("saved"))
            {
                HandleSavedAsync(query["saved"].ToString()).SafeFireAndForget();
            }
        }

        #endregion

        #region Private helpers

        private async Task HandleSavedAsync(string? id)
        {
            // Rebuild tag lookup so chip filter reflects tag changes made on the detail page
            _requestTagIds = await BuildRequestTagLookupAsync();

            // Refresh tag chips — add new tags, remove deleted tags
            var allTags = (await _tagService.GetTagsAsync()).ToList();
            var freshTagIds = allTags.Select(t => t.Id).ToHashSet();
            var existingIds = AvailableTags.Select(c => c.Tag.Id).ToHashSet();

            // Remove chips for tags that no longer exist
            var chipsToRemove = AvailableTags.Where(c => !freshTagIds.Contains(c.Tag.Id)).ToList();
            foreach (var chip in chipsToRemove)
                AvailableTags.Remove(chip);

            // Add chips for newly created tags
            foreach (var tag in allTags.Where(t => !existingIds.Contains(t.Id)))
                AvailableTags.Add(new TagFilterChipViewModel(tag, _ => ApplyFilter()));
            OnPropertyChanged(nameof(HasTags));

            var matched = AllPrayers.FirstOrDefault(p => p.Identifier == id);
            if (matched != null)
            {
                if (int.TryParse(id, out int prayerId))
                {
                    var freshPrayer = await Prayer.LoadAsync(prayerId);
                    if (freshPrayer is not null)
                    {
                        // Refresh card lookup in case the prayer moved to a different card
                        var cards = await _cardService.GetCardsAsync();
                        _cardTitleLookup = cards.ToDictionary(c => c.Id, c => c.Title ?? string.Empty);
                        _cardLookup = cards.ToDictionary(c => c.Id);

                        // Update all list-visible properties from the awaited DB load
                        // (replaces fire-and-forget Reload which raced with ApplyFilter)
                        matched.Title = freshPrayer.Title;
                        matched.IsAnswered = freshPrayer.IsAnswered;
                        matched.CardTitle = _cardTitleLookup.TryGetValue(freshPrayer.PrayerCardId, out var t) ? t : string.Empty;
                    }
                }
                ApplyFilter();
            }
            else
            {
                await AddNewPrayerAsync(id);
            }
        }

        private void ApplyPreselectedTag()
        {
            if (_preselectedTagId <= 0) return;

            // Deselect all chips first
            foreach (var chip in AvailableTags)
                chip.IsSelected = false;

            var target = AvailableTags.FirstOrDefault(c => c.Tag.Id == _preselectedTagId);
            if (target is null)
            {
                // Lifecycle race (#49): Shell's ApplyQueryAttributes can deliver
                // tagId before OnAppearing → SyncAsync populates AvailableTags.
                // Keep _preselectedTagId so SyncCoreAsync's post-population
                // re-call lands on the (now-present) chip. Cleared on success.
                return;
            }

            target.IsSelected = true;
            // Bypass setter to avoid premature ApplyFilter — caller runs it once
            if (_statusFilter != FilterStatus.All)
            {
                _statusFilter = FilterStatus.All;
                OnPropertyChanged(nameof(StatusFilter));
                OnPropertyChanged(nameof(IsActiveSelected));
                OnPropertyChanged(nameof(IsAnsweredSelected));
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(IsOverdueSelected));
            }
            _accessibilityService.Announce($"Filtered by {target.Tag.Name}");

            _preselectedTagId = 0;
        }

        private PrayerRequestDetailViewModel BuildViewModel(Prayer p)
        {
            var vm = PrayerRowFactory(p);
            vm.CardTitle = _cardTitleLookup.TryGetValue(p.PrayerCardId, out var t) ? t : string.Empty;
            SubscribeToPropertyChanges(vm);
            return vm;
        }

        private async Task<Dictionary<int, HashSet<int>>> BuildRequestTagLookupAsync()
        {
            var lookup = new Dictionary<int, HashSet<int>>();
            var allRows = await PrayerCardTag.LoadAllAsync();
            foreach (var row in allRows.Where(r => r.PrayerRequestId > 0))
            {
                if (!lookup.ContainsKey(row.PrayerRequestId))
                    lookup[row.PrayerRequestId] = new HashSet<int>();
                lookup[row.PrayerRequestId].Add(row.PrayerTagId);
            }
            return lookup;
        }

        private void ApplyFilter()
        {
            IEnumerable<PrayerRequestDetailViewModel> result = AllPrayers;

            // Issue #255: drop effectively-protected prayers from the list while locked.
            // Resolves each prayer's owning card (+ that card's box, for box-cascaded
            // protection) via the lookups refreshed in SyncCoreAsync — no extra service
            // call per filter pass.
            if (!_confidentialAccessService.IsSessionUnlocked)
            {
                result = result.Where(p =>
                    !_cardLookup.TryGetValue(p.PrayerCardId, out var owningCard) ||
                    !ProtectionPolicy.IsAccessBlocked(owningCard, _boxes.FirstOrDefault(b => b.Id == owningCard.BoxId), isSessionUnlocked: false));
            }

            // 1. Status filter
            result = StatusFilter switch
            {
                FilterStatus.Active   => result.Where(p => !p.IsAnswered),
                FilterStatus.Answered => result.Where(p => p.IsAnswered),
                FilterStatus.Overdue  => result.Where(p => !p.IsAnswered && _overdueIds.Contains(p.Id)),
                _                     => result
            };

            // 2. Tag chip filter
            var selectedTagIds = AvailableTags
                .Where(c => c.IsSelected)
                .Select(c => c.Tag.Id)
                .ToHashSet();

            if (selectedTagIds.Count > 0)
            {
                result = result.Where(p =>
                    _requestTagIds.TryGetValue(p.Id, out var tagIds) &&
                    selectedTagIds.Overlaps(tagIds));
            }

            // 3. Text search (title + card name)
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var q = _searchText.Trim();
                result = result.Where(p =>
                    (p.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.CardTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            // 4. Sort
            var sorted = result
                .OrderBy(p => p.CardTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FilteredPrayers = new ObservableCollection<PrayerRequestDetailViewModel>(sorted);

            // FilteredPrayers was just rebuilt — re-evaluate whether a Prayer Time
            // session can launch (false when 0 active prayers are in view).
            ((IRelayCommand)StartPrayerTimeCommand).NotifyCanExecuteChanged();

            if (!_suppressAnnounce)
            {
                _accessibilityService.NotifyLayoutChanged();
                AnnounceFilterCountDebounced(FilteredPrayers.Count);
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
                    _accessibilityService.Announce($"Showing {count} prayers");
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        private async Task AddNewPrayerAsync(string? prayerIdString)
        {
            try
            {
                var p = await Prayer.LoadAsync(int.Parse(prayerIdString ?? "0"));
                if (p is null) return;
                var vm = BuildViewModel(p);
                AllPrayers.Add(vm); // CollectionChanged → ApplyFilter
            }
            catch (Exception e)
            {
                await _navigationService.DisplayAlertAsync("Error", $"Failed to add new prayer: {e.Message}", "OK");
            }
        }

        private async Task NewPrayerAsync()
        {
            await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?new=true");
        }

        private async Task StartPrayerTimeAsync()
        {
            // Snapshot the active subset of the current filtered view. Answered prayers
            // can't be prayed through, so they're dropped even when visible (StatusFilter
            // = Answered/All). The ID set travels via the selection service (not the URL)
            // to mirror #34's "Choose cards" path and keep the Shell nav URL small.
            var activeIds = FilteredPrayers.Where(p => !p.IsAnswered).Select(p => p.Id).ToList();
            if (activeIds.Count == 0) return; // canExecute guards this, but stay defensive

            _selectionService.Set(activeIds);
            await _navigationService.GoToAsync($"{Routes.PrayerTimePage}?scope={Routes.ScopeSelection}");
        }

        private void SubscribeToPropertyChanges(PrayerRequestDetailViewModel prayer)
        {
            void Handler(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(PrayerRequestDetailViewModel.Title)
                                   or nameof(PrayerRequestDetailViewModel.IsAnswered))
                    ApplyFilter();
            }

            prayer.PropertyChanged += Handler;
            _prayerHandlers[prayer] = Handler;
        }

        private void UnsubscribeFromPropertyChanges(PrayerRequestDetailViewModel prayer)
        {
            if (_prayerHandlers.Remove(prayer, out var handler))
                prayer.PropertyChanged -= handler;
        }

        #endregion
    }
}
