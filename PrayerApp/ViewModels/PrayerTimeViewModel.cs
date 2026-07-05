using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using System.Windows.Input;

namespace PrayerApp.ViewModels;

public sealed record PrayerTimeEntry(int PrayerId, string CardTitle, string PrayerTitle, string Details)
{
    public bool IsSentinel => PrayerId == -1;
}

public class PrayerTimeViewModel : ObservableObject, IQueryAttributable
{
    private readonly IPrayerService _prayerService;
    private readonly ICardService _cardService;
    private readonly ITagService _tagService;
    private readonly IPrayerInteractionService _interactionService;
    private readonly INavigationService _navigationService;
    private readonly IAccessibilityService _accessibilityService;
    private readonly INotificationService _notificationService;
    private readonly ISettings _settings;
    private readonly IPrayerSelectionService _selectionService;
    private readonly IDispatcher _dispatcher;
    private readonly IBoxService _boxService;
    private readonly IConfidentialAccessService _confidentialAccessService;
    private CancellationTokenSource _loadCts = new();
    private int? _recentlyNotifiedTagId;

    // Auto-mode
    private static readonly int[] _intervalOptions = { 30, 60, 120 };
    private IDispatcherTimer? _autoTimer;

    private int _selectedIntervalSeconds;
    public int SelectedIntervalSeconds
    {
        get => _selectedIntervalSeconds;
        private set
        {
            if (SetProperty(ref _selectedIntervalSeconds, value))
            {
                _settings.AutoModeIntervalSeconds = value;
                OnPropertyChanged(nameof(CountdownDisplay));
            }
        }
    }

    private static readonly PrayerTimeEntry _completionSentinel = new(-1, "", "", "");

    private IReadOnlyList<PrayerTimeEntry> _entries = Array.Empty<PrayerTimeEntry>();
    public IReadOnlyList<PrayerTimeEntry> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    private int _currentIndex;
    /// <summary>
    /// Two-way bound to CarouselView.Position. The setter is public so the
    /// CarouselView can update it on swipe. When the value changes forward,
    /// interaction logging fires for the prayer that was just left.
    /// </summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            var previous = _currentIndex;
            if (SetProperty(ref _currentIndex, value))
            {
                OnPropertyChanged(nameof(CurrentEntry));
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(HasPrevious));
                OnPropertyChanged(nameof(HasNext));

                // Announce current prayer for screen readers
                if (!string.IsNullOrEmpty(ProgressDisplay) && CurrentEntry is { } entry && !entry.IsSentinel)
                    _accessibilityService.Announce($"Prayer {ProgressDisplay}: {entry.PrayerTitle}");

                // Log interaction for the prayer we just swiped away from (forward only)
                if (value > previous && previous < Entries.Count)
                    LogInteractionForIndex(previous).SafeFireAndForget();

                // Swiped to the sentinel (completion) position
                if (value > previous && Entries.Count > 0 && Entries[value].IsSentinel)
                {
                    HasCompleted = true;
                    StopAutoMode();
                }
            }
        }
    }

    /// <summary>Real prayer count, excluding the completion sentinel.</summary>
    private int RealEntryCount => Entries.Count > 0 && Entries[^1].IsSentinel ? Entries.Count - 1 : Entries.Count;

    public PrayerTimeEntry? CurrentEntry => Entries.Count > 0 ? Entries[CurrentIndex] : null;
    public string ProgressDisplay
    {
        get
        {
            var count = RealEntryCount;
            return count > 0 ? $"{Math.Min(CurrentIndex + 1, count)} of {count}" : string.Empty;
        }
    }
    public bool HasPrevious => CurrentIndex > 0;
    public bool HasNext => CurrentIndex < Entries.Count - 1;

    private bool _hasCompleted;
    public bool HasCompleted
    {
        get => _hasCompleted;
        private set => SetProperty(ref _hasCompleted, value);
    }

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                _accessibilityService.Announce(value ? "Loading" : "Content loaded");
        }
    }

    private bool _isAutoMode;
    public bool IsAutoMode
    {
        get => _isAutoMode;
        private set
        {
            if (SetProperty(ref _isAutoMode, value))
            {
                OnPropertyChanged(nameof(AutoModeButtonText));
                OnPropertyChanged(nameof(CountdownDisplay));
                if (!value) IsPaused = false;
            }
        }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(PauseButtonDescription));
            }
        }
    }

    public string PauseButtonText => IsPaused ? "▶" : "⏸";
    public string PauseButtonDescription => IsPaused ? "Resume auto-advance" : "Pause auto-advance";

    private int _countdownSeconds;
    public int CountdownSeconds
    {
        get => _countdownSeconds;
        private set
        {
            if (SetProperty(ref _countdownSeconds, value))
                OnPropertyChanged(nameof(CountdownDisplay));
        }
    }

    /// <summary>Label text for the Auto toggle button.</summary>
    public string AutoModeButtonText => IsAutoMode ? "⏸ Auto" : "Auto ▷";

    /// <summary>
    /// Shows the live countdown while auto-mode is running; otherwise shows the
    /// configured interval so the user knows what they'll get when they start auto-mode.
    /// Tapping this label cycles the interval (30s → 1m → 2m → 30s).
    /// </summary>
    public string CountdownDisplay => IsAutoMode ? $"{CountdownSeconds}s" : IntervalLabel(SelectedIntervalSeconds);

    private static string IntervalLabel(int seconds) => seconds switch
    {
        60 => "1m",
        120 => "2m",
        _ => $"{seconds}s",
    };

    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand EndSessionCommand { get; }
    public ICommand MarkAnsweredCommand { get; }
    public ICommand ToggleAutoModeCommand { get; }
    public ICommand CycleIntervalCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public PrayerTimeViewModel(IPrayerService prayerService, ICardService cardService,
        ITagService tagService, IPrayerInteractionService interactionService,
        INavigationService navigationService, IAccessibilityService accessibilityService,
        INotificationService notificationService, ISettings settings,
        IPrayerSelectionService selectionService, IDispatcher dispatcher,
        IBoxService boxService, IConfidentialAccessService confidentialAccessService)
    {
        _prayerService = prayerService;
        _cardService = cardService;
        _tagService = tagService;
        _interactionService = interactionService;
        _navigationService = navigationService;
        _accessibilityService = accessibilityService;
        _notificationService = notificationService;
        _settings = settings;
        _selectionService = selectionService;
        _dispatcher = dispatcher;
        _boxService = boxService;
        _confidentialAccessService = confidentialAccessService;

        _selectedIntervalSeconds = _settings.AutoModeIntervalSeconds;

        NextCommand = new AsyncRelayCommand(NextAsync);
        PreviousCommand = new RelayCommand(Previous);
        EndSessionCommand = new AsyncRelayCommand(EndSessionAsync);
        MarkAnsweredCommand = new AsyncRelayCommand(MarkAnsweredAsync);
        ToggleAutoModeCommand = new RelayCommand(ToggleAutoMode);
        CycleIntervalCommand = new RelayCommand(CycleInterval);
        TogglePauseCommand = new RelayCommand(TogglePause);
    }

    public PrayerTimeViewModel() : this(
        IPlatformApplication.Current!.Services.GetRequiredService<IPrayerService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<ICardService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<ITagService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IPrayerInteractionService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<INotificationService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<ISettings>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IPrayerSelectionService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IDispatcher>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IBoxService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IConfidentialAccessService>())
    { }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var scope = query.TryGetValue("scope", out var s) ? s?.ToString() : Routes.ScopeAll;

        if (scope == Routes.ScopeSelection)
        {
            // The ID set travels via IPrayerSelectionService (not the URL) to avoid
            // bloating the Shell nav URL for large selections. Consume() reads and
            // clears so a stale set is never reused on a later navigation.
            var selectionIds = _selectionService.Consume();
            LoadEntriesAsync(tagIds: null, boxId: null, selectionIds: selectionIds).SafeFireAndForget();
            return;
        }

        if (scope == Routes.ScopeBox && query.TryGetValue("boxId", out var bObj)
            && bObj is string bStr && int.TryParse(bStr, out var boxId))
        {
            LoadEntriesAsync(tagIds: null, boxId: boxId).SafeFireAndForget();
            return;
        }

        var tagIdsStr = query.TryGetValue("tagIds", out var t) ? t?.ToString() : null;

        IEnumerable<int> tagIds = tagIdsStr is not null
            ? tagIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => int.TryParse(x, out var id) ? id : -1)
                       .Where(id => id > 0)
            : Enumerable.Empty<int>();

        LoadEntriesAsync(scope == Routes.ScopeTags ? tagIds : null, boxId: null).SafeFireAndForget();
    }

    private async Task LoadEntriesAsync(IEnumerable<int>? tagIds, int? boxId = null,
        IReadOnlyList<int>? selectionIds = null)
    {
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        HasCompleted = false;
        try
        {
            // Cache "Recently Notified" tag ID for cleanup after prayers are prayed
            var systemTag = await _tagService.GetSystemTagAsync(TagService.RecentlyNotifiedTagName);
            _recentlyNotifiedTagId = systemTag?.Id;

            var allActive = await _prayerService.GetAllActivePrayersAsync();
            token.ThrowIfCancellationRequested();

            var cardsTask = _cardService.GetCardsAsync();
            var boxesTask = _boxService.GetBoxesAsync();
            await Task.WhenAll(cardsTask, boxesTask);
            var cards = cardsTask.Result;
            var boxes = boxesTask.Result;
            token.ThrowIfCancellationRequested();

            IEnumerable<Prayer> filtered;
            if (selectionIds is not null)
            {
                // scope=selection: intersect with the explicit ID set handed over by
                // the "Choose cards" modal or the per-card "Pray" chip. Prayers
                // answered mid-session naturally drop out (they leave allActive),
                // matching the other scopes. Archived cards were already excluded at
                // selection time, so no extra archived filter is needed here.
                var idSet = selectionIds.ToHashSet();
                filtered = allActive.Where(p => idSet.Contains(p.Id));
            }
            else if (tagIds is not null)
            {
                var requestIdSet = (await _tagService.GetRequestIdsByTagIdsAsync(tagIds)).ToHashSet();
                token.ThrowIfCancellationRequested();
                filtered = allActive.Where(p => requestIdSet.Contains(p.Id));
            }
            else if (boxId is not null)
            {
                var cardIdsInBox = cards.Where(c => c.BoxId == boxId.Value).Select(c => c.Id).ToHashSet();
                filtered = allActive.Where(p => cardIdsInBox.Contains(p.PrayerCardId));
            }
            else
            {
                // scope=all: exclude only prayers whose card is in the Archived box.
                // (Card-less/orphan prayers are preserved — the cardLookup "Unknown" fallback handles them.)
                var archivedCardIds = cards
                    .Where(c => c.BoxId == _settings.ArchivedFolderId)
                    .Select(c => c.Id)
                    .ToHashSet();
                filtered = allActive.Where(p => !archivedCardIds.Contains(p.PrayerCardId));
            }

            var cardLookup = cards.ToDictionary(c => c.Id, c => c.Title);

            // Issue #255: Prayer Time is an exposed landscape view, so it re-authenticates
            // UNCONDITIONALLY whenever the session would include any effectively-protected
            // prayer — stricter than the search/share gates, which skip the prompt once
            // IsSessionUnlocked is already true. Resolve each prayer's owning card (+ that
            // card's box, for box-cascaded protection) via one dictionary built from the
            // already-loaded cards/boxes, then batch-check before building entries. On
            // deny, protected prayers are dropped and the session proceeds with the rest.
            var cardById = cards.ToDictionary(c => c.Id);
            var boxById = boxes.ToDictionary(b => b.Id);
            filtered = filtered.ToList();
            var hasProtected = filtered.Any(p =>
                cardById.TryGetValue(p.PrayerCardId, out var owningCard) &&
                ProtectionPolicy.IsAccessBlocked(owningCard, boxById.GetValueOrDefault(owningCard.BoxId), isSessionUnlocked: false));

            if (hasProtected)
            {
                var authorized = await _confidentialAccessService.AuthenticateAsync("Start Prayer Time with protected prayers");
                token.ThrowIfCancellationRequested();
                if (!authorized)
                {
                    filtered = filtered.Where(p =>
                        !(cardById.TryGetValue(p.PrayerCardId, out var owningCard) &&
                          ProtectionPolicy.IsAccessBlocked(owningCard, boxById.GetValueOrDefault(owningCard.BoxId), isSessionUnlocked: false)));
                }
            }

            var entries = filtered
                .Select(p => new PrayerTimeEntry(
                    PrayerId: p.Id,
                    CardTitle: cardLookup.TryGetValue(p.PrayerCardId, out var cardTitle) ? cardTitle : "Unknown",
                    PrayerTitle: p.Title,
                    Details: p.Details ?? string.Empty))
                .OrderBy(e => e.CardTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.PrayerTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool anyPrayers = entries.Count > 0;
            if (anyPrayers)
                entries.Add(_completionSentinel);

            Entries = entries.AsReadOnly();
            // SetProperty(ref _currentIndex, 0) is a no-op when _currentIndex is already 0
            // (int default value). Bypass it and fire dependent notifications manually so
            // the XAML bindings on CurrentEntry update correctly after the first load.
            _currentIndex = 0;
            OnPropertyChanged(nameof(CurrentEntry));
            OnPropertyChanged(nameof(ProgressDisplay));
            OnPropertyChanged(nameof(HasPrevious));
            OnPropertyChanged(nameof(HasNext));
            HasCompleted = !anyPrayers;
        }
        catch (OperationCanceledException)
        {
            // Load was superseded by a newer call — ignore
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load prayer time entries: {ex.Message}");
            await _navigationService.DisplayAlertAsync("Error", "Unable to load prayers for this session.", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NextAsync()
    {
        if (HasCompleted) return;

        if (HasNext)
        {
            // CurrentIndex setter handles interaction logging for the leaving prayer
            CurrentIndex++;
            // Reset countdown so user gets full interval on the new card
            if (IsAutoMode)
                CountdownSeconds = SelectedIntervalSeconds;
        }
        else
        {
            // Log the final prayer before completing
            await LogInteractionForIndex(CurrentIndex);
            HasCompleted = true;
            StopAutoMode();
            _accessibilityService.Announce("Prayer session complete");
        }
    }

    private async Task LogInteractionForIndex(int index)
    {
        if (index < 0 || index >= Entries.Count) return;
        var entry = Entries[index];
        if (entry.IsSentinel) return; // sentinel — nothing to log
        try
        {
            await _interactionService.LogInteractionAsync(entry.PrayerId);
            if (_recentlyNotifiedTagId.HasValue)
                await _tagService.RemoveTagFromRequestAsync(entry.PrayerId, _recentlyNotifiedTagId.Value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to log interaction: {ex.Message}");
        }
    }

    private void Previous()
    {
        if (HasPrevious)
            CurrentIndex--;
    }

    private async Task MarkAnsweredAsync()
    {
        if (HasCompleted || CurrentIndex < 0 || CurrentIndex >= Entries.Count) return;
        var entry = Entries[CurrentIndex];
        if (entry.IsSentinel) return;

        try
        {
            var prayer = await Prayer.LoadAsync(entry.PrayerId);
            if (prayer is null) return;

            prayer.IsAnswered = true;
            prayer.AnsweredAt = DateTime.Now;
            await _prayerService.SavePrayerAsync(prayer);
            await _notificationService.CancelAsync(prayer.Id, prayer.PrayerFrequency);

            _accessibilityService.Announce($"{entry.PrayerTitle} marked as answered");
            await NextAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MarkAnswered failed: {ex.Message}");
        }
    }

    private async Task EndSessionAsync()
    {
        StopAutoMode();
        await _navigationService.GoToAsync("..");
    }

    // ── Auto-mode ────────────────────────────────────────────────────────────

    private void ToggleAutoMode()
    {
        if (HasCompleted) return;

        if (IsAutoMode)
            StopAutoMode();
        else
            StartAutoMode();
    }

    private void CycleInterval()
    {
        var idx = Array.IndexOf(_intervalOptions, SelectedIntervalSeconds);
        SelectedIntervalSeconds = _intervalOptions[(idx + 1) % _intervalOptions.Length];
        // If auto-mode is running, reset the countdown to the new interval immediately
        if (IsAutoMode)
            CountdownSeconds = SelectedIntervalSeconds;
        _accessibilityService.Announce($"Interval set to {IntervalLabel(SelectedIntervalSeconds)}");
    }

    private void StartAutoMode()
    {
        IsAutoMode = true;
        IsPaused = false;
        CountdownSeconds = SelectedIntervalSeconds;

        _autoTimer = _dispatcher.CreateTimer();
        _autoTimer.Interval = TimeSpan.FromSeconds(1);
        _autoTimer.Tick += OnAutoTimerTick;
        _autoTimer.Start();
        _accessibilityService.Announce("Auto-advance started");
    }

    /// <summary>
    /// Permanently stops auto-mode and disposes the timer.
    /// Called when the session ends, the user taps Stop, or all cards have been prayed.
    /// </summary>
    public void StopAutoMode()
    {
        if (_autoTimer is not null)
        {
            _autoTimer.Stop();
            _autoTimer.Tick -= OnAutoTimerTick;
            _autoTimer = null;
        }
        IsAutoMode = false;
        CountdownSeconds = 0;
    }

    private void TogglePause()
    {
        if (!IsAutoMode) return;
        if (IsPaused)
        {
            IsPaused = false;
            _autoTimer?.Start();
            _accessibilityService.Announce("Resumed");
        }
        else
        {
            IsPaused = true;
            _autoTimer?.Stop();
            _accessibilityService.Announce("Paused");
        }
    }

    /// <summary>
    /// Pauses the countdown without disabling auto-mode.
    /// Called when the app goes to background.
    /// </summary>
    public void PauseAutoMode()
    {
        IsPaused = true;
        _autoTimer?.Stop();
    }

    /// <summary>
    /// Resumes the countdown if auto-mode is still active.
    /// Called when the app returns to foreground.
    /// </summary>
    public void ResumeAutoMode()
    {
        if (IsAutoMode && IsPaused)
        {
            IsPaused = false;
            _autoTimer?.Start();
        }
    }

    private void OnAutoTimerTick(object? sender, EventArgs e)
    {
        CountdownSeconds--;
        if (CountdownSeconds <= 0)
            NextAsync().SafeFireAndForget();
    }
}
