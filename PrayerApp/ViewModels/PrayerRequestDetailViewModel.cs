using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PrayerApp.ViewModels
{
    public class PrayerRequestDetailViewModel : ObservableObject, IQueryAttributable, IEditGuard
    {
        private Prayer _prayer;
        private readonly IPrayerService _prayerService;
        private readonly ITagService _tagService;
        private readonly ICardService _cardService;
        private readonly IOnboardingService _onboardingService;
        private readonly INotificationService _notificationService;
        private readonly INavigationService _navigationService;
        private readonly IAccessibilityService _accessibilityService;
        private readonly ISettings _settings;
        private readonly IBoxService _boxService;
        private readonly IConfidentialAccessService _confidentialAccessService;
        private List<PrayerTag> _allTags = new();
        private int _prayedCount;
        private string _prayedSummary = string.Empty;
        // Issue #255: the owning card's box, resolved once per LoadCardsAsync (mirrors
        // PrayerCardViewModel._box) so CanShare can evaluate GetEffectiveProtectionMode
        // without a service call per property read.
        private CardBox? _ownerBox;

        /// <summary>
        /// True while SaveAsync or SaveAndNewAsync is in flight. Drives the page-level
        /// ActivityIndicator and gates SaveCommand/SaveAndNewCommand canExecute so a
        /// double-tap can't duplicate the row.
        /// </summary>
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    (SaveCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
                    (SaveAndNewCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        public ICommand SaveCommand { get; private set; }
        public ICommand SaveAndNewCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand SelectPrayerCommand { get; private set; }
        public ICommand EditPrayerCommand { get; private set; }
        public ICommand MarkAnsweredCommand { get; private set; }
        public ICommand ShareCommand { get; private set; }
        public ICommand OpenTagPickerCommand { get; private set; }

        public ObservableCollection<TagChipViewModel> SelectedTags { get; } = new();

        public bool HasTags => SelectedTags.Count > 0;

        private string _savedQueryKey = Routes.QueryKeys.Saved;
        private string _deletedQueryKey = Routes.QueryKeys.Deleted;

        // Dirty-tracking originals (set after load/save)
        private string _originalTitle = string.Empty;
        private string? _originalDetails;
        private int _originalPrayerCardId;
        private bool _originalCanNotify;
        private PrayerFrequency _originalFrequency;
        private bool _originalIsAnswered;
        private DateTime? _originalAnsweredAt;
        private int _originalNotifyHour;
        private int _originalNotifyMinute;
        private int _originalDayOfWeek;
        private int _originalDayOfMonth;

        public bool ReturnToCards { get; set; }
        public bool IsNew => _prayer.Id == 0;
        public bool ShowSaveAndNew => IsNew && ReturnToCards;


        /// <summary>
        /// Lifecycle-gated focus signal raised after Save &amp; Add Another resets the form.
        /// PrayerDetailPage observes via PropertyChanged and focuses the title entry on
        /// the dispatcher channel — direct C# event handlers raced platform layout state.
        /// </summary>
        private bool _pendingFocusTitle;
        public bool PendingFocusTitle
        {
            get => _pendingFocusTitle;
            private set => SetProperty(ref _pendingFocusTitle, value);
        }

        /// <summary>View clears the signal after focusing — prevents re-fire on next OnAppearing.</summary>
        public void ConsumePendingFocusTitle() => PendingFocusTitle = false;

        private bool _isReadOnly;
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (_isReadOnly != value)
                {
                    _isReadOnly = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEditable));
                }
            }
        }

        public bool IsEditable => !IsReadOnly;

        public bool IsNotAnswered => !IsAnswered;

        public bool IsDirty =>
            Title != _originalTitle ||
            Details != _originalDetails ||
            PrayerCardId != _originalPrayerCardId ||
            CanNotify != _originalCanNotify ||
            PrayerFrequency != _originalFrequency ||
            IsAnswered != _originalIsAnswered ||
            AnsweredAt != _originalAnsweredAt ||
            _prayer.NotifyHour != _originalNotifyHour ||
            _prayer.NotifyMinute != _originalNotifyMinute ||
            _prayer.NotifyDayOfWeek != _originalDayOfWeek ||
            _prayer.NotifyDayOfMonth != _originalDayOfMonth;

        public async Task<bool> CanLeaveAsync()
        {
            if (!IsDirty) return true;
            return await _navigationService.DisplayConfirmAsync(
                "Unsaved Changes", "Discard changes?", "Discard", "Cancel");
        }

        public string Identifier => _prayer.Id.ToString();

        public int Id
        {
            get => _prayer.Id;
            set
            {
                if (_prayer.Id != value)
                {
                    _prayer.Id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Title
        {
            get => _prayer.Title;
            set
            {
                if (_prayer.Title != value)
                {
                    _prayer.Title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AccessibleSummary));
                }
            }
        }

        public string? Details
        {
            get => _prayer.Details;
            set
            {
                if (_prayer.Details != value)
                {
                    _prayer.Details = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PrayerCardId
        {
            get => _prayer.PrayerCardId;
            set
            {
                if (_prayer.PrayerCardId != value)
                {
                    _prayer.PrayerCardId = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanNotify
        {
            get => _prayer.CanNotify;
            set
            {
                if (_prayer.CanNotify != value)
                {
                    _prayer.CanNotify = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowNotifyTime));
                    OnPropertyChanged(nameof(ShowDayOfWeek));
                    OnPropertyChanged(nameof(ShowDayOfMonth));
                    // Request OS permission immediately when user enables notifications here
                    // (e.g. during tutorial) rather than waiting for Settings page visit.
                    if (value && _settings.AllowNotifications)
                        _notificationService.RequestPermissionAsync().SafeFireAndForget();
                }
            }
        }

        public PrayerFrequency PrayerFrequency
        {
            get => _prayer.PrayerFrequency;
            set
            {
                if (_prayer.PrayerFrequency != value)
                {
                    _prayer.PrayerFrequency = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PrayerFrequencyDisplay));
                    OnPropertyChanged(nameof(ShowDayOfWeek));
                    OnPropertyChanged(nameof(ShowDayOfMonth));

                    // Materialize defaults so the displayed Picker value matches
                    // what actually gets saved (avoids -1 sentinel persisting).
                    if (value == PrayerFrequency.Weekly && _prayer.NotifyDayOfWeek < 0)
                        SelectedDayOfWeek = DaysOfWeek[(int)DateTime.Now.DayOfWeek];
                    if (value == PrayerFrequency.Monthly && _prayer.NotifyDayOfMonth <= 0)
                        SelectedDayOfMonth = DateTime.Now.Day;
                }
            }
        }

        public string PrayerFrequencyDisplay
        {
            get
            {
                var time = new TimeSpan(_prayer.NotifyHour, _prayer.NotifyMinute, 0);
                var timeStr = DateTime.Today.Add(time).ToString("h:mm tt");
                return PrayerFrequency switch
                {
                    PrayerFrequency.Daily => $"Daily at {timeStr}",
                    PrayerFrequency.Weekly when _prayer.NotifyDayOfWeek >= 0 =>
                        $"Weekly on {DaysOfWeek[_prayer.NotifyDayOfWeek]} at {timeStr}",
                    PrayerFrequency.Weekly => $"Weekly at {timeStr}",
                    PrayerFrequency.Monthly when _prayer.NotifyDayOfMonth > 0 =>
                        $"Monthly on the {OrdinalSuffix(_prayer.NotifyDayOfMonth)} at {timeStr}",
                    PrayerFrequency.Monthly => $"Monthly at {timeStr}",
                    PrayerFrequency.Yearly => $"Yearly at {timeStr}",
                    PrayerFrequency.OneTime => $"Once at {timeStr}",
                    _ => PrayerFrequency.ToString()
                };
            }
        }

        public TimeSpan NotifyTime
        {
            get => new TimeSpan(_prayer.NotifyHour, _prayer.NotifyMinute, 0);
            set
            {
                _prayer.NotifyHour = value.Hours;
                _prayer.NotifyMinute = value.Minutes;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrayerFrequencyDisplay));
            }
        }

        public bool ShowNotifyTime => CanNotify;
        public bool ShowDayOfWeek => CanNotify && PrayerFrequency == PrayerFrequency.Weekly;
        public bool ShowDayOfMonth => CanNotify && PrayerFrequency == PrayerFrequency.Monthly;

        public List<string> DaysOfWeek { get; } =
            ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

        public string? SelectedDayOfWeek
        {
            get => _prayer.NotifyDayOfWeek >= 0 ? DaysOfWeek[_prayer.NotifyDayOfWeek] : null;
            set
            {
                _prayer.NotifyDayOfWeek = value != null ? DaysOfWeek.IndexOf(value) : -1;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrayerFrequencyDisplay));
            }
        }

        public IReadOnlyList<int> DaysOfMonth { get; } = Enumerable.Range(1, 31).ToList();

        public int SelectedDayOfMonth
        {
            get => _prayer.NotifyDayOfMonth > 0 ? _prayer.NotifyDayOfMonth : DateTime.Now.Day;
            set
            {
                _prayer.NotifyDayOfMonth = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrayerFrequencyDisplay));
            }
        }

        private static string OrdinalSuffix(int day) => day switch
        {
            1 or 21 or 31 => $"{day}st",
            2 or 22 => $"{day}nd",
            3 or 23 => $"{day}rd",
            _ => $"{day}th"
        };

        public bool IsAnswered
        {
            get => _prayer.IsAnswered;
            set
            {
                if (_prayer.IsAnswered != value)
                {
                    _prayer.IsAnswered = value;
                    // Date-only so the mark-answered default, the CaptureOriginals baseline,
                    // and the DatePicker's date-only writeback all agree. A time-bearing
                    // DateTime.Now default would spuriously dirty the form when the picker
                    // round-trips it to midnight (issue #108 review). PrayerTimeViewModel keeps
                    // its own DateTime.Now convention; normalize only at this editable surface.
                    _prayer.AnsweredAt = value ? DateTime.Today : null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AnsweredAt));
                    OnPropertyChanged(nameof(AnsweredAtDisplay));
                    OnPropertyChanged(nameof(IsNotAnswered));
                    OnPropertyChanged(nameof(AccessibleSummary));
                }
            }
        }

        public DateTime? AnsweredAt
        {
            // Date-only at BOTH boundaries. The getter normalizes the read so the
            // CaptureOriginals baseline can never carry a stray time, even when the stored
            // value came from a time-bearing writer (PrayerTimeViewModel writes DateTime.Now,
            // and legacy rows answered before this fix). The setter coerces the write so saves
            // persist date-only too. Without the getter normalization, a Prayer-Time-answered
            // row's 14:32 baseline != the picker's midnight writeback → spurious dirty (issue #108 review).
            get => _prayer.AnsweredAt?.Date;
            set
            {
                var normalized = value?.Date;
                if (_prayer.AnsweredAt != normalized)
                {
                    _prayer.AnsweredAt = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AnsweredAtDisplay));
                    OnPropertyChanged(nameof(AccessibleSummary));
                }
            }
        }

        /// <summary>Today, exposed for the answered-date picker's <c>MaximumDate</c>
        /// (a prayer cannot be marked answered in the future).</summary>
        public DateTime Today => DateTime.Today;

        /// <summary>Date-only projection of <see cref="CreatedAt"/> for the answered-date
        /// picker's <c>MinimumDate</c>. <c>CreatedAt</c> carries a time component, so binding
        /// the raw value would coerce a date-only "today" answer UP to the creation timestamp
        /// on the creation day — re-injecting time-of-day (issue #108 review).</summary>
        public DateTime CreatedAtDate => _prayer.CreatedAt.Date;

        public string AnsweredAtDisplay =>
            IsAnswered && AnsweredAt.HasValue
                ? $"✓ {AnsweredAt.Value:MMM d}"
                : string.Empty;

        /// <summary>
        /// Absolute "started" caption shown beneath the title on the prayer-request detail view
        /// (issue #107), e.g. "Started Mar 3, 2026". Reuses the <c>MMM d, yyyy</c> format already
        /// used by <see cref="PrayedSummary"/>. <see cref="CreatedAt"/> is a populated DateTime, so
        /// this is always non-empty. The detail view's Label announces it verbatim, so it is not
        /// folded into <see cref="AccessibleSummary"/>.
        /// </summary>
        public string CreatedAtDisplay => $"Started {CreatedAt:MMM d, yyyy}";

        /// <summary>
        /// Composed accessible label for screen readers. VoiceOver reads this as a single
        /// announcement for the prayer row: "Card Name, Prayer Title, Answered Mar 15".
        /// </summary>
        public string AccessibleSummary
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrEmpty(CardTitle)) parts.Add(CardTitle);
                parts.Add(Title);
                if (IsAnswered && !string.IsNullOrEmpty(AnsweredAtDisplay))
                    parts.Add(AnsweredAtDisplay);
                return string.Join(", ", parts);
            }
        }

        public IReadOnlyList<PrayerFrequency> FrequencyOptions { get; } =
            new ReadOnlyCollection<PrayerFrequency>(Enum.GetValues<PrayerFrequency>().ToList());

        private string _cardTitle = string.Empty;
        public string CardTitle
        {
            get => _cardTitle;
            set
            {
                if (SetProperty(ref _cardTitle, value))
                    OnPropertyChanged(nameof(AccessibleSummary));
            }
        }

        public ObservableCollection<PrayerCard> AvailableCards { get; } = new();

        private PrayerCard? _selectedCard;
        public PrayerCard? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (SetProperty(ref _selectedCard, value) && value is not null)
                {
                    PrayerCardId = value.Id;
                    CardTitle = value.Title ?? string.Empty;
                    RaiseCanShareChanged();
                }
            }
        }

        /// <summary>
        /// Re-raises CanShare and notifies ShareCommand's canExecute. Called whenever the
        /// owning card, its box, or the confidential-access lock state changes (issue #255).
        /// </summary>
        private void RaiseCanShareChanged()
        {
            OnPropertyChanged(nameof(CanShare));
            (ShareCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }

        public bool IsImported => _prayer.IsImported;
        public DateTime CreatedAt => _prayer.CreatedAt;
        public DateTime UpdatedAt => _prayer.UpdatedAt;

        /// <summary>
        /// Gates the Share action (issue #255): disabled (not hidden — per
        /// .project/design-system.md "Required states") when the owning card is
        /// effectively protected and the confidential-access session is locked. True
        /// when the owning card hasn't loaded yet (e.g. a brand-new unsaved prayer has
        /// no card resolved) — mirrors the pre-#255 always-enabled behavior for that case.
        /// </summary>
        public bool CanShare => _selectedCard is null
            || !ProtectionPolicy.IsAccessBlocked(_selectedCard, _ownerBox, _confidentialAccessService.IsSessionUnlocked);

        /// <summary>
        /// Shows how many times this prayer has been prayed and when it was created.
        /// Hidden when the count is zero (see <see cref="HasBeenPrayedFor"/>); visible at 1+.
        /// Backed by a field and set via SetProperty so compiled bindings always pick
        /// up the update regardless of which thread LoadPrayerAsync completes on.
        /// </summary>
        public string PrayedSummary
        {
            get => _prayedSummary;
            private set => SetProperty(ref _prayedSummary, value);
        }

        /// <summary>
        /// Gates the <see cref="PrayedSummary"/> label's visibility: false at zero count
        /// (hides the "Prayed for 0 times" line), true at 1+. Kept in sync with
        /// <c>_prayedCount</c> via <see cref="RecomputePrayedSummary"/>.
        /// </summary>
        public bool HasBeenPrayedFor => _prayedCount > 0;

        private void RecomputePrayedSummary()
        {
            PrayedSummary = $"Prayed for {_prayedCount} {(_prayedCount == 1 ? "time" : "times")} since {CreatedAt:MMM d, yyyy}";
            OnPropertyChanged(nameof(HasBeenPrayedFor));
        }

        public PrayerRequestDetailViewModel(IPrayerService prayerService, ITagService tagService,
            ICardService cardService, IOnboardingService onboardingService, INotificationService notificationService,
            INavigationService navigationService, IAccessibilityService accessibilityService, ISettings settings,
            IBoxService boxService, IConfidentialAccessService confidentialAccessService)
        {
            _prayer = new Prayer();
            _prayerService = prayerService;
            _tagService = tagService;
            _cardService = cardService;
            _onboardingService = onboardingService;
            _notificationService = notificationService;
            _navigationService = navigationService;
            _accessibilityService = accessibilityService;
            _settings = settings;
            _boxService = boxService;
            _confidentialAccessService = confidentialAccessService;
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
            SaveAndNewCommand = new AsyncRelayCommand(SaveAndNewAsync, () => !IsBusy);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync);
            SelectPrayerCommand = new AsyncRelayCommand(SelectPrayerAsync);
            EditPrayerCommand = new RelayCommand(EditPrayer);
            MarkAnsweredCommand = new AsyncRelayCommand(MarkAnsweredAsync);
            ShareCommand = new AsyncRelayCommand(ShareAsync, () => CanShare);
            OpenTagPickerCommand = new AsyncRelayCommand(OpenTagPickerAsync);

            SelectedTags.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTags));
        }

        public PrayerRequestDetailViewModel() : this(
            IPlatformApplication.Current!.Services.GetRequiredService<IPrayerService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ITagService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ICardService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IOnboardingService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<INotificationService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ISettings>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IBoxService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IConfidentialAccessService>())
        { }

        public PrayerRequestDetailViewModel(Prayer prayer) : this()
        {
            _prayer = prayer ?? new Prayer();
            IsReadOnly = false;
        }

        // Test-only DI ctor. The production (Prayer) ctor chains through
        // IPlatformApplication.Current, which unit tests can't stub; tests
        // inject this via PrayerCardViewModel.PrayerRowFactory. Internal so the
        // prod public surface keeps (Prayer) as the only call shape.
        internal PrayerRequestDetailViewModel(Prayer prayer,
            IPrayerService prayerService, ITagService tagService,
            ICardService cardService, IOnboardingService onboardingService,
            INotificationService notificationService,
            INavigationService navigationService, IAccessibilityService accessibilityService,
            ISettings settings, IBoxService boxService, IConfidentialAccessService confidentialAccessService)
            : this(prayerService, tagService, cardService, onboardingService,
                notificationService, navigationService, accessibilityService, settings,
                boxService, confidentialAccessService)
        {
            _prayer = prayer ?? new Prayer();
            IsReadOnly = false;
        }

        /// <summary>Core save logic shared by Save and Save &amp; Add Another.</summary>
        /// <returns>True if this was a new prayer (first save).</returns>
        private async Task<bool> CoreSaveAsync()
        {
            bool isNew = _prayer.Id == 0;
            await _prayerService.SavePrayerAsync(_prayer);
            CaptureOriginals();

            // New prayers: persist staged tags now that we have an ID.
            // Existing prayers: tags are persisted immediately by the tag picker modal.
            if (isNew)
            {
                foreach (var chip in SelectedTags)
                    await _tagService.AddTagToRequestAsync(_prayer.Id, chip.Id);
            }

            if (_prayer.CanNotify)
                await _notificationService.ScheduleAsync(_prayer);
            else
                await _notificationService.CancelAsync(_prayer.Id, _prayer.PrayerFrequency);

            if (isNew)
                _onboardingService.Advance();

            _accessibilityService.Announce("Prayer saved");
            return isNew;
        }

        private async Task SaveAsync()
        {
            if (IsBusy) return;
            try
            {
                IsBusy = true;
                var origCardId = _originalPrayerCardId; // capture before CoreSaveAsync resets originals
                bool isNew = await CoreSaveAsync();

                if (ReturnToCards)
                {
                    string route = isNew ? ".." : "../..";
                    bool cardChanged = !isNew && origCardId != 0 && origCardId != PrayerCardId;
                    var queryParts = $"{Routes.QueryKeys.PrayerSaved}={Identifier}&{Routes.QueryKeys.ParentCardId}={PrayerCardId}";
                    if (cardChanged)
                        queryParts += $"&{Routes.QueryKeys.OldCardId}={origCardId}";
                    await _navigationService.GoToAsync($"{route}?{queryParts}");
                }
                else
                {
                    await _navigationService.GoToAsync($"..?{_savedQueryKey}={Identifier}");
                }
            }
            finally { IsBusy = false; }
        }

        private async Task SaveAndNewAsync()
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(Title))
            {
                await _navigationService.DisplayAlertAsync("Required", "Please enter a prayer title.", "OK");
                return;
            }

            try
            {
                IsBusy = true;
                var savedTitle = Title;
                var cardId = PrayerCardId;
                await CoreSaveAsync();

                await CommunityToolkit.Maui.Alerts.Toast.Make($"Saved \"{savedTitle}\"").Show();
                ResetForNewPrayer(cardId);
            }
            finally { IsBusy = false; }
        }

        private Prayer CreateDefaultPrayer(int? cardId = null) => new()
        {
            PrayerCardId = cardId ?? 0,
            NotifyHour = _settings.DefaultNotifyHour,
            NotifyMinute = _settings.DefaultNotifyMinute
        };

        private void ResetForNewPrayer(int cardId)
        {
            _prayer = CreateDefaultPrayer(cardId);
            _prayedCount = 0;
            RecomputePrayedSummary();
            SelectedTags.Clear();
            RefreshProperties();
            CaptureOriginals();
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(ShowSaveAndNew));
            PendingFocusTitle = true;
        }

        private async Task DeleteAsync()
        {
            bool confirmed = await _navigationService.DisplayConfirmAsync(
                "Delete Prayer Request",
                $"Delete \"{Title}\"?",
                "Delete", "Cancel");

            if (!confirmed) return;

            await _prayerService.DeletePrayerAsync(_prayer);
            _accessibilityService.Announce("Prayer deleted");
            if (ReturnToCards)
            {
                await _navigationService.GoToAsync($"..?{Routes.QueryKeys.PrayerDeleted}={Identifier}&{Routes.QueryKeys.ParentCardId}={PrayerCardId}");
            }
            else
            {
                await _navigationService.GoToAsync($"..?{_deletedQueryKey}={Identifier}");
            }
        }

        private async Task SelectPrayerAsync()
        {
            if (ReturnToCards)
            {
                await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?load={Identifier}&viewOnly=true&returnToCards=true&parentCardId={PrayerCardId}");
            }
            else
            {
                await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?load={Identifier}&viewOnly=true");
            }
        }

        private void EditPrayer()
        {
            // Toggle the same page instance into edit mode instead of pushing a second
            // PrayerDetailPage. Keeps a single detail page on the Shell stack so Save's
            // "..?saved=X" and Delete's "..?deleted=X" pop cleanly to the parent list
            // (fixes BUG-69, where Shell couldn't disambiguate two same-route stack entries).
            IsReadOnly = false;
        }

        private async Task MarkAnsweredAsync()
        {
            IsAnswered = true;
            await _notificationService.CancelAsync(_prayer.Id, _prayer.PrayerFrequency);
            await _prayerService.SavePrayerAsync(_prayer);
            CaptureOriginals(); // Reset dirty state before navigation
            _accessibilityService.Announce("Prayer marked as answered");

            // Navigate back so parent page (list or card accordion) picks up the change
            if (ReturnToCards)
            {
                await _navigationService.GoToAsync($"..?{Routes.QueryKeys.PrayerSaved}={Identifier}&{Routes.QueryKeys.ParentCardId}={PrayerCardId}");
            }
            else
            {
                await _navigationService.GoToAsync($"..?{_savedQueryKey}={Identifier}");
            }
        }

        private async Task ShareAsync()
        {
            // Issue #255 defense in depth: CanShare already gates ShareCommand's
            // canExecute (disabling the button), but guard the action itself in case it's
            // ever invoked directly (e.g. ExecuteAsync bypassing canExecute).
            if (!CanShare) return;
            var deepLinkService = IPlatformApplication.Current!.Services.GetRequiredService<IDeepLinkService>();
            await deepLinkService.ShareRequestAsync(_prayer);
            _onboardingService.Advance();
        }

        private bool _queryApplied;

        void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (_queryApplied) return;
            _queryApplied = true;

            if (query.ContainsKey("newForCard"))
            {
                if (int.TryParse(query["newForCard"].ToString(), out int cardId))
                {
                    _prayer = CreateDefaultPrayer(cardId);
                    ReturnToCards = true;
                    _savedQueryKey = Routes.QueryKeys.PrayerSaved;
                    _deletedQueryKey = Routes.QueryKeys.PrayerDeleted;
                    IsReadOnly = false;
                    RefreshProperties();
                    InitNewPrayerAsync().SafeFireAndForget();
                }
            }
            else if (query.ContainsKey("new"))
            {
                // New prayer from the prayer list (no card pre-selected)
                _prayer = CreateDefaultPrayer();
                IsReadOnly = false;
                RefreshProperties();
                InitNewPrayerAsync().SafeFireAndForget();
            }
            else if (query.ContainsKey("load"))
            {
                if (query.ContainsKey("returnToCards"))
                {
                    ReturnToCards = true;
                    _savedQueryKey = Routes.QueryKeys.PrayerSaved;
                    _deletedQueryKey = Routes.QueryKeys.PrayerDeleted;
                }

                if (query.ContainsKey("viewOnly"))
                {
                    IsReadOnly = true;
                }
                else if (query.ContainsKey("edit"))
                {
                    IsReadOnly = false;
                }

                if (int.TryParse(query["load"].ToString(), out int _id))
                {
                    LoadPrayerAsync(_id).SafeFireAndForget();
                }
            }
            else if (query.ContainsKey("prayerSaved"))
            {
                // Navigated back to the view-only page after saving from the edit page.
                // Reload unconditionally — this page can only receive prayerSaved for
                // the prayer it was already displaying (no ID guard needed, and an ID
                // guard would be unreliable if LoadPrayerAsync hasn't completed yet).
                Reload();
            }
            else if (query.ContainsKey("saved"))
            {
                // Same scenario via the PrayerListPage (ReturnToCards = false) code path.
                Reload();
            }
        }

        private async Task LoadPrayerAsync(int id)
        {
            try
            {
                var result = await Prayer.LoadAsync(id);
                if (result is null)
                {
                    await _navigationService.GoToAsync("..");
                    return;
                }
                _prayer = result;
            }
            catch (Exception e)
            {
                await _navigationService.DisplayAlertAsync("Error", $"Failed to load prayer: {e.Message}", "OK");
                return;
            }
            _prayedCount = await _prayerService.GetInteractionCountByPrayerAsync(_prayer.Id);
            RecomputePrayedSummary();
            RefreshProperties();
            CaptureOriginals();
            await LoadCardsAsync();
            await LoadTagsAsync();
        }

        public void Reload()
        {
            if (_prayer.Id == 0) return;
            LoadPrayerAsync(_prayer.Id).SafeFireAndForget();
        }

        private async Task InitNewPrayerAsync()
        {
            CaptureOriginals();
            await LoadCardsAsync();
            await LoadTagsAsync();
        }

        private async Task LoadCardsAsync()
        {
            var cards = await _cardService.GetCardsAsync();
            AvailableCards.Clear();
            foreach (var card in cards)
                AvailableCards.Add(card);

            // Set SelectedCard to the current prayer's card
            _selectedCard = AvailableCards.FirstOrDefault(c => c.Id == _prayer.PrayerCardId);
            OnPropertyChanged(nameof(SelectedCard));

            // Issue #255: resolve the owning card's box so CanShare can evaluate
            // GetEffectiveProtectionMode (box-cascaded protection). One GetBoxesAsync
            // call reused across this load — mirrors PrayerCardsViewModel.RebuildSections'
            // per-sync box lookup rather than a per-card round-trip.
            _ownerBox = _selectedCard is not null
                ? (await _boxService.GetBoxesAsync()).FirstOrDefault(b => b.Id == _selectedCard.BoxId)
                : null;
            RaiseCanShareChanged();
        }

        private async Task LoadTagsAsync()
        {
            _allTags = (await _tagService.GetTagsAsync()).ToList();

            // For existing prayers, load their assigned tags from the DB.
            // For new prayers (Id <= 0), SelectedTags stays empty — user can still
            // add tags via the search entry; they'll be staged locally until save.
            if (_prayer.Id > 0)
            {
                var requestTags = await _tagService.GetTagsByRequestIdAsync(_prayer.Id);
                SelectedTags.Clear();
                foreach (var tag in requestTags.OrderBy(t => t.Name))
                    SelectedTags.Add(new TagChipViewModel(tag, RemoveTagFromDisplayAsync));
            }
        }

        /// <summary>Raised when the user taps the + button to open the tag picker modal.
        /// The view subscribes to this and pushes the modal page.</summary>
        public event Func<TagPickerViewModel, Task>? TagPickerRequested;

        private async Task OpenTagPickerAsync()
        {
            var pickerVm = new TagPickerViewModel(_tagService, _navigationService, _accessibilityService);
            // _allTags shared by reference so new tags created in the picker
            // are visible to SyncTagsFromPicker after dismissal.
            pickerVm.Initialize(_prayer.Id, _allTags, SelectedTags.Select(t => t.Id).ToList());

            if (TagPickerRequested is null)
                return;

            await TagPickerRequested.Invoke(pickerVm);
            await pickerVm.WaitForDismissAsync();

            // Sync tags back from the picker
            SyncTagsFromPicker(pickerVm);
        }

        private void SyncTagsFromPicker(TagPickerViewModel pickerVm)
        {
            var pickerTagIds = pickerVm.GetSelectedTagIds().ToHashSet();
            var currentTagIds = SelectedTags.Select(t => t.Id).ToHashSet();

            // Remove tags that were deselected in the picker
            foreach (var chip in SelectedTags.Where(t => !pickerTagIds.Contains(t.Id)).ToList())
                SelectedTags.Remove(chip);

            // Add tags that were newly selected in the picker
            foreach (var tagId in pickerTagIds.Where(id => !currentTagIds.Contains(id)))
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == tagId);
                if (tag is not null)
                    SelectedTags.Add(new TagChipViewModel(tag, RemoveTagFromDisplayAsync));
            }
        }

        private async Task RemoveTagFromDisplayAsync(int tagId)
        {
            // Only hit the DB if the prayer has been saved (has a real ID).
            if (_prayer.Id > 0)
                await _tagService.RemoveTagFromRequestAsync(_prayer.Id, tagId);
            var chip = SelectedTags.FirstOrDefault(t => t.Id == tagId);
            if (chip is not null)
                SelectedTags.Remove(chip);
        }

        private void CaptureOriginals()
        {
            _originalTitle = Title ?? string.Empty;
            _originalDetails = Details;
            _originalPrayerCardId = PrayerCardId;
            _originalCanNotify = CanNotify;
            _originalFrequency = PrayerFrequency;
            _originalIsAnswered = IsAnswered;
            _originalAnsweredAt = AnsweredAt;
            _originalNotifyHour = _prayer.NotifyHour;
            _originalNotifyMinute = _prayer.NotifyMinute;
            _originalDayOfWeek = _prayer.NotifyDayOfWeek;
            _originalDayOfMonth = _prayer.NotifyDayOfMonth;
        }

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(PrayerCardId));
            OnPropertyChanged(nameof(CanNotify));
            OnPropertyChanged(nameof(IsAnswered));
            OnPropertyChanged(nameof(AnsweredAt));
            OnPropertyChanged(nameof(AnsweredAtDisplay));
            OnPropertyChanged(nameof(CreatedAt));
            OnPropertyChanged(nameof(CreatedAtDisplay));
            OnPropertyChanged(nameof(PrayedSummary));
            OnPropertyChanged(nameof(HasBeenPrayedFor));
            OnPropertyChanged(nameof(UpdatedAt));
            OnPropertyChanged(nameof(Identifier));
            OnPropertyChanged(nameof(PrayerFrequency));
            OnPropertyChanged(nameof(PrayerFrequencyDisplay));
            OnPropertyChanged(nameof(NotifyTime));
            OnPropertyChanged(nameof(ShowNotifyTime));
            OnPropertyChanged(nameof(ShowDayOfWeek));
            OnPropertyChanged(nameof(ShowDayOfMonth));
            OnPropertyChanged(nameof(SelectedDayOfWeek));
            OnPropertyChanged(nameof(SelectedDayOfMonth));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsNotAnswered));
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(ShowSaveAndNew));
            OnPropertyChanged(nameof(CardTitle));
            OnPropertyChanged(nameof(AccessibleSummary));
        }
    }
}
