using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class PrayerListViewModelTests
{
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly ITagService _tagService = Substitute.For<ITagService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IPrayerSelectionService _selectionService = Substitute.For<IPrayerSelectionService>();
    private readonly IMessenger _messenger = new WeakReferenceMessenger();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    // Issue #255: gates search-result exclusion of effectively-protected prayers while
    // locked. Fake mirrors the #251/#253/#254 seam-faking approach — defaults to locked
    // (IsSessionUnlocked=false) so existing tests exercise the unprotected path unless a
    // card explicitly sets ProtectionMode.
    private readonly IConfidentialAccessService _confidentialAccessService = Substitute.For<IConfidentialAccessService>();

    // Extra mocks needed only to build stub prayer rows via the internal test-only
    // PrayerRequestDetailViewModel ctor (the production (Prayer) ctor chains through
    // IPlatformApplication.Current, which unit tests can't stub).
    private readonly IOnboardingService _onboardingService = Substitute.For<IOnboardingService>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();

    public PrayerListViewModelTests()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());
    }

    private PrayerListViewModel CreateSut() =>
        new(_prayerService, _cardService, _tagService, _navigationService, _accessibilityService, _settings,
            _selectionService, _messenger, _boxService, _confidentialAccessService)
        {
            PrayerRowFactory = BuildStubPrayerRowVm
        };

    private PrayerRequestDetailViewModel BuildStubPrayerRowVm(Prayer p)
        => new(p, _prayerService, _tagService, _cardService, _onboardingService,
            _notificationService, _navigationService, _accessibilityService, _settings,
            _boxService, _confidentialAccessService);

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultState()
    {
        var sut = CreateSut();

        Assert.True(sut.IsLoading); // starts true
        Assert.Empty(sut.AllPrayers);
        Assert.Empty(sut.FilteredPrayers);
        Assert.Equal(FilterStatus.Active, sut.StatusFilter);
        Assert.Equal(string.Empty, sut.SearchText);
    }

    // ── IsLoading announces ───────────────────────────────────────────

    [Fact]
    public void IsLoading_TransitionAnnounces()
    {
        var sut = CreateSut();
        _accessibilityService.ClearReceivedCalls();

        sut.IsLoading = false;
        _accessibilityService.Received(1).Announce("Content loaded");

        sut.IsLoading = true;
        _accessibilityService.Received(1).Announce("Loading");
    }

    // ── StatusFilter ──────────────────────────────────────────────────

    [Fact]
    public void SetStatusCommand_ChangesFilter()
    {
        var sut = CreateSut();

        sut.SetStatusCommand.Execute("Answered");
        Assert.Equal(FilterStatus.Answered, sut.StatusFilter);
        Assert.True(sut.IsAnsweredSelected);
        Assert.False(sut.IsActiveSelected);

        sut.SetStatusCommand.Execute("All");
        Assert.Equal(FilterStatus.All, sut.StatusFilter);
        Assert.True(sut.IsAllSelected);

        sut.SetStatusCommand.Execute("Overdue");
        Assert.Equal(FilterStatus.Overdue, sut.StatusFilter);
        Assert.True(sut.IsOverdueSelected);

        sut.SetStatusCommand.Execute("Active");
        Assert.Equal(FilterStatus.Active, sut.StatusFilter);
        Assert.True(sut.IsActiveSelected);
    }

    // ── NewCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task NewCommand_NavigatesToNewPrayer()
    {
        var sut = CreateSut();

        await ((IAsyncRelayCommand)sut.NewCommand).ExecuteAsync(null);

        await _navigationService.Received(1).GoToAsync(
            Arg.Is<string>(s => s.Contains(Routes.PrayerDetailPage) && s.Contains("new=true")));
    }

    // ── SearchText ────────────────────────────────────────────────────

    [Fact]
    public void SearchText_PropertyChanges()
    {
        var sut = CreateSut();
        var changed = false;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrayerListViewModel.SearchText))
                changed = true;
        };

        sut.SearchText = "test";

        Assert.True(changed);
        Assert.Equal("test", sut.SearchText);
    }

    // ── SyncAsync no longer invalidates service cache ─────────────────
    // Slice 3: services auto-invalidate on mutation (Slice 2). VMs trust the cache.

    [Fact]
    public async Task SyncAsync_DoesNotInvalidateServiceCache()
    {
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _settings.OverdueDayThreshold.Returns(30);
        _prayerService.GetOverduePrayersAsync(30).Returns(new List<Prayer>().AsReadOnly());
        _db_Setup();

        var sut = CreateSut();
        await sut.SyncAsync();

        _prayerService.DidNotReceive().InvalidateCache();
    }

    // ── HasTags ───────────────────────────────────────────────────────

    [Fact]
    public void HasTags_InitiallyFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.HasTags);
    }

    // Helper to set up DB mocks needed for SyncAsync (PrayerCardTag.LoadAllAsync)
    private void _db_Setup()
    {
        var db = Substitute.For<IDBService>();
        PrayerCardTag.SetDBService(db);
        db.GetAllAsync<PrayerCardTag>().Returns(new List<PrayerCardTag>());
    }

    // ── #49 — tag preselect survives empty AvailableTags ──────────────
    // Lifecycle race: Shell's ApplyQueryAttributes fires the tagId preselect
    // BEFORE OnAppearing → SyncAsync populates AvailableTags. The lookup
    // against the empty collection finds nothing; before the fix, the
    // unconditional `_preselectedTagId = 0` reset wiped the request, so the
    // post-population re-call from SyncCoreAsync early-returned and the
    // filter never applied. Repro: ApplyQueryAttributes → then SyncAsync.

    [Fact]
    public async Task ApplyQueryAttributes_TagId_BeforeSync_AppliesAfterPopulation()
    {
        // Arrange — only the targeted tag exists; no prayers needed (the
        // chip-selection contract is what regresses, not the row filter).
        var tag = new PrayerTag { Id = 42, Name = "Family" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _settings.OverdueDayThreshold.Returns(30);
        _prayerService.GetOverduePrayersAsync(30).Returns(new List<Prayer>().AsReadOnly());
        _db_Setup();

        var sut = CreateSut();
        Assert.Empty(sut.AvailableTags); // precondition: chips not yet populated

        // Act 1 — Shell delivers the tagId before OnAppearing fires SyncAsync
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["tagId"] = "42" });

        // Act 2 — OnAppearing fires SyncAsync, populating chips
        await sut.SyncAsync();

        // Assert — chip exists, is selected, and StatusFilter flipped to All
        // (so a future row matching that tag wouldn't be hidden by the
        // default Active-only filter).
        var chip = Assert.Single(sut.AvailableTags);
        Assert.Equal(42, chip.Tag.Id);
        Assert.True(chip.IsSelected, "preselected tag chip should be selected after Sync populates AvailableTags");
        Assert.Equal(FilterStatus.All, sut.StatusFilter);
    }

    // ── Slice 6a — single-flight + coalesce-pending SyncAsync ─────────

    [Fact]
    public async Task SyncAsync_BurstOfThreeConcurrent_CoalescesToTwoFetches()
    {
        // See PrayerCardsViewModelTests for full context. Same coalesce contract.
        var gate = new TaskCompletionSource<IReadOnlyList<PrayerCard>>();
        _cardService.GetCardsAsync().Returns(gate.Task);
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _settings.OverdueDayThreshold.Returns(30);
        _prayerService.GetOverduePrayersAsync(30).Returns(new List<Prayer>().AsReadOnly());
        _db_Setup();

        var sut = CreateSut();

        var t1 = sut.SyncAsync();
        var t2 = sut.SyncAsync();
        var t3 = sut.SyncAsync();

        gate.SetResult(new List<PrayerCard>().AsReadOnly());
        await Task.WhenAll(t1, t2, t3);

        await _cardService.Received(2).GetCardsAsync();
    }

    // ── #5 — StartPrayerTimeCommand (launch Prayer Time from filtered view) ──

    // Helper: fully mock SyncAsync's dependencies for an arbitrary prayer set,
    // wiring card titles so text/card search behaves realistically.
    private void SetupSync(IEnumerable<Prayer> prayers, IEnumerable<PrayerCard>? cards = null,
        IEnumerable<PrayerTag>? tags = null)
    {
        _prayerService.GetAllPrayersAsync().Returns(prayers.ToList().AsReadOnly());
        _cardService.GetCardsAsync().Returns((cards ?? new List<PrayerCard>()).ToList().AsReadOnly());
        _tagService.GetTagsAsync().Returns((tags ?? new List<PrayerTag>()).ToList().AsReadOnly());
        _settings.OverdueDayThreshold.Returns(30);
        _prayerService.GetOverduePrayersAsync(30).Returns(new List<Prayer>().AsReadOnly());
        _db_Setup();
    }

    [Fact]
    public async Task StartPrayerTimeCommand_SetsActiveIdsAndNavigatesToSelectionScope()
    {
        SetupSync(new[]
        {
            new Prayer { Id = 1, Title = "Aunt",   IsAnswered = false },
            new Prayer { Id = 2, Title = "Friend", IsAnswered = false },
            new Prayer { Id = 3, Title = "Done",   IsAnswered = true  }, // answered → excluded
        });

        var sut = CreateSut();
        await sut.SyncAsync(); // default Active filter → 1 & 2 visible, 3 hidden

        await ((IAsyncRelayCommand)sut.StartPrayerTimeCommand).ExecuteAsync(null);

        _selectionService.Received(1).Set(Arg.Is<IEnumerable<int>>(ids =>
            ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2 })));
        await _navigationService.Received(1).GoToAsync(
            $"{Routes.PrayerTimePage}?scope={Routes.ScopeSelection}");
    }

    [Fact]
    public async Task StartPrayerTimeCommand_ExcludesAnsweredEvenWhenVisible()
    {
        // StatusFilter=All shows answered rows, but they must NOT be prayed through.
        SetupSync(new[]
        {
            new Prayer { Id = 10, Title = "Active", IsAnswered = false },
            new Prayer { Id = 11, Title = "Answered", IsAnswered = true },
        });

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.SetStatusCommand.Execute("All"); // both rows now visible

        await ((IAsyncRelayCommand)sut.StartPrayerTimeCommand).ExecuteAsync(null);

        _selectionService.Received(1).Set(Arg.Is<IEnumerable<int>>(ids =>
            ids.SequenceEqual(new[] { 10 })));
    }

    [Fact]
    public async Task StartPrayerTimeCommand_CanExecuteFalse_WhenNoActivePrayersInView()
    {
        // Answered filter over an answered-only set → 0 active in view.
        SetupSync(new[]
        {
            new Prayer { Id = 20, Title = "Done1", IsAnswered = true },
            new Prayer { Id = 21, Title = "Done2", IsAnswered = true },
        });

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.SetStatusCommand.Execute("Answered"); // both answered rows visible, none active

        Assert.False(((IRelayCommand)sut.StartPrayerTimeCommand).CanExecute(null));
    }

    [Fact]
    public async Task StartPrayerTimeCommand_CanExecuteTrue_WhenAtLeastOneActiveInView()
    {
        SetupSync(new[] { new Prayer { Id = 30, Title = "Active", IsAnswered = false } });

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.True(((IRelayCommand)sut.StartPrayerTimeCommand).CanExecute(null));
    }

    [Fact]
    public async Task StartPrayerTimeCommand_HonorsCombinedTextStatusTagFilter()
    {
        var family = new PrayerTag { Id = 100, Name = "Family" };
        var card = new PrayerCard { Id = 7, Title = "People" };
        SetupSync(
            new[]
            {
                new Prayer { Id = 1, Title = "Mom visit",   IsAnswered = false, PrayerCardId = 7 },
                new Prayer { Id = 2, Title = "Mom recovery", IsAnswered = false, PrayerCardId = 7 },
                new Prayer { Id = 3, Title = "Dad",          IsAnswered = false, PrayerCardId = 7 },
                new Prayer { Id = 4, Title = "Mom answered", IsAnswered = true,  PrayerCardId = 7 },
            },
            cards: new[] { card },
            tags: new[] { family });

        // Tag rows: prayers 1 and 4 carry the Family tag (3 does not).
        var db = Substitute.For<IDBService>();
        PrayerCardTag.SetDBService(db);
        db.GetAllAsync<PrayerCardTag>().Returns(new List<PrayerCardTag>
        {
            new() { PrayerRequestId = 1, PrayerTagId = 100 },
            new() { PrayerRequestId = 4, PrayerTagId = 100 },
        });

        var sut = CreateSut();
        await sut.SyncAsync();

        sut.SearchText = "Mom"; // titles 1,2,4 match
        sut.AvailableTags.Single(c => c.Tag.Id == 100).ToggleCommand.Execute(null); // narrow to 1,4 + re-filters
        // status defaults Active → drops answered #4. Net active-in-view: {1}

        await ((IAsyncRelayCommand)sut.StartPrayerTimeCommand).ExecuteAsync(null);

        _selectionService.Received(1).Set(Arg.Is<IEnumerable<int>>(ids =>
            ids.SequenceEqual(new[] { 1 })));
    }

    [Fact]
    public async Task StartPrayerTimeCommand_DoesNotResetSearchOrFilterState()
    {
        SetupSync(new[] { new Prayer { Id = 1, Title = "Keep", IsAnswered = false } });

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.SetStatusCommand.Execute("All");
        sut.SearchText = "Keep";

        await ((IAsyncRelayCommand)sut.StartPrayerTimeCommand).ExecuteAsync(null);

        // Navigating away preserves the underlying page's filter state.
        Assert.Equal(FilterStatus.All, sut.StatusFilter);
        Assert.Equal("Keep", sut.SearchText);
    }

    // ── Issue #255 — search exclusion of effectively-protected prayers while locked ──

    [Fact]
    public async Task ApplyFilter_ProtectedPrayer_SessionLocked_ExcludedFromFilteredView()
    {
        var protectedCard = new PrayerCard { Id = 1, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        var openCard = new PrayerCard { Id = 2, Title = "Open", ProtectionMode = CardProtectionMode.None };
        SetupSync(new[]
        {
            new Prayer { Id = 100, Title = "Protected Prayer", PrayerCardId = 1, IsAnswered = false },
            new Prayer { Id = 200, Title = "Open Prayer", PrayerCardId = 2, IsAnswered = false }
        }, cards: new[] { protectedCard, openCard });
        _confidentialAccessService.IsSessionUnlocked.Returns(false);

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.DoesNotContain(sut.FilteredPrayers, p => p.Id == 100);
        Assert.Contains(sut.FilteredPrayers, p => p.Id == 200);
    }

    [Fact]
    public async Task ApplyFilter_ProtectedPrayer_SessionUnlocked_IncludedInFilteredView()
    {
        var protectedCard = new PrayerCard { Id = 1, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        SetupSync(new[]
        {
            new Prayer { Id = 100, Title = "Protected Prayer", PrayerCardId = 1, IsAnswered = false }
        }, cards: new[] { protectedCard });
        _confidentialAccessService.IsSessionUnlocked.Returns(true);

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Contains(sut.FilteredPrayers, p => p.Id == 100);
    }

    [Fact]
    public async Task ApplyFilter_ProtectedPrayerViaBoxCascade_SessionLocked_Excluded()
    {
        var box = new CardBox { Id = 7, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.Hidden };
        _boxService.GetBoxesAsync().Returns(new List<CardBox> { box }.AsReadOnly());
        var card = new PrayerCard { Id = 1, Title = "Cascaded", ProtectionMode = CardProtectionMode.None, BoxId = 7 };
        SetupSync(new[]
        {
            new Prayer { Id = 100, Title = "Prayer", PrayerCardId = 1, IsAnswered = false }
        }, cards: new[] { card });
        _confidentialAccessService.IsSessionUnlocked.Returns(false);

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.DoesNotContain(sut.FilteredPrayers, p => p.Id == 100);
    }
}
