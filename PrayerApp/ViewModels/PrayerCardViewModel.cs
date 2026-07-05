using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace PrayerApp.ViewModels
{
    public class PrayerCardViewModel : ObservableObject, IQueryAttributable, IEditGuard
    {
        private PrayerCard _prayerCard;
        private bool _prayersLoaded;
        private bool _addingPrayer;
        private string _originalTitle = string.Empty;
        private readonly ICardService _cardService;
        private readonly IPrayerService _prayerService;
        private readonly IOnboardingService _onboardingService;
        private readonly INavigationService _navigationService;
        private readonly IAccessibilityService _accessibilityService;
        private readonly IBoxService _boxService;
        private readonly ISettings _settings;
        private readonly IConfidentialAccessService _confidentialAccessService;

        // Single-expand invariant lives on PrayerCardsViewModel.ExpandedCardId.
        // Per-card IsExpanded is a read-only projection over that — see the
        // property below. The page-VM raises RaiseIsExpandedChanged() on the
        // before-and-after card when ExpandedCardId mutates.
        // Public set: tests that bypass the CreateCardViewModel factory wire
        // this manually. Production wires it once via the factory; nothing
        // mutates it post-attach.
        public PrayerCardsViewModel? Parent { get; set; }

        /// <summary>
        /// True while SaveAsync is in flight. Drives the page-level ActivityIndicator
        /// and gates the SaveCommand canExecute so a double-tap can't duplicate the row.
        /// </summary>
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                // PerfLog.Log($"PrayerCardViewModel.IsBusy.set({value}) prev={_isBusy}");
                if (SetProperty(ref _isBusy, value))
                    (SaveCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand ArchiveCommand { get; }
        public ICommand SelectCardCommand { get; }
        public ICommand ToggleExpandedCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand AddPrayerCommand { get; }
        public ICommand ShareCommand { get; }
        public ICommand PrayCommand { get; }

        /// <summary>
        /// Resolves the transient prayer-selection service. Defaults to the DI
        /// container (same service-locator seam <see cref="ShareAsync"/> uses for
        /// <see cref="IDeepLinkService"/>); unit tests override it with a stub.
        /// Kept off the constructor so adding the per-card "Pray" chip doesn't
        /// churn every <c>new PrayerCardViewModel(...)</c> call-site.
        /// </summary>
        public Func<IPrayerSelectionService> SelectionServiceFactory { get; set; }
            = () => IPlatformApplication.Current!.Services.GetRequiredService<IPrayerSelectionService>();

        #region Properties

        public string Identifier => _prayerCard.Id.ToString();

        /// <summary>Backing model for batch operations (e.g., multi-select move).</summary>
        internal PrayerCard Card => _prayerCard;

        public int Id
        {
            get => _prayerCard.Id;
            set
            {
                if (_prayerCard.Id != value)
                {
                    _prayerCard.Id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Title
        {
            get => _prayerCard.Title;
            set
            {
                if (_prayerCard.Title != value)
                {
                    _prayerCard.Title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AccessibleCardHeader));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public bool IsFavorite
        {
            get => _prayerCard.IsFavorite;
            set
            {
                if (_prayerCard.IsFavorite != value)
                {
                    _prayerCard.IsFavorite = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FavoriteLabel));
                    OnPropertyChanged(nameof(AccessibleCardHeader));
                }
            }
        }

        /// <summary>
        /// The owning box, threaded in by <c>PrayerCardsViewModel.RebuildSections</c> at
        /// section-build time (issue #253) so <see cref="IsLockedVisible"/> can resolve
        /// <see cref="PrayerCard.GetEffectiveProtectionMode"/>, which needs the box for
        /// box-cascaded protection. Null until the first RebuildSections after construction
        /// (or in tests that don't wire it — box-less cards simply can't cascade).
        /// </summary>
        private CardBox? _box;
        public CardBox? Box
        {
            get => _box;
            set
            {
                if (!ReferenceEquals(_box, value))
                {
                    _box = value;
                    RaiseLockStateChanged();
                }
            }
        }

        /// <summary>
        /// True while this card's effective protection is <see cref="CardProtectionMode.LockedVisible"/>
        /// and the confidential-access session is locked (issue #253). Drives the cell's masked
        /// rendering (lock glyph + "Protected") via <see cref="DisplayTitle"/> — this property
        /// itself never exposes <see cref="Title"/>.
        /// </summary>
        public bool IsLockedVisible =>
            Parent is not null
            && !Parent.IsSessionUnlocked
            && PrayerCard.GetEffectiveProtectionMode(_prayerCard, _box) == CardProtectionMode.LockedVisible;

        /// <summary>
        /// Projected display value the cell binds to instead of <see cref="Title"/> (issue #253).
        /// Returns the literal "Protected" while <see cref="IsLockedVisible"/> is true so the
        /// real title never enters the visual tree while the session is locked — a stricter
        /// constraint than binding <see cref="Title"/> and toggling <c>IsVisible</c>, which
        /// would still place the real string in the compiled binding / element tree.
        /// </summary>
        public string DisplayTitle => IsLockedVisible ? "Protected" : Title;

        /// <summary>
        /// Re-raises the lock-state-derived projections. Called by <see cref="Box"/>'s setter
        /// and by <c>PrayerCardsViewModel.ShowConfidentialAsync</c> after a successful
        /// "Show confidential" authentication, so masked cells unmask immediately without
        /// a full cell re-template. Also re-evaluates <see cref="CanShare"/> (issue #255) so
        /// the Share button's disabled state tracks lock-state changes, not just
        /// ActivePrayerCount/ProtectionMode changes.
        /// </summary>
        internal void RaiseLockStateChanged()
        {
            OnPropertyChanged(nameof(IsLockedVisible));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(CanShare));
            ((IRelayCommand)ShareCommand).NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Card-level protection mode (Off/Locked/Hidden). Pure UI binding — no auth
        /// gating happens here (see #253/#254/#255); saving persists via the existing
        /// SaveCommand → SaveCardAsync path.
        /// </summary>
        public CardProtectionMode ProtectionMode
        {
            get => _prayerCard.ProtectionMode;
            set
            {
                if (_prayerCard.ProtectionMode != value)
                {
                    _prayerCard.ProtectionMode = value;
                    OnPropertyChanged();
                    RaiseLockStateChanged();
                }
            }
        }

        // Read-only derived. Single source of truth: PrayerCardsViewModel.ExpandedCardId.
        // The page-VM owns the singleton invariant — eliminates the cascade-collapse
        // handler and its `_suppressIsExpandedRebuild` flag (the smoke alarm: when a
        // flag suppresses side-effects you fired on purpose, the abstraction is wrong).
        public bool IsExpanded => Parent?.ExpandedCardId == _prayerCard.Id;

        // Internal — called by PrayerCardsViewModel when ExpandedCardId changes,
        // for both the previously-expanded card (collapse) and the newly-expanded
        // card (expand). All derived projections re-raise here.
        internal void RaiseIsExpandedChanged()
        {
            // INTENTIONAL — skip-3 design (PR #37 corrections, commit ac5afe2):
            // PrayersHeader recomputes via the ActivePrayerCount setter's notify chain.
            // Do NOT add OnPropertyChanged(nameof(PrayersHeader)) here — that defensive
            // notify was the marginal no-op flagged by /simplify and removed by user
            // override. Re-adding it would re-introduce dead code without fixing anything.
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(HasAnyPrayer));
            OnPropertyChanged(nameof(ShowActionChips));
            OnPropertyChanged(nameof(AccessibleCardHeader));
        }

        public bool IsSystem => _prayerCard.IsSystem;
        public bool IsImported => _prayerCard.IsImported;
        public int BoxId => _prayerCard.BoxId;
        public bool IsNew => _prayerCard.Id == 0;
        public bool CanDelete => !IsSystem && !IsNew;

        /// <summary>
        /// Gates the Share action (issue #255): disabled (not hidden — per
        /// .project/design-system.md "Required states") when this card is effectively
        /// protected and the confidential-access session is locked, in addition to the
        /// pre-existing IsSystem/has-active-prayers checks.
        /// </summary>
        public bool CanShare => !IsSystem && ActivePrayerCount > 0
            && !ProtectionPolicy.IsAccessBlocked(_prayerCard, _box, _confidentialAccessService.IsSessionUnlocked);
        /// <summary>Gates the "Pray" action chip — only meaningful when the card has active prayers.</summary>
        public bool CanPray => ActivePrayerCount > 0;
        public bool ShowActionChips => IsExpanded && !IsSystem;
        public string FavoriteLabel => IsFavorite ? "Favorited" : "Favorite";
        public bool IsArchived => _prayerCard.BoxId == _settings.ArchivedFolderId;
        public string ArchiveLabel => IsArchived ? "Unarchive" : "Archive";

        public bool IsDirty => Title != _originalTitle;

        public async Task<bool> CanLeaveAsync()
        {
            if (!IsDirty) return true;
            return await _navigationService.DisplayConfirmAsync(
                "Unsaved Changes", "Discard changes?", "Discard", "Cancel");
        }

        public bool IsAnswered
        {
            get => _prayerCard.IsAnswered;
            set
            {
                if (_prayerCard.IsAnswered != value)
                {
                    _prayerCard.IsAnswered = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (SetProperty(ref _isHighlighted, value))
                    OnPropertyChanged(nameof(CardVisualState));
            }
        }

        private bool _isMultiSelected;
        public bool IsMultiSelected
        {
            get => _isMultiSelected;
            set
            {
                if (SetProperty(ref _isMultiSelected, value))
                    OnPropertyChanged(nameof(CardVisualState));
            }
        }

        /// <summary>
        /// Drives the <c>CardStates</c> <see cref="Microsoft.Maui.Controls.VisualStateGroup"/>
        /// on the cell <c>Border</c> via <see cref="PrayerApp.Behaviors.VisualStateBindingBehavior"/>.
        /// Precedence: <c>MultiSelected</c> &gt; <c>Highlighted</c> &gt; <c>Normal</c>
        /// (matches the prior DataTrigger ordering on PrayerCardsPage).
        /// </summary>
        public string CardVisualState =>
            IsMultiSelected ? "MultiSelected"
            : IsHighlighted ? "Highlighted"
            : "Normal";

        // Mirrored from PrayerCardsViewModel.IsMultiSelectMode so the card check slot
        // in the DataTemplate can bind directly (CollectionView DataTemplate NameScope
        // makes {x:Reference cardCollection} unreliable; simpler to propagate here).
        private bool _isMultiSelectMode;
        public bool IsMultiSelectMode
        {
            get => _isMultiSelectMode;
            set => SetProperty(ref _isMultiSelectMode, value);
        }

        private int _activePrayerCount;
        public int ActivePrayerCount
        {
            get => _activePrayerCount;
            set
            {
                if (SetProperty(ref _activePrayerCount, value))
                {
                    OnPropertyChanged(nameof(CanShare));
                    OnPropertyChanged(nameof(CanPray));
                    OnPropertyChanged(nameof(HasAnyPrayer));
                    OnPropertyChanged(nameof(AccessibleCardHeader));
                    OnPropertyChanged(nameof(PrayersHeader));
                    ((IRelayCommand)ShareCommand).NotifyCanExecuteChanged();
                    ((IRelayCommand)PrayCommand).NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gates the parenthetical "(N)" prayer count Label on the collapsed card row
        /// (issue #33 P3). Hidden when the card has no active prayers — keeps an empty
        /// card's title row visually clean instead of trailing a "(0)". Also gated by
        /// !IsExpanded since the count is a collapsed-row affordance only.
        /// </summary>
        public bool HasAnyPrayer => !IsExpanded && ActivePrayerCount > 0;

        /// <summary>
        /// Display string for the inner "Prayers" section header inside an expanded card
        /// (issue #33 UAT-8). Returns "Prayers" when ActivePrayerCount is zero (clean
        /// affordance on an empty card) and "Prayers (N)" when there are active prayers.
        /// Mirrors ConfirmImportViewModel.PrayersHeader semantic for cross-page consistency.
        /// </summary>
        public string PrayersHeader =>
            ActivePrayerCount > 0 ? $"Prayers ({ActivePrayerCount})" : "Prayers";

        /// <summary>
        /// Composed accessible label for the card header. VoiceOver reads:
        /// "Quick Add, 3 prayers, Favorited, Imported, Collapsed".
        /// </summary>
        public string AccessibleCardHeader
        {
            get
            {
                var desc = Title;
                if (IsSystem) desc += ", System";
                if (!IsExpanded && ActivePrayerCount > 0)
                    desc += $", {ActivePrayerCount} prayer{(ActivePrayerCount == 1 ? "" : "s")}";
                if (IsFavorite) desc += ", Favorited";
                if (IsImported) desc += ", Imported";
                desc += IsExpanded ? ", Expanded" : ", Collapsed";
                return desc;
            }
        }

        public ObservableCollection<PrayerRequestDetailViewModel> Prayers { get; }

        public bool HasPrayers => Prayers.Count > 0;

        /// <summary>
        /// Factory for per-prayer row VMs built by <see cref="LoadPrayersAsync"/>.
        /// Default chains through <c>IPlatformApplication.Current.Services</c>;
        /// unit tests override with a stub-services factory.
        /// </summary>
        public Func<Prayer, PrayerRequestDetailViewModel> PrayerRowFactory { get; set; }
            = prayer => new PrayerRequestDetailViewModel(prayer) { ReturnToCards = true };

        /// <summary>Available collections for the picker on the card edit form. Excludes system boxes.</summary>
        public ObservableCollection<BoxPickerItem> AvailableBoxes { get; } = new();

        private BoxPickerItem? _selectedBox;
        public BoxPickerItem? SelectedBox
        {
            get => _selectedBox;
            set => SetProperty(ref _selectedBox, value);
        }

        #endregion

        #region Constructors

        public PrayerCardViewModel(ICardService cardService, IPrayerService prayerService,
            IOnboardingService onboardingService, INavigationService navigationService,
            IAccessibilityService accessibilityService, IBoxService boxService, ISettings settings,
            IConfidentialAccessService confidentialAccessService)
        {
            _prayerCard = new PrayerCard();
            _cardService = cardService;
            _prayerService = prayerService;
            _onboardingService = onboardingService;
            _navigationService = navigationService;
            _accessibilityService = accessibilityService;
            _boxService = boxService;
            _settings = settings;
            _confidentialAccessService = confidentialAccessService;
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsSystem);
            ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => !IsSystem);
            SelectCardCommand = new AsyncRelayCommand(SelectPrayerCardAsync, () => !IsSystem);
            ToggleExpandedCommand = new AsyncRelayCommand(ToggleExpandedAsync);
            ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => !IsSystem);
            AddPrayerCommand = new AsyncRelayCommand(AddPrayerAsync);
            ShareCommand = new AsyncRelayCommand(ShareAsync, () => CanShare);
            PrayCommand = new AsyncRelayCommand(PrayAsync, () => CanPray);
            Prayers = new ObservableCollection<PrayerRequestDetailViewModel>();
            Prayers.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasPrayers));
        }

        public PrayerCardViewModel() : this(
            IPlatformApplication.Current!.Services.GetRequiredService<ICardService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IPrayerService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IOnboardingService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IAccessibilityService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IBoxService>(),
            IPlatformApplication.Current!.Services.GetRequiredService<ISettings>(),
            IPlatformApplication.Current!.Services.GetRequiredService<IConfidentialAccessService>())
        { }

        public PrayerCardViewModel(PrayerCard pc) : this()
        {
            _prayerCard = pc;
            LoadActivePrayerCountAsync().SafeFireAndForget();
        }

        /// <summary>
        /// Constructor for creating a card VM with injected services and a PrayerCard model.
        /// Used by PrayerCardsViewModel to avoid dependency on IPlatformApplication in tests.
        /// </summary>
        public PrayerCardViewModel(PrayerCard pc, ICardService cardService, IPrayerService prayerService,
            IOnboardingService onboardingService, INavigationService navigationService,
            IAccessibilityService accessibilityService, IBoxService boxService, ISettings settings,
            IConfidentialAccessService confidentialAccessService)
            : this(cardService, prayerService, onboardingService, navigationService, accessibilityService, boxService, settings, confidentialAccessService)
        {
            _prayerCard = pc;
            LoadActivePrayerCountAsync().SafeFireAndForget();
        }

        private async Task LoadActivePrayerCountAsync()
        {
            // When prayers are already loaded locally, derive count without a service call
            if (_prayersLoaded)
                ActivePrayerCount = Prayers.Count(p => !p.IsAnswered);
            else
                ActivePrayerCount = await _prayerService.GetActivePrayerCountByCardAsync(_prayerCard.Id);
        }

        /// <summary>Refresh the badge count from the service (lightweight — single query).</summary>
        public void RefreshActivePrayerCount()
            => LoadActivePrayerCountAsync().SafeFireAndForget();

        /// <summary>Force-reload prayers from the service (e.g. after Save+ added new prayers).</summary>
        public void ReloadPrayers()
        {
            _prayersLoaded = false;
            LoadPrayersAsync().SafeFireAndForget();
        }

        #endregion

        #region Private Methods

        private async Task SaveAsync()
        {
            // PerfLog.Log("PrayerCardViewModel.SaveAsync.entry");
            // if (IsBusy) { PerfLog.Log("SaveAsync.early-return (already busy)"); return; }
            if (IsBusy) return;
            try
            {
                IsBusy = true;
                bool isNew = _prayerCard.Id == 0;
                // Card-edit picker is RealBoxPickerItem-only; defensive
                // pattern-match keeps the read type-safe and returns the
                // Loose-Cards default if a future refactor admits a sentinel.
                _prayerCard.BoxId = _selectedBox is RealBoxPickerItem real ? real.BoxId : 0;
                // PerfLog.Log("SaveAsync.before SaveCardAsync");
                await _cardService.SaveCardAsync(_prayerCard);
                // PerfLog.Log("SaveAsync.after SaveCardAsync");
                _originalTitle = Title; // Reset dirty state before navigation
                if (isNew)
                    _onboardingService.Advance(); // NameCard → AddRequest
                _accessibilityService.Announce("Card saved");
                // PerfLog.Log("SaveAsync.before GoToAsync");
                await _navigationService.GoToAsync($"..?{Routes.QueryKeys.Saved}={Identifier}");
                // PerfLog.Log("SaveAsync.after GoToAsync");
            }
            finally
            {
                // PerfLog.Log("SaveAsync.finally entering");
                IsBusy = false;
                // PerfLog.Log("SaveAsync.finally exited");
            }
        }

        private async Task DeleteAsync()
        {
            if (_prayerCard.IsSystem) return;

            var count = ActivePrayerCount;
            string detail = count > 0
                ? $"Delete \"{Title}\" and its {count} prayer request{(count == 1 ? "" : "s")}?"
                : $"Delete \"{Title}\"?";

            bool confirmed = await _navigationService.DisplayConfirmAsync(
                "Delete Card", detail, "Delete", "Cancel");

            if (!confirmed) return;

            var prayers = await _prayerService.GetPrayersByCardAsync(_prayerCard.Id);
            foreach (var prayer in prayers)
                await _prayerService.DeletePrayerAsync(prayer);
            await _cardService.DeleteCardAsync(_prayerCard);
            _accessibilityService.Announce("Card deleted");
            await _navigationService.GoToAsync($"..?{Routes.QueryKeys.Deleted}={Identifier}");
        }

        /// <summary>
        /// Issue #254 auth gate — mirrors the <see cref="_prayerCard"/>.IsSystem short-circuit
        /// pattern above SelectPrayerCardAsync/DeleteAsync, but for confidential cards. When
        /// this card is effectively protected (own <see cref="PrayerCard.ProtectionMode"/> or a
        /// box cascade) AND the confidential-access session is locked, prompts for
        /// authentication before letting the caller proceed. Returns true immediately for an
        /// unprotected card or an already-unlocked session (no auth prompt). Shared by both
        /// <see cref="SelectPrayerCardAsync"/> (open-card navigation) and
        /// <see cref="ToggleExpandedAsync"/> (in-place expand) so the condition is expressed once.
        /// Reads <see cref="_confidentialAccessService"/> directly rather than
        /// <c>Parent.IsSessionUnlocked</c> — it's the same singleton instance (see
        /// <see cref="PrayerCardsViewModel.CreateCardViewModel"/>), and this keeps the gate usable
        /// even before <see cref="Parent"/> is wired (e.g. <see cref="SelectCardCommand"/>'s
        /// canExecute-bypassed direct-execute tests).
        /// </summary>
        private async Task<bool> EnsureUnlockedForAccessAsync(string reason)
        {
            var isProtectedAndLocked =
                PrayerCard.GetEffectiveProtectionMode(_prayerCard, _box) != CardProtectionMode.None
                && !_confidentialAccessService.IsSessionUnlocked;
            if (!isProtectedAndLocked) return true;

            var unlocked = await _confidentialAccessService.AuthenticateAsync(reason);
            // Issue #296: the session just transitioned to unlocked via this in-path gate
            // (tap-to-expand or open-card) — re-raise every card's lock-derived projections
            // through the same mechanism ShowConfidentialAsync uses, so the bound cell (this
            // card's DisplayTitle/IsLockedVisible) refreshes immediately instead of staying
            // stale until some other event rebuilds the list. Parent is null-conditional:
            // this gate is intentionally usable before Parent is wired (see the doc comment
            // above), so a null Parent must remain a safe no-op.
            if (unlocked) Parent?.RefreshAfterUnlock();
            return unlocked;
        }

        private async Task SelectPrayerCardAsync()
        {
            if (_prayerCard.IsSystem) return;
            if (!await EnsureUnlockedForAccessAsync("Open protected card")) return;
            await _navigationService.GoToAsync($"{Routes.PrayerCardPage}?load={Identifier}");
        }

        private async Task ToggleExpandedAsync()
        {
            // PerfLog.Log($"ToggleExpanded.entry id={_prayerCard.Id} IsExpanded={IsExpanded} _prayersLoaded={_prayersLoaded} Prayers.Count={Prayers.Count}");
            // Delegate to the page-VM's ExpandedCardId so the singleton invariant
            // is enforced structurally instead of via a cascade handler.
            if (Parent is null) return;
            var nowExpanded = Parent.ExpandedCardId == _prayerCard.Id;

            // Issue #254: only the EXPAND direction reveals real content (LoadPrayersAsync +
            // ExpandedCardId), so the gate runs strictly before both when about to expand.
            // Collapsing a card never needs auth.
            if (!nowExpanded && !await EnsureUnlockedForAccessAsync("Expand protected card"))
                return;

            if (!nowExpanded && !_prayersLoaded)
            {
                // Load first, then reveal — avoids empty flash
                await LoadPrayersAsync();
            }
            Parent.ExpandedCardId = nowExpanded ? null : _prayerCard.Id;
        }

        private bool _isFavoriteSaving;
        private async Task ToggleFavoriteAsync()
        {
            if (_isFavoriteSaving) return;
            _isFavoriteSaving = true;
            try
            {
                IsFavorite = !IsFavorite;
                await _cardService.SaveCardAsync(_prayerCard);
                _accessibilityService.Announce(IsFavorite ? "Marked as favorite" : "Removed from favorites");
            }
            finally
            {
                _isFavoriteSaving = false;
            }
        }

        private bool _isArchiveSaving;
        private async Task ArchiveAsync()
        {
            if (_prayerCard.IsSystem) return;
            if (_isArchiveSaving) return;

            var archiving = !IsArchived;
            if (archiving)
            {
                var confirmed = await _navigationService.DisplayConfirmAsync(
                    "Archive Card?",
                    "Are you sure you want to Archive this card? You can find it in your Archived Cards collection.",
                    "Archive",
                    "Cancel");
                if (!confirmed) return;
            }

            _isArchiveSaving = true;
            try
            {
                int target;
                if (archiving)
                {
                    // Stash the current collection so unarchive can restore it.
                    _prayerCard.PreArchiveBoxId = _prayerCard.BoxId;
                    target = _settings.ArchivedFolderId;
                }
                else
                {
                    // Restore to the original collection if it still exists; otherwise Loose Cards.
                    target = 0;
                    var stashed = _prayerCard.PreArchiveBoxId;
                    if (stashed is int boxId && boxId != 0)
                    {
                        var boxes = await _boxService.GetBoxesAsync();
                        if (boxes.Any(b => b.Id == boxId))
                            target = boxId;
                    }
                    _prayerCard.PreArchiveBoxId = null;
                }

                await _cardService.AssignBoxAsync(_prayerCard, target);
                OnPropertyChanged(nameof(IsArchived));
                OnPropertyChanged(nameof(ArchiveLabel));

                _accessibilityService.Announce(archiving ? "Card archived" : "Card unarchived");

                // Collapse the expanded card after archiving — it leaves this section.
                if (archiving && Parent is not null)
                    Parent.ExpandedCardId = null;
            }
            finally
            {
                _isArchiveSaving = false;
            }
        }

        private async Task AddPrayerAsync()
        {
            _onboardingService.Advance(); // AddRequest → NameRequest
            await _navigationService.GoToAsync($"{Routes.PrayerDetailPage}?newForCard={_prayerCard.Id}");
        }

        private async Task ShareAsync()
        {
            if (_prayerCard.IsSystem) return;
            // Issue #255 defense in depth: CanShare already gates ShareCommand's
            // canExecute (disabling the button), but guard the action itself in case it's
            // ever invoked directly (e.g. ExecuteAsync bypassing canExecute).
            if (!CanShare) return;
            var allPrayers = await _prayerService.GetPrayersByCardAsync(_prayerCard.Id);
            var activePrayers = allPrayers
                .Where(p => !p.IsAnswered && !string.IsNullOrWhiteSpace(p.Title))
                .ToList();
            var deepLinkService = IPlatformApplication.Current!.Services.GetRequiredService<IDeepLinkService>();
            await deepLinkService.ShareCardAsync(_prayerCard, activePrayers);
            _onboardingService.Advance();
        }

        private async Task PrayAsync()
        {
            // Resolve this card's active prayer IDs and hand them to the transient
            // selection service, then launch Prayer Time in scope=selection. The ID
            // set travels via the service (not the URL) to match the "Choose cards"
            // modal path and keep the Shell nav URL small.
            // Resolve via GetAllActivePrayersAsync so the "active" definition (!IsAnswered)
            // lives in one place — the service — rather than being re-stated inline here.
            var activeIds = (await _prayerService.GetAllActivePrayersAsync())
                .Where(p => p.PrayerCardId == _prayerCard.Id)
                .Select(p => p.Id)
                .ToList();
            if (activeIds.Count == 0) return;

            SelectionServiceFactory().Set(activeIds);
            await _navigationService.GoToAsync(
                $"{Routes.PrayerTimePage}?scope={Routes.ScopeSelection}");
        }

        #endregion

        #region Implemented Contract Methods

        void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.ContainsKey("load"))
            {
                if (int.TryParse(query["load"].ToString(), out int _id))
                {
                    LoadPrayerCardAsync(_id).SafeFireAndForget();
                }
            }
        }

        #endregion

        #region Helper Methods

        private async Task LoadPrayerCardAsync(int id)
        {
            try
            {
                var loaded = await PrayerCard.LoadAsync(id);
                if (loaded is null)
                {
                    await _navigationService.GoToAsync("..");
                    return;
                }
                _prayerCard = loaded;
            }
            catch (Exception e)
            {
                await _navigationService.DisplayAlertAsync("Error", $"Failed to load card: {e.Message}", "OK");
                return;
            }

            _originalTitle = _prayerCard.Title ?? string.Empty;
            RefreshProperties();
            await LoadBoxPickerAsync();
        }

        public async Task LoadBoxPickerAsync()
        {
            var items = await BoxPickerItems.GetBoxPickerItemsAsync(_boxService);
            AvailableBoxes.Clear();

            // "Loose Cards" (no collection) is always the first helper item.
            var looseCards = items[0];
            foreach (var item in items)
                AvailableBoxes.Add(item);

            // Set selection to match current card's BoxId — the card-edit
            // picker only contains RealBoxPickerItem rows (no All-collections
            // sentinel), so OfType<RealBoxPickerItem> is a no-op cast filter
            // that yields the right element type for the BoxId predicate.
            SelectedBox = AvailableBoxes
                              .OfType<RealBoxPickerItem>()
                              .FirstOrDefault(b => b.BoxId == _prayerCard.BoxId)
                          ?? looseCards;
        }

        public void Reload()
        {
            LoadPrayerCardAsync(_prayerCard.Id).SafeFireAndForget();
        }

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsFavorite));
            OnPropertyChanged(nameof(ProtectionMode));
            OnPropertyChanged(nameof(IsSystem));
            OnPropertyChanged(nameof(IsImported));
            OnPropertyChanged(nameof(BoxId));
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanShare));
            OnPropertyChanged(nameof(HasPrayers));
            OnPropertyChanged(nameof(IsAnswered));
            OnPropertyChanged(nameof(AccessibleCardHeader));
        }

        private static IOrderedEnumerable<T> SortPrayers<T>(IEnumerable<T> source, Func<T, bool> isAnswered, Func<T, DateTime?> answeredAt, Func<T, string> title) =>
            source.OrderBy(isAnswered).ThenByDescending(p => answeredAt(p) ?? DateTime.MinValue).ThenBy(title);

        /// <summary>
        /// In-place reorder of <see cref="Prayers"/> via <see cref="ObservableCollection{T}.Move"/>
        /// after a single-row update (e.g., Mark Answered) — keeps the row in its correct
        /// slot without a Clear+Add realize storm.
        /// </summary>
        public void ResortPrayers()
        {
            var sorted = SortPrayers(Prayers, p => p.IsAnswered, p => p.AnsweredAt, p => p.Title).ToList();

            // Fast path: list already in order. Avoids N IndexOf calls and any
            // Move-induced CollectionChanged churn on the common non-reordering edit.
            var inOrder = true;
            for (int i = 0; i < sorted.Count; i++)
                if (!ReferenceEquals(sorted[i], Prayers[i])) { inOrder = false; break; }
            if (inOrder) return;

            for (int targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
            {
                var currentIndex = Prayers.IndexOf(sorted[targetIndex]);
                if (currentIndex != targetIndex)
                    Prayers.Move(currentIndex, targetIndex);
            }
        }

        public async Task LoadPrayersAsync()
        {
            // PerfLog.Log($"LoadPrayers.entry id={_prayerCard.Id}");
            try
            {
                var prayers = await _prayerService.GetPrayersByCardAsync(_prayerCard.Id);
                Prayers.Clear();
                foreach (var prayer in SortPrayers(prayers, p => p.IsAnswered, p => p.AnsweredAt, p => p.Title))
                {
                    Prayers.Add(PrayerRowFactory(prayer));
                }

                _prayersLoaded = true;
                OnPropertyChanged(nameof(HasPrayers));
                // PerfLog.Log($"LoadPrayers.exit id={_prayerCard.Id} Prayers.Count={Prayers.Count} dbReturned={prayers.Count}");
            }
            catch (Exception e)
            {
                await _navigationService.DisplayAlertAsync("Error", $"Failed to load prayers: {e.Message}", "OK");
            }
        }

        public async Task AddOrUpdatePrayerAsync(int prayerId)
        {
            if (_addingPrayer) return;
            _addingPrayer = true;
            try
            {
                // If prayers haven't been loaded yet, load them first so we can
                // display the new/updated prayer in the expanded accordion.
                if (!_prayersLoaded)
                    await LoadPrayersAsync();

                var existing = Prayers.FirstOrDefault(p => p.Id == prayerId);
                if (existing != null)
                {
                    existing.Reload();
                    ResortPrayers();
                    return;
                }

                var prayer = await Prayer.LoadAsync(prayerId);
                if (prayer is null) return;
                if (prayer.PrayerCardId != _prayerCard.Id)
                {
                    return;
                }

                // Route through PrayerRowFactory (same seam LoadPrayersAsync uses) so the
                // in-place insert honors test-injected row VMs; the production default
                // factory is `new PrayerRequestDetailViewModel(p) { ReturnToCards = true }`,
                // so prod behavior is unchanged.
                var viewModel = PrayerRowFactory(prayer);
                var insertIndex = Prayers
                    .TakeWhile(p => string.Compare(p.Title, prayer.Title, StringComparison.OrdinalIgnoreCase) < 0)
                    .Count();
                Prayers.Insert(insertIndex, viewModel);
                OnPropertyChanged(nameof(HasPrayers));
                LoadActivePrayerCountAsync().SafeFireAndForget();
            }
            finally { _addingPrayer = false; }
        }

        public void RemovePrayer(int prayerId)
        {
            if (_prayersLoaded)
            {
                var existing = Prayers.FirstOrDefault(p => p.Id == prayerId);
                if (existing != null)
                {
                    Prayers.Remove(existing);
                    OnPropertyChanged(nameof(HasPrayers));
                }
            }

            // Always refresh the badge count, even if prayers weren't loaded
            RefreshActivePrayerCount();
        }

        #endregion
    }

    /// <summary>
    /// Shared loader for the Collection picker's box rows. Returns the same
    /// list both <see cref="PrayerCardViewModel.LoadBoxPickerAsync"/> and
    /// <see cref="ConfirmImportViewModel.LoadBoxesAsync"/> need:
    /// "Loose Cards" (BoxId 0) first, then user-created boxes in
    /// <see cref="IBoxService.GetBoxesAsync"/> order. System / archived boxes
    /// are excluded. The <see cref="AllCollectionsPickerItem"/> sentinel is
    /// NOT produced here — ConfirmImport inserts/removes it separately when
    /// ImportMode flips. Both callers rely on element 0 being Loose Cards.
    /// </summary>
    public static class BoxPickerItems
    {
        public static async Task<List<RealBoxPickerItem>> GetBoxPickerItemsAsync(IBoxService boxService)
        {
            var boxes = await boxService.GetBoxesAsync();

            // "Loose Cards" (no collection) always first
            var items = new List<RealBoxPickerItem>
            {
                new(0, BoxStrings.Unorganized)
            };

            // User-created boxes only (no System/Archived)
            foreach (var box in boxes.Where(b => !b.IsSystem))
                items.Add(new RealBoxPickerItem(box.Id, box.Name));

            return items;
        }
    }

    /// <summary>
    /// Picker source row for the Collection field. Two flavours, enforced
    /// by the type system rather than a runtime sentinel flag:
    ///   • <see cref="RealBoxPickerItem"/> — a real CardBox row (or the
    ///     synthesised Loose Cards bucket at BoxId=0). Carries a BoxId.
    ///   • <see cref="AllCollectionsPickerItem"/> — the "no filter" sentinel,
    ///     only valid in Confirm-Import / Existing-Card mode. Has no BoxId.
    /// Pattern-match on <see cref="RealBoxPickerItem"/> before reading
    /// BoxId; the compiler then enforces "check before use" instead of
    /// callsites remembering to skip <c>== -1</c>.
    /// </summary>
    public abstract class BoxPickerItem
    {
        /// <summary>Display label for the Picker row.</summary>
        public string Name { get; protected init; } = string.Empty;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A real (or Loose-Cards) box. Equals/GetHashCode by <see cref="BoxId"/>
    /// so MAUI Picker SelectedItem binding round-trips correctly across
    /// re-issued AvailableBoxes lists.
    /// </summary>
    public sealed class RealBoxPickerItem : BoxPickerItem
    {
        public int BoxId { get; }

        public RealBoxPickerItem(int boxId, string name)
        {
            BoxId = boxId;
            Name = name;
        }

        public override bool Equals(object? obj) =>
            obj is RealBoxPickerItem other && BoxId == other.BoxId;

        public override int GetHashCode() => BoxId.GetHashCode();
    }

    /// <summary>
    /// Singleton "no collection filter" picker entry — used only on the
    /// Confirm Import page in Existing-Card mode.
    /// <see cref="ConfirmImportViewModel.LoadBoxesAsync"/> does not produce
    /// this; it is inserted/removed when ImportMode flips. Equality is
    /// reference-based via the singleton instance.
    /// </summary>
    public sealed class AllCollectionsPickerItem : BoxPickerItem
    {
        public static AllCollectionsPickerItem Instance { get; } = new();

        private AllCollectionsPickerItem()
        {
            Name = "All collections";
        }
    }
}
