using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using NSubstitute;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class PrayerTimeViewModelTests
{
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly ITagService _tagService = Substitute.For<ITagService>();
    private readonly IPrayerInteractionService _interactionService = Substitute.For<IPrayerInteractionService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IPrayerSelectionService _selectionService = Substitute.For<IPrayerSelectionService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IDispatcherTimer _autoTimer = Substitute.For<IDispatcherTimer>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    // Issue #255: Prayer Time always re-authenticates when the session includes a
    // protected prayer, regardless of IsSessionUnlocked. Fake mirrors #251/#253/#254 —
    // defaults to locked via NSubstitute's bool default.
    private readonly IConfidentialAccessService _confidentialAccessService = Substitute.For<IConfidentialAccessService>();

    public PrayerTimeViewModelTests()
    {
        _settings.AutoModeIntervalSeconds.Returns(30);
        // Use a non-zero ID so cards with BoxId=0 are not incorrectly treated as archived.
        _settings.ArchivedFolderId.Returns(999);
        _dispatcher.CreateTimer().Returns(_autoTimer);
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());
    }

    private PrayerTimeViewModel CreateSut() =>
        new(_prayerService, _cardService, _tagService, _interactionService,
            _navigationService, _accessibilityService, _notificationService, _settings,
            _selectionService, _dispatcher, _boxService, _confidentialAccessService);

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultState()
    {
        var sut = CreateSut();

        Assert.True(sut.IsLoading);
        Assert.Empty(sut.Entries);
        Assert.Equal(30, sut.SelectedIntervalSeconds);
        Assert.False(sut.IsAutoMode);
        Assert.False(sut.HasCompleted);
    }

    // ── SelectedIntervalSeconds persists ──────────────────────────────

    [Fact]
    public void Constructor_ReadsIntervalFromSettings()
    {
        _settings.AutoModeIntervalSeconds.Returns(120);

        var sut = CreateSut();

        Assert.Equal(120, sut.SelectedIntervalSeconds);
    }

    // ── ProgressDisplay ───────────────────────────────────────────────

    [Fact]
    public void ProgressDisplay_NoEntries_Empty()
    {
        var sut = CreateSut();
        Assert.Equal(string.Empty, sut.ProgressDisplay);
    }

    // ── EndSessionCommand ─────────────────────────────────────────────

    [Fact]
    public async Task EndSessionCommand_NavigatesBack()
    {
        var sut = CreateSut();

        await ((IAsyncRelayCommand)sut.EndSessionCommand).ExecuteAsync(null);

        await _navigationService.Received(1).GoToAsync("..");
    }

    // ── HasPrevious / HasNext ─────────────────────────────────────────

    [Fact]
    public void HasPrevious_AtZero_False()
    {
        var sut = CreateSut();
        Assert.False(sut.HasPrevious);
    }

    [Fact]
    public void HasNext_NoEntries_False()
    {
        var sut = CreateSut();
        Assert.False(sut.HasNext);
    }

    // ── scope=box filtering ──────────────────────────────────────────

    [Fact]
    public async Task ApplyQueryAttributes_BoxScope_FiltersToBoxCards()
    {
        // Card 1 is in box 5, Card 2 is in a different box
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Family", BoxId = 5 },
            new() { Id = 2, Title = "Work", BoxId = 10 }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Prayer A", PrayerCardId = 1 },
            new() { Id = 200, Title = "Prayer B", PrayerCardId = 2 },
            new() { Id = 300, Title = "Prayer C", PrayerCardId = 1 }
        }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object>
        {
            { "scope", "box" },
            { "boxId", "5" }
        });

        // Wait for async load to complete
        await Task.Delay(200);

        // Should include only prayers from cards in box 5 (+ completion sentinel)
        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Equal(2, realEntries.Count);
        Assert.All(realEntries, e => Assert.Equal("Family", e.CardTitle));
    }

    // ── scope=selection filtering ─────────────────────────────────────

    [Fact]
    public async Task ApplyQueryAttributes_SelectionScope_FiltersToSelectedIds()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Family" },
            new() { Id = 2, Title = "Work" }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Prayer A", PrayerCardId = 1 },
            new() { Id = 200, Title = "Prayer B", PrayerCardId = 2 },
            new() { Id = 300, Title = "Prayer C", PrayerCardId = 1 }
        }.AsReadOnly());

        // Selection service hands over IDs 100 and 300; 200 and a stale 999 must be excluded.
        _selectionService.Consume().Returns(new List<int> { 100, 300, 999 }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "selection" } });

        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Equal(2, realEntries.Count);
        Assert.All(realEntries, e => Assert.Equal("Family", e.CardTitle));
    }

    [Fact]
    public async Task ApplyQueryAttributes_SelectionScope_ConsumesSelectionServiceOnce()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { new() { Id = 1, Title = "A" } }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "P", PrayerCardId = 1 }
        }.AsReadOnly());
        _selectionService.Consume().Returns(new List<int> { 100 }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "selection" } });

        await Task.Delay(200);

        _selectionService.Received(1).Consume();
    }

    [Fact]
    public async Task ApplyQueryAttributes_SelectionScope_EmptySelection_NoEntries()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { new() { Id = 1, Title = "A" } }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "P", PrayerCardId = 1 }
        }.AsReadOnly());
        _selectionService.Consume().Returns(new List<int>().AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "selection" } });

        await Task.Delay(200);

        Assert.DoesNotContain(sut.Entries, e => !e.IsSentinel);
        Assert.True(sut.HasCompleted);
    }

    // ── Ordering (BUG-61) ────────────────────────────────────────────

    [Fact]
    public async Task LoadEntries_OrdersByCardTitleThenPrayerTitle()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Zebra" },
            new() { Id = 2, Title = "Alpha" }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "C Prayer", PrayerCardId = 1 },
            new() { Id = 200, Title = "A Prayer", PrayerCardId = 2 },
            new() { Id = 300, Title = "B Prayer", PrayerCardId = 2 },
            new() { Id = 400, Title = "A Prayer", PrayerCardId = 1 }
        }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });

        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Equal(4, realEntries.Count);
        // Alpha card first, sorted by prayer title
        Assert.Equal("Alpha", realEntries[0].CardTitle);
        Assert.Equal("A Prayer", realEntries[0].PrayerTitle);
        Assert.Equal("Alpha", realEntries[1].CardTitle);
        Assert.Equal("B Prayer", realEntries[1].PrayerTitle);
        // Then Zebra card
        Assert.Equal("Zebra", realEntries[2].CardTitle);
        Assert.Equal("A Prayer", realEntries[2].PrayerTitle);
        Assert.Equal("Zebra", realEntries[3].CardTitle);
        Assert.Equal("C Prayer", realEntries[3].PrayerTitle);
    }

    // ── scope=all excludes archived cards ───────────────────────────────

    [Fact]
    public async Task ScopeAll_ExcludesPrayersFromArchivedCards()
    {
        const int archivedBoxId = 999; // matches fixture default

        // Card 1 is normal, Card 2 is archived
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Active Card", BoxId = 0 },
            new() { Id = 2, Title = "Archived Card", BoxId = archivedBoxId }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Normal Prayer", PrayerCardId = 1 },
            new() { Id = 200, Title = "Archived Prayer", PrayerCardId = 2 }
        }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });

        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Single(realEntries);
        Assert.Equal(100, realEntries[0].PrayerId);
        Assert.Equal("Normal Prayer", realEntries[0].PrayerTitle);
    }

    [Fact]
    public async Task ScopeAll_PreservesOrphanPrayersWhoseCardIsMissing()
    {
        // scope=all uses denylist semantics: only prayers whose card is in the
        // Archived box are excluded. A card-less/orphan active prayer (its card
        // absent from GetCardsAsync) must still appear — the cardLookup "Unknown"
        // fallback handles its missing title.
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Active Card", BoxId = 0 }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Normal Prayer", PrayerCardId = 1 },
            new() { Id = 300, Title = "Orphan Prayer", PrayerCardId = 42 } // card 42 not in GetCardsAsync
        }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });

        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Equal(2, realEntries.Count);
        Assert.Contains(realEntries, e => e.PrayerId == 300 && e.CardTitle == "Unknown");
    }

    [Fact]
    public async Task ScopeBox_DoesNotExcludeArchivedCardsByArchiveFilter()
    {
        // scope=box should remain unmodified — only scope=all gets the archive filter
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Card In Box 5", BoxId = 5 }
        }.AsReadOnly());

        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Prayer", PrayerCardId = 1 }
        }.AsReadOnly());

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object>
        {
            { "scope", "box" },
            { "boxId", "5" }
        });

        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        // scope=box filter is unchanged — card in box 5 is included
        Assert.Single(realEntries);
        Assert.Equal(100, realEntries[0].PrayerId);
    }

    // ── CycleIntervalCommand (E2E-cull conversion) ────────────────────
    // Replaces the interval half of the deleted PrayerTime_AutoMode_CyclesInterval
    // E2E (PrayerTimeTests.cs). CycleInterval (PrayerTimeViewModel.cs:450) steps
    // SelectedIntervalSeconds through the {30,60,120} options (:30) and wraps,
    // writing each choice back to ISettings.AutoModeIntervalSeconds via the
    // setter (:41).

    [Fact]
    public void CycleIntervalCommand_CyclesThroughOptions()
    {
        var sut = CreateSut();
        Assert.Equal(30, sut.SelectedIntervalSeconds);

        sut.CycleIntervalCommand.Execute(null);
        Assert.Equal(60, sut.SelectedIntervalSeconds);

        sut.CycleIntervalCommand.Execute(null);
        Assert.Equal(120, sut.SelectedIntervalSeconds);

        sut.CycleIntervalCommand.Execute(null);
        Assert.Equal(30, sut.SelectedIntervalSeconds); // wraps back to the first option

        // Each distinct interval is persisted to settings exactly once.
        _settings.Received(1).AutoModeIntervalSeconds = 60;
        _settings.Received(1).AutoModeIntervalSeconds = 120;
        _settings.Received(1).AutoModeIntervalSeconds = 30;
    }

    // ── ToggleAutoModeCommand (issue #218 — IDispatcher injection) ────
    // StartAutoMode used to reach Application.Current!.Dispatcher directly, which
    // NullReferenceExceptions off-device. Injecting IDispatcher makes the auto-mode
    // toggle and its timer unit-testable.

    [Fact]
    public void ToggleAutoModeCommand_TogglesIsAutoMode_AndButtonText()
    {
        var sut = CreateSut();
        Assert.False(sut.IsAutoMode);
        Assert.Equal("Auto ▷", sut.AutoModeButtonText);

        sut.ToggleAutoModeCommand.Execute(null);
        Assert.True(sut.IsAutoMode);
        Assert.Equal("⏸ Auto", sut.AutoModeButtonText);

        sut.ToggleAutoModeCommand.Execute(null);
        Assert.False(sut.IsAutoMode);
        Assert.Equal("Auto ▷", sut.AutoModeButtonText);
    }

    [Fact]
    public void ToggleAutoModeCommand_StartingAutoMode_CreatesAndStartsInjectedTimer()
    {
        var sut = CreateSut();

        sut.ToggleAutoModeCommand.Execute(null);

        _dispatcher.Received(1).CreateTimer();
        _autoTimer.Received(1).Start();
    }

    // ── Issue #255: Prayer Time stricter re-auth gate ─────────────────────
    // Unlike the search/share gates (which skip auth when IsSessionUnlocked is already
    // true), Prayer Time is an exposed landscape view that always re-authenticates when
    // the session includes any effectively-protected prayer — even if the confidential
    // session is already unlocked. On deny, the protected prayers are excluded and the
    // session still runs with the remaining unprotected prayers. On success, they're
    // included.

    private static PrayerCard ProtectedCard(int id, string title) =>
        new() { Id = id, Title = title, ProtectionMode = CardProtectionMode.LockedVisible };

    [Fact]
    public async Task LoadEntries_ContainsProtectedPrayer_ReAuthenticatesEvenWhenAlreadyUnlocked()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            ProtectedCard(1, "Secret")
        }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Prayer A", PrayerCardId = 1 }
        }.AsReadOnly());
        _confidentialAccessService.IsSessionUnlocked.Returns(true); // already unlocked
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });
        await Task.Delay(200);

        // Stricter gate: re-auths regardless of already-unlocked session.
        await _confidentialAccessService.Received(1).AuthenticateAsync(Arg.Any<string>());
        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Single(realEntries);
        Assert.Equal(100, realEntries[0].PrayerId);
    }

    [Fact]
    public async Task LoadEntries_ContainsProtectedPrayer_AuthDenied_ExcludesProtectedButRunsWithRest()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            ProtectedCard(1, "Secret"),
            new() { Id = 2, Title = "Open", ProtectionMode = CardProtectionMode.None }
        }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Protected Prayer", PrayerCardId = 1 },
            new() { Id = 200, Title = "Open Prayer", PrayerCardId = 2 }
        }.AsReadOnly());
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>()).Returns(false);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });
        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Single(realEntries);
        Assert.Equal(200, realEntries[0].PrayerId);
        Assert.False(sut.HasCompleted); // session still runs with the remaining prayer
    }

    [Fact]
    public async Task LoadEntries_ContainsProtectedPrayer_AuthSucceeds_IncludesProtected()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            ProtectedCard(1, "Secret"),
            new() { Id = 2, Title = "Open", ProtectionMode = CardProtectionMode.None }
        }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Protected Prayer", PrayerCardId = 1 },
            new() { Id = 200, Title = "Open Prayer", PrayerCardId = 2 }
        }.AsReadOnly());
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });
        await Task.Delay(200);

        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Equal(2, realEntries.Count);
        Assert.Contains(realEntries, e => e.PrayerId == 100);
        Assert.Contains(realEntries, e => e.PrayerId == 200);
    }

    [Fact]
    public async Task LoadEntries_NoProtectedPrayers_NeverPromptsAuth()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Open", ProtectionMode = CardProtectionMode.None }
        }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Open Prayer", PrayerCardId = 1 }
        }.AsReadOnly());
        _confidentialAccessService.IsSessionUnlocked.Returns(false);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });
        await Task.Delay(200);

        await _confidentialAccessService.DidNotReceive().AuthenticateAsync(Arg.Any<string>());
        var realEntries = sut.Entries.Where(e => !e.IsSentinel).ToList();
        Assert.Single(realEntries);
    }

    [Fact]
    public async Task LoadEntries_ProtectedPrayerViaBoxCascade_ReAuthenticates()
    {
        var box = new CardBox { Id = 7, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible };
        _boxService.GetBoxesAsync().Returns(new List<CardBox> { box }.AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Cascaded", ProtectionMode = CardProtectionMode.None, BoxId = 7 }
        }.AsReadOnly());
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, Title = "Prayer", PrayerCardId = 1 }
        }.AsReadOnly());
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { { "scope", "all" } });
        await Task.Delay(200);

        await _confidentialAccessService.Received(1).AuthenticateAsync(Arg.Any<string>());
        Assert.Single(sut.Entries.Where(e => !e.IsSentinel));
    }
}
