using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp;
using PrayerApp.Helpers;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class PrayerCardsViewModelTests
{
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly IOnboardingService _onboardingService = Substitute.For<IOnboardingService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly ITagService _tagService = Substitute.For<ITagService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    // Issue #253: gates hidden-exclusion + locked-masking. Fake mirrors the #251
    // seam-faking approach — defaults to locked (IsSessionUnlocked=false) so
    // existing tests that don't care about confidentiality see the pre-#253
    // "everything visible/unmasked" behavior only when they explicitly opt in.
    private readonly IConfidentialAccessService _confidentialAccessService = Substitute.For<IConfidentialAccessService>();
    // Fresh WeakReferenceMessenger per fixture so messenger-driven tests can fire
    // real Send/Register without leaking across tests via the .Default singleton.
    private readonly IMessenger _messenger = new WeakReferenceMessenger();

    public PrayerCardsViewModelTests()
    {
        // Default mock: GetActivePrayerCountByCardAsync returns 0 for any card
        // (needed because PrayerCardViewModel constructor fires LoadActivePrayerCountAsync)
        _prayerService.GetActivePrayerCountByCardAsync(Arg.Any<int>()).Returns(0);

        // Default: no boxes (sections will be empty)
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());

        // Reset the Prayer Active-Record static per test so a stub set by one test
        // (e.g. the #82 in-place-insert test) can't leak into the next. Matches the
        // constructor-init pattern in PrayerCardViewModelTests / PrayerRequestDetailViewModelTests.
        Prayer.SetDBService(Substitute.For<IDBService>());
    }

    private PrayerCardsViewModel CreateSut() =>
        new(_cardService, _prayerService, _onboardingService, _navigationService,
            _accessibilityService, _tagService, _settings, _boxService,
            _confidentialAccessService, _messenger);

    /// <summary>Sets up standard system boxes so sections can be built.</summary>
    private void SetupSystemBoxes()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        };
        _boxService.GetBoxesAsync().Returns(boxes.AsReadOnly());
        _settings.ArchivedFolderId.Returns(20);
    }

    /// <summary>Helper to get all visible cards across all sections.</summary>
    private List<PrayerCardViewModel> GetAllVisibleCards(PrayerCardsViewModel sut) =>
        sut.BoxSections.SelectMany(s => s).ToList();

    /// <summary>
    /// Helper for issue #253 hidden-exclusion tests. Reads section *membership*
    /// (backing list, via CardCount + the section's expanded contents when expanded)
    /// rather than only-when-expanded rendered contents — GetAllVisibleCards returns
    /// empty for a collapsed section (the BoxSectionViewModel clears its observable
    /// collection while collapsed), which is irrelevant to whether a card was excluded
    /// from the box's card set at RebuildSections time. Expand every section first so
    /// the collection reflects the backing list, mirroring EnsureAllSectionsExpanded's
    /// UITest pattern of forcing expansion before reading contents.
    /// </summary>
    private List<PrayerCardViewModel> GetAllSectionMemberCards(PrayerCardsViewModel sut)
    {
        foreach (var section in sut.BoxSections)
            section.IsExpanded = true;
        return sut.BoxSections.SelectMany(s => s).ToList();
    }

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_InitializesEmptyCollections()
    {
        var sut = CreateSut();

        Assert.Empty(sut.AllPrayerCards);
        Assert.Empty(sut.BoxSections);
        Assert.False(sut.IsLoading);
        Assert.Equal(string.Empty, sut.SearchText);
    }

    // ── IsLoading announces ───────────────────────────────────────────

    [Fact]
    public void IsLoading_True_AnnouncesLoading()
    {
        var sut = CreateSut();

        sut.IsLoading = true;

        _accessibilityService.Received(1).Announce("Loading");
    }

    [Fact]
    public void IsLoading_False_AnnouncesContentLoaded()
    {
        var sut = CreateSut();
        sut.IsLoading = true;
        _accessibilityService.ClearReceivedCalls();

        sut.IsLoading = false;

        _accessibilityService.Received(1).Announce("Content loaded");
    }

    // ── Slice 6g — IsAwaitingSavedCard + IsBusyOverall ─────────────────
    // The LoadingOverlay binds to IsBusyOverall (= IsLoading || IsAwaitingSavedCard)
    // so the post-save flow can keep the overlay visible across the SyncAsync→
    // ConsumePendingSavedAsync→ScrollTo window — even though SyncAsync's own
    // finally block flips IsLoading off mid-way. View code-behind sets
    // IsAwaitingSavedCard at OnAppearing entry (when a save is pending) and
    // clears it after ScrollTo completes.

    [Fact]
    public void IsAwaitingSavedCard_DefaultState_False()
    {
        var sut = CreateSut();

        Assert.False(sut.IsAwaitingSavedCard);
    }

    [Fact]
    public void IsBusyOverall_DefaultState_False()
    {
        var sut = CreateSut();

        Assert.False(sut.IsBusyOverall);
    }

    [Fact]
    public void IsBusyOverall_OnlyIsLoadingTrue_ReturnsTrue()
    {
        var sut = CreateSut();

        sut.IsLoading = true;

        Assert.True(sut.IsBusyOverall);
    }

    [Fact]
    public void IsBusyOverall_OnlyIsAwaitingSavedCardTrue_ReturnsTrue()
    {
        var sut = CreateSut();

        sut.IsAwaitingSavedCard = true;

        Assert.True(sut.IsBusyOverall);
    }

    [Fact]
    public void IsAwaitingSavedCard_Toggle_RaisesIsBusyOverallPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.IsAwaitingSavedCard = true;

        Assert.Contains(nameof(PrayerCardsViewModel.IsBusyOverall), raised);
    }

    [Fact]
    public void IsLoading_Toggle_RaisesIsBusyOverallPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.IsLoading = true;

        Assert.Contains(nameof(PrayerCardsViewModel.IsBusyOverall), raised);
    }

    // ── NewCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task NewCommand_AdvancesOnboarding_AndNavigates()
    {
        var sut = CreateSut();

        await ((IAsyncRelayCommand)sut.NewCommand).ExecuteAsync(null);

        _onboardingService.Received(1).Advance();
        await _navigationService.Received(1).GoToAsync(Routes.PrayerCardPage);
    }

    // ── SyncAsync (Slice 3 part 2) ────────────────────────────────────

    [Fact]
    public async Task SyncAsync_FetchesAndPopulatesAllPrayerCards()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Equal(2, sut.AllPrayerCards.Count);
        Assert.Contains(sut.AllPrayerCards, c => c.Id == 1);
        Assert.Contains(sut.AllPrayerCards, c => c.Id == 2);
    }

    [Fact]
    public async Task SyncAsync_DoesNotInvalidateServiceCaches()
    {
        // Slice 2 contract: services auto-invalidate on mutation. VMs no longer
        // defensively invalidate before reading. (This inverts the old
        // LoadAsync_InvalidatesCardAndBoxCaches and RefreshAsync_InvalidatesAllCaches
        // tests below — those will be removed in step 13.)
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        _cardService.DidNotReceive().InvalidateCache();
        _prayerService.DidNotReceive().InvalidateCache();
        _boxService.DidNotReceive().InvalidateCache();
        _tagService.DidNotReceive().InvalidateCache();
    }

    [Fact]
    public async Task SyncAsync_PreservesMultiSelectMode()
    {
        // Messenger-driven sync (e.g. a tag rename elsewhere) must NOT drop the
        // user's current selection. The old LoadAsync/RefreshAsync called
        // ExitMultiSelectMode at entry — that's wrong for the new entry path.
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "A", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "B", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);
        Assert.True(sut.IsMultiSelectMode);
        Assert.Equal(1, sut.SelectedCardCount);

        await sut.SyncAsync();

        Assert.True(sut.IsMultiSelectMode);
        Assert.Equal(1, sut.SelectedCardCount);
    }

    [Fact]
    public async Task SyncAsync_PreservesTagFilterChipSelection()
    {
        // Selecting a tag chip then triggering SyncAsync (via messenger or OnAppearing)
        // must keep the chip selected — the diff path reuses existing TagFilterChipViewModel
        // instances, preserving IsSelected.
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Card", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());

        var tag = new PrayerTag { Id = 100, Name = "Healing" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        Assert.Single(sut.AvailableTags);
        sut.AvailableTags[0].ToggleCommand.Execute(null);
        Assert.True(sut.AvailableTags[0].IsSelected);

        await sut.SyncAsync();

        Assert.Single(sut.AvailableTags);
        Assert.True(sut.AvailableTags[0].IsSelected);
    }

    [Fact]
    public async Task SyncAsync_PreservesSearchText()
    {
        // SearchText is user-typed UI state; SyncAsync must not touch it.
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.SearchText = "needle";

        await sut.SyncAsync();

        Assert.Equal("needle", sut.SearchText);
    }

    // ── Messenger-driven sync (Slice 3 part 2) ────────────────────────

    [Fact]
    public async Task PrayerCardChangedMessage_TriggersSyncAsync()
    {
        SetupDefaultSyncMocks();
        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        _messenger.Send(new PrayerCardChangedMessage(1, ChangeKind.Created));
        await Task.Yield();
        await Task.Yield();

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task PrayerChangedMessage_TriggersSyncAsync()
    {
        SetupDefaultSyncMocks();
        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        _messenger.Send(new PrayerChangedMessage(10, 1, ChangeKind.Updated));
        await Task.Yield();
        await Task.Yield();

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task TagChangedMessage_TriggersSyncAsync()
    {
        SetupDefaultSyncMocks();
        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        _messenger.Send(new TagChangedMessage(5, ChangeKind.Updated));
        await Task.Yield();
        await Task.Yield();

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task CardBoxChangedMessage_TriggersSyncAsync()
    {
        SetupDefaultSyncMocks();
        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        _messenger.Send(new CardBoxChangedMessage(3, ChangeKind.Created));
        await Task.Yield();
        await Task.Yield();

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task BulkChangedMessage_TriggersSyncAsync()
    {
        // Bulk consolidation contract from Slice 2: backup restore / deep-link import
        // fire a single BulkChangedMessage rather than N granular per-entity messages.
        SetupDefaultSyncMocks();
        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        _messenger.Send(new BulkChangedMessage());
        await Task.Yield();
        await Task.Yield();

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_AfterSyncAddedCard_DoesNotRebuildSectionsAgain()
    {
        // After SyncAsync adds the saved card via its diff loop, ConsumePendingSavedAsync
        // should NOT rebuild sections again — SyncAsync's tail RebuildSections already
        // covered the new card. Folding the redundant rebuild eliminates the second
        // CollectionView adapter reset that contributed to the post-Save lag.
        // RebuildSections replaces the BoxSections instance, so an unchanged reference
        // proves it wasn't called inside Consume.
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        // ApplyQueryAttributes runs while AllPrayerCards is empty (snapshots wasAlreadyInList=false)
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        // Simulate SyncAsync's diff loop having added the new card
        var newCard = new PrayerCard { Id = 42, Title = "Just Created", BoxId = 0 };
        var newVm = new PrayerCardViewModel(newCard, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
        { Parent = sut };
        sut.AllPrayerCards.Add(newVm);

        var sectionsBeforeConsume = sut.BoxSections;

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
        Assert.True(result.IsHighlighted);
        Assert.Same(sectionsBeforeConsume, sut.BoxSections);
    }

    // ── Slice 6a — single-flight + coalesce-pending SyncAsync ─────────

    [Fact]
    public async Task SyncAsync_BurstOfThreeConcurrent_CoalescesToTwoFetches()
    {
        // Save→Cards burst: PrayerCardChangedMessage fires SyncAsync on the messenger
        // path, PageSync.OnAppearingAsync awaits its own SyncAsync, and additional
        // sibling broadcasts pile on. Without coalescing, every trigger calls
        // RebuildSections at the tail — each one replaces BoxSections and forces
        // Android RecyclerView to re-inflate every visible cell (~330 ms each).
        // The 2026-04-26 PERF log captured TWO cascades back-to-back per Save.
        // Single-flight + coalesce-pending collapses any burst of N triggers into
        // exactly 2 fetches: one in-flight + one coalesced follow-up that guarantees
        // freshness for triggers that arrived AFTER the in-flight read.
        SetupSystemBoxes();
        var gate = new TaskCompletionSource<IReadOnlyList<PrayerCard>>();
        _cardService.GetCardsAsync().Returns(gate.Task);
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();

        var t1 = sut.SyncAsync();
        var t2 = sut.SyncAsync();
        var t3 = sut.SyncAsync();

        gate.SetResult(new List<PrayerCard>().AsReadOnly());
        await Task.WhenAll(t1, t2, t3);

        await _cardService.Received(2).GetCardsAsync();
    }

    // ── Slice 6b — structural-equality guard on RebuildSections ──────

    [Fact]
    public async Task SyncAsync_TwiceWithIdenticalData_PreservesBoxSectionsReference()
    {
        // 6b: when SyncAsync runs twice with identical data (typical coalesced
        // follow-up after a Save), the second RebuildSections must not replace
        // BoxSections — replacement raises PropertyChanged and triggers an Android
        // RecyclerView re-inflate cascade. Reference identity is the contract.
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        var sectionsAfterFirst = sut.BoxSections;

        await sut.SyncAsync();

        Assert.Same(sectionsAfterFirst, sut.BoxSections);
    }

    private void SetupDefaultSyncMocks()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
    }

    // ── Section building ──────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_BuildsSections_UnboxedAndSystemAndArchived()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 }; // Unboxed
        var card2 = new PrayerCard { Id = 2, Title = "Quick Add", IsSystem = true, BoxId = 10 }; // System
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        // Should have: Unboxed (1 card), System (1 card), Archived (0 cards but always shown)
        Assert.Equal(3, sut.BoxSections.Count);
        Assert.Equal(BoxStrings.Unorganized, sut.BoxSections[0].Name);
        Assert.Equal(1, sut.BoxSections[0].CardCount);
        Assert.Equal("System", sut.BoxSections[1].Name);
        Assert.Equal(1, sut.BoxSections[1].CardCount);
        Assert.Equal("Archived", sut.BoxSections[2].Name);
        Assert.Equal(0, sut.BoxSections[2].CardCount);
    }

    [Fact]
    public async Task SyncAsync_UserBoxesSortedByName()
    {
        SetupSystemBoxes();
        var boxes = new List<CardBox>
        {
            new() { Id = 5, Name = "Zulu", SortOrder = 0 },
            new() { Id = 6, Name = "Alpha", SortOrder = 0 },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        };
        _boxService.GetBoxesAsync().Returns(boxes.AsReadOnly());

        var card1 = new PrayerCard { Id = 1, Title = "Card A", BoxId = 5 }; // Zulu
        var card2 = new PrayerCard { Id = 2, Title = "Card B", BoxId = 6 }; // Alpha
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        // User boxes sorted A→Z: Alpha before Zulu
        Assert.Equal("Alpha", sut.BoxSections[0].Name);
        Assert.Equal("Zulu", sut.BoxSections[1].Name);
    }

    [Fact]
    public async Task SyncAsync_CardsWithinSectionSorted_FavoritesFirst()
    {
        SetupSystemBoxes();
        _settings.ExpandedSectionIds.Returns("0"); // Expand unboxed so items are visible
        var card1 = new PrayerCard { Id = 1, Title = "Beta", BoxId = 0, IsFavorite = false };
        var card2 = new PrayerCard { Id = 2, Title = "Alpha", BoxId = 0, IsFavorite = true };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        var unboxed = sut.BoxSections.First(s => s.BoxId == 0);
        Assert.Equal("Alpha", unboxed[0].Title); // Favorited → first
        Assert.Equal("Beta", unboxed[1].Title);
    }

    // ── Tag filter tests ────────────────────────────────────────────────

    [Fact]
    public async Task TagFilter_ShowsCardsWithUnansweredTaggedPrayers()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "Card One", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1 }.AsReadOnly());

        var tag = new PrayerTag { Id = 100, Name = "Healing" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());

        var prayer = new Prayer { Id = 10, PrayerCardId = 1, Title = "Pray for healing", IsAnswered = false };
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer> { prayer }.AsReadOnly());

        var junction = new PrayerCardTag { Id = 1, PrayerCardId = 1, PrayerTagId = 100, PrayerRequestId = 10 };
        SetupDbMocks(new List<PrayerCardTag> { junction });

        var sut = CreateSut();
        await sut.SyncAsync();

        // Act: select the tag chip
        Assert.Single(sut.AvailableTags);
        sut.AvailableTags[0].ToggleCommand.Execute(null);

        // Assert: card appears in visible results
        var visible = GetAllVisibleCards(sut);
        Assert.Single(visible);
        Assert.Equal("Card One", visible[0].Title);
    }

    [Fact]
    public async Task TagFilter_ExcludesCardsWhereAllTaggedPrayersAnswered()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "Card One", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1 }.AsReadOnly());

        var tag = new PrayerTag { Id = 100, Name = "Healing" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());

        var prayer = new Prayer { Id = 10, PrayerCardId = 1, Title = "Answered prayer", IsAnswered = true };
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer> { prayer }.AsReadOnly());

        var junction = new PrayerCardTag { Id = 1, PrayerCardId = 1, PrayerTagId = 100, PrayerRequestId = 10 };
        SetupDbMocks(new List<PrayerCardTag> { junction });

        var sut = CreateSut();
        await sut.SyncAsync();

        // Act: select the tag chip
        sut.AvailableTags[0].ToggleCommand.Execute(null);

        // Assert: card is excluded (no unanswered tagged prayers)
        var visible = GetAllVisibleCards(sut);
        Assert.Empty(visible);
    }

    [Fact]
    public async Task TagFilter_MultipleTagsUseOrLogic()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "Card One", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Card Two", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var tag1 = new PrayerTag { Id = 100, Name = "Healing" };
        var tag2 = new PrayerTag { Id = 200, Name = "Guidance" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag1, tag2 }.AsReadOnly());

        var prayer1 = new Prayer { Id = 10, PrayerCardId = 1, Title = "Prayer 1", IsAnswered = false };
        var prayer2 = new Prayer { Id = 20, PrayerCardId = 2, Title = "Prayer 2", IsAnswered = false };
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer> { prayer1, prayer2 }.AsReadOnly());

        var junc1 = new PrayerCardTag { Id = 1, PrayerCardId = 1, PrayerTagId = 100, PrayerRequestId = 10 };
        var junc2 = new PrayerCardTag { Id = 2, PrayerCardId = 2, PrayerTagId = 200, PrayerRequestId = 20 };
        SetupDbMocks(new List<PrayerCardTag> { junc1, junc2 });

        var sut = CreateSut();
        await sut.SyncAsync();

        // Act: select both tag chips
        sut.AvailableTags[0].ToggleCommand.Execute(null);
        sut.AvailableTags[1].ToggleCommand.Execute(null);

        // Assert: both cards appear (OR logic)
        var visible = GetAllVisibleCards(sut);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public async Task TagFilter_ClearedShowsAllCards()
    {
        SetupSystemBoxes();
        _settings.ExpandedSectionIds.Returns("0"); // Section starts expanded so restore returns to expanded
        var card1 = new PrayerCard { Id = 1, Title = "Card One", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Card Two", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var tag = new PrayerTag { Id = 100, Name = "Healing" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());

        var prayer1 = new Prayer { Id = 10, PrayerCardId = 1, Title = "Prayer 1", IsAnswered = false };
        var prayer2 = new Prayer { Id = 20, PrayerCardId = 2, Title = "Prayer 2", IsAnswered = false };
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer> { prayer1, prayer2 }.AsReadOnly());

        var junction = new PrayerCardTag { Id = 1, PrayerCardId = 1, PrayerTagId = 100, PrayerRequestId = 10 };
        SetupDbMocks(new List<PrayerCardTag> { junction });

        var sut = CreateSut();
        await sut.SyncAsync();

        // Act: select then deselect tag
        sut.AvailableTags[0].ToggleCommand.Execute(null);
        var filteredVisible = GetAllVisibleCards(sut);
        Assert.Single(filteredVisible); // filtered to 1 card

        sut.AvailableTags[0].ToggleCommand.Execute(null); // deselect

        // Assert: all cards reappear
        var allVisible = GetAllVisibleCards(sut);
        Assert.Equal(2, allVisible.Count);
    }

    [Fact]
    public async Task SyncAsync_PopulatesAvailableTags()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());

        var tag1 = new PrayerTag { Id = 1, Name = "Healing" };
        var tag2 = new PrayerTag { Id = 2, Name = "Guidance" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag1, tag2 }.AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Equal(2, sut.AvailableTags.Count);
        Assert.Equal("Healing", sut.AvailableTags[0].Tag.Name);
        Assert.Equal("Guidance", sut.AvailableTags[1].Tag.Name);
    }

    [Fact]
    public async Task HasTags_TrueWhenTagsExist()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());

        var tag = new PrayerTag { Id = 1, Name = "Healing" };
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.True(sut.HasTags);
    }

    // ── Archived section always visible ───────────────────────────────

    [Fact]
    public async Task SyncAsync_ArchivedSectionAlwaysPresent_EvenWhenEmpty()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        // Only Archived should be visible (no cards in any section, but Archived is always shown)
        Assert.Single(sut.BoxSections);
        Assert.Equal("Archived", sut.BoxSections[0].Name);
        Assert.Equal(0, sut.BoxSections[0].CardCount);
    }

    // ── Auto-collapse-others on IsExpanded ──────────────────────────

    [Fact]
    public async Task ExpandingOneCard_CollapsesOtherExpandedCards()
    {
        // Only one card expanded at a time — the View's margin-animation contract depends on this.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        var vmAlpha = sut.AllPrayerCards.First(c => c.Id == 1);
        var vmBeta  = sut.AllPrayerCards.First(c => c.Id == 2);

        // Expand Alpha first — Beta is collapsed, no auto-collapse needed.
        sut.ExpandedCardId = vmAlpha.Id;
        Assert.True(vmAlpha.IsExpanded);
        Assert.False(vmBeta.IsExpanded);

        // Expand Beta — Alpha must auto-collapse (singleton invariant).
        sut.ExpandedCardId = vmBeta.Id;
        Assert.True(vmBeta.IsExpanded);
        Assert.False(vmAlpha.IsExpanded);
    }

    [Fact]
    public async Task CollapsingExpandedCard_DoesNotAffectOtherCards()
    {
        // Only the IsExpanded=true transition fires the auto-collapse loop.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        var vmAlpha = sut.AllPrayerCards.First(c => c.Id == 1);
        var vmBeta  = sut.AllPrayerCards.First(c => c.Id == 2);

        sut.ExpandedCardId = vmAlpha.Id;
        sut.ExpandedCardId = null;

        // Beta was never touched — must remain collapsed.
        Assert.False(vmBeta.IsExpanded);
        Assert.False(vmAlpha.IsExpanded);
    }

    [Fact]
    public async Task ExpandedCardId_Set_RaisesIsExpandedPropertyChanged_OnPrevAndNextCards()
    {
        // The View binds each cell's inlined expanded subtree to
        // IsVisible="{Binding IsExpanded}" (issue #42 shape (i)). If ExpandedCardId
        // mutation stops raising IsExpanded-changed on the prev+next cards,
        // tap-to-expand silently does nothing on device. (Regression caught in
        // Commit 2 slim.)
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var vmAlpha = sut.AllPrayerCards.First(c => c.Id == 1);
        var vmBeta  = sut.AllPrayerCards.First(c => c.Id == 2);

        var alphaIsExpandedRaised = 0;
        var betaIsExpandedRaised  = 0;
        vmAlpha.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PrayerCardViewModel.IsExpanded)) alphaIsExpandedRaised++; };
        vmBeta.PropertyChanged  += (_, e) => { if (e.PropertyName == nameof(PrayerCardViewModel.IsExpanded)) betaIsExpandedRaised++; };

        // First expand: only Beta flips state.
        sut.ExpandedCardId = vmBeta.Id;
        Assert.Equal(0, alphaIsExpandedRaised);
        Assert.Equal(1, betaIsExpandedRaised);

        // Switch expand A↔B: both cards flip.
        sut.ExpandedCardId = vmAlpha.Id;
        Assert.Equal(1, alphaIsExpandedRaised);
        Assert.Equal(2, betaIsExpandedRaised);

        // Collapse: only Alpha flips back.
        sut.ExpandedCardId = null;
        Assert.Equal(2, alphaIsExpandedRaised);
        Assert.Equal(2, betaIsExpandedRaised);
    }

    // ── Multi-select ─────────────────────────────────────────────────

    [Fact]
    public async Task EnterMultiSelectMode_SetsIsMultiSelectModeAndSelectsCard()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        Assert.True(sut.IsMultiSelectMode);
        Assert.True(sut.AllPrayerCards[0].IsMultiSelected);
        Assert.Equal(1, sut.SelectedCardCount);
    }

    [Fact]
    public async Task ToggleCardSelection_TogglesIsMultiSelected()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        // Wait past the 300ms long-press suppression window
        await Task.Delay(350);

        sut.ToggleCardSelection(sut.AllPrayerCards[0]); // deselect
        Assert.False(sut.AllPrayerCards[0].IsMultiSelected);
        Assert.Equal(0, sut.SelectedCardCount);
    }

    [Fact]
    public async Task CancelMultiSelectCommand_DeselectsAllAndExitsMode()
    {
        SetupSystemBoxes();
        var card1 = new PrayerCard { Id = 1, Title = "A", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "B", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);
        sut.ToggleCardSelection(sut.AllPrayerCards[1]); // select second too

        sut.CancelMultiSelectCommand.Execute(null);

        Assert.False(sut.IsMultiSelectMode);
        Assert.False(sut.AllPrayerCards[0].IsMultiSelected);
        Assert.False(sut.AllPrayerCards[1].IsMultiSelected);
    }

    [Fact]
    public async Task MoveSelectedCommand_Cancel_DoesNotMove()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        }.AsReadOnly());
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Cancel");

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        await ((IAsyncRelayCommand)sut.MoveSelectedCommand).ExecuteAsync(null);

        await _cardService.DidNotReceive().AssignBoxAsync(Arg.Any<PrayerCard>(), Arg.Any<int>());
        Assert.True(sut.IsMultiSelectMode); // stays in mode
    }

    [Fact]
    public async Task MoveSelectedCommand_SelectsBox_MovesCardsAndExitsMode()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        }.AsReadOnly());
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Family");

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        await ((IAsyncRelayCommand)sut.MoveSelectedCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(Arg.Any<PrayerCard>(), 5);
        Assert.False(sut.IsMultiSelectMode); // exits mode after move
    }

    [Fact]
    public void SelectedCountText_FormatsCorrectly()
    {
        var sut = CreateSut();
        Assert.Equal("None selected", sut.SelectedCountText);
    }

    // ── BUG-55: HasNoSections ──────────────────────────────────────────────

    [Fact]
    public async Task HasNoSections_TrueWhenNoSections()
    {
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        var sut = CreateSut();

        await sut.SyncAsync();

        Assert.True(sut.HasNoSections);
    }

    [Fact]
    public async Task HasNoSections_FalseWhenSectionsExist()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Test", BoxId = 0 }
        }.AsReadOnly());
        var sut = CreateSut();

        await sut.SyncAsync();

        Assert.False(sut.HasNoSections);
    }

    // ── BUG-56: Empty user boxes always shown ────────────────────────

    [Fact]
    public async Task SyncAsync_EmptyUserBox_AppearsAsSection()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 5, Name = "Empty Box", IsSystem = false, SortOrder = 0 },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        };
        _boxService.GetBoxesAsync().Returns(boxes.AsReadOnly());
        _settings.ArchivedFolderId.Returns(20);
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        var sut = CreateSut();

        await sut.SyncAsync();

        var emptySection = sut.BoxSections.FirstOrDefault(s => s.BoxId == 5);
        Assert.NotNull(emptySection);
        Assert.Equal("Empty Box", emptySection.Name);
        Assert.Equal(0, emptySection.CardCount);
    }

    // ── BUG-59: Multi-select propagation ─────────────────────────────

    [Fact]
    public async Task IsMultiSelectMode_PropagatesToSections()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Card", BoxId = 0 }
        }.AsReadOnly());
        var sut = CreateSut();
        await sut.SyncAsync();

        sut.IsMultiSelectMode = true;

        Assert.All(sut.BoxSections, s => Assert.True(s.IsMultiSelectMode));
    }

    [Fact]
    public async Task IsMultiSelectMode_False_ClearsOnSections()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Card", BoxId = 0 }
        }.AsReadOnly());
        var sut = CreateSut();
        await sut.SyncAsync();
        sut.IsMultiSelectMode = true;

        sut.IsMultiSelectMode = false;

        Assert.All(sut.BoxSections, s => Assert.False(s.IsMultiSelectMode));
    }

    // ── Collections Banner ─────────────────────────────────────────────

    [Fact]
    public void CollectionsBanner_NotDismissed_ShowsBanner()
    {
        _settings.CollectionsBannerDismissed.Returns(false);

        var sut = CreateSut();

        Assert.True(sut.ShowCollectionsBanner);
    }

    [Fact]
    public void CollectionsBanner_AlreadyDismissed_HidesBanner()
    {
        _settings.CollectionsBannerDismissed.Returns(true);

        var sut = CreateSut();

        Assert.False(sut.ShowCollectionsBanner);
    }

    [Fact]
    public void DismissCollectionsBannerCommand_SetsFlagAndHidesBanner()
    {
        _settings.CollectionsBannerDismissed.Returns(false);

        var sut = CreateSut();
        Assert.True(sut.ShowCollectionsBanner);

        sut.DismissCollectionsBannerCommand.Execute(null);

        Assert.False(sut.ShowCollectionsBanner);
        _settings.Received(1).CollectionsBannerDismissed = true;
    }

    // ── UX-31: Section expansion persistence ─────────────────────────────

    [Fact]
    public async Task SyncAsync_NoSavedState_AllSectionsCollapsed()
    {
        SetupSystemBoxes();
        // ExpandedSectionIds defaults to null/empty → all collapsed
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.All(sut.BoxSections, s => Assert.False(s.IsExpanded));
    }

    [Fact]
    public async Task SyncAsync_SavedExpandedIds_RestoresExpansionState()
    {
        SetupSystemBoxes();
        _settings.ExpandedSectionIds.Returns("0,20"); // Unboxed and Archived expanded
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        var unboxed = sut.BoxSections.First(s => s.BoxId == 0);
        var archived = sut.BoxSections.First(s => s.BoxId == 20);
        Assert.True(unboxed.IsExpanded);
        Assert.True(archived.IsExpanded);
    }

    [Fact]
    public async Task SaveSectionExpansionState_PersistsExpandedIds()
    {
        SetupSystemBoxes();
        _settings.ExpandedSectionIds.Returns("0"); // Start with Unboxed expanded
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        sut.SaveSectionExpansionState();

        // Should save "0" (the Unboxed section that was expanded)
        _settings.Received().ExpandedSectionIds = Arg.Is<string>(s => s.Contains("0"));
    }

    [Fact]
    public async Task SaveSectionExpansionState_AllCollapsed_SavesEmpty()
    {
        SetupSystemBoxes();
        // No saved state → all collapsed
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        sut.SaveSectionExpansionState();

        _settings.Received().ExpandedSectionIds = "";
    }

    // ── PendingSavedIdentifier (replaces HighlightCardRequested event) ──
    // Background: a tester crashed reproducibly on a Galaxy Ultra when saving a new card.
    // Root cause: ApplyQueryAttributes("saved") fired a C# event whose handler called
    // CollectionView.ScrollTo on a MauiRecyclerView whose adapter snapshot hadn't
    // committed the BoxSections swap from RebuildSections. Fix: stage the identifier
    // here, consume it in OnAppearing on the lifecycle channel.

    [Fact]
    public void ApplyQueryAttributes_Saved_NewIdentifier_SetsPendingSavedIdentifier()
    {
        var sut = CreateSut();
        var query = new Dictionary<string, object> { { "saved", "42" } };

        ((IQueryAttributable)sut).ApplyQueryAttributes(query);

        Assert.Equal("42", sut.PendingSavedIdentifier);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_WhenIdentifierNull_NoOps_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.ConsumePendingSavedAsync();

        Assert.Null(result);
        Assert.Empty(sut.AllPrayerCards);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCard_LoadsFromDbAddsToAllPrayerCards()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var pcDb = SetupCardLoadMock(42, new PrayerCard { Id = 42, Title = "Just Created", BoxId = 0 });

        var sut = CreateSut();
        await sut.SyncAsync();
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Contains(sut.AllPrayerCards, c => c.Id == 42);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCard_MarksHighlightedAndExpanded()
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        SetupCardLoadMock(42, new PrayerCard { Id = 42, Title = "Just Created", BoxId = 0 });

        var sut = CreateSut();
        await sut.SyncAsync();
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(result.IsHighlighted);
        Assert.True(result.IsExpanded);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_ClearsPendingSavedIdentifier()
    {
        var sut = CreateSut();
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });
        Assert.Equal("42", sut.PendingSavedIdentifier);

        // Even if no card is mocked (PrayerCard.LoadAsync returns null), the identifier
        // must clear so a stale signal doesn't fire on the next OnAppearing.
        SetupCardLoadMock(42, null);
        await sut.ConsumePendingSavedAsync();

        Assert.Null(sut.PendingSavedIdentifier);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_MatchedExisting_PreservesStateReturnsNull()
    {
        // Edit case: card was already in AllPrayerCards when ApplyQueryAttributes
        // ran. Don't highlight/expand. Don't scroll (no race window).
        SetupSystemBoxes();
        var existingCard = new PrayerCard { Id = 7, Title = "Existing", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existingCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();
        var matched = sut.AllPrayerCards.Single(c => c.Id == 7);
        var wasExpanded = matched.IsExpanded;
        var wasHighlighted = matched.IsHighlighted;

        // ApplyQueryAttributes runs *while* card 7 is in the list — snapshots
        // wasAlreadyInList = true, so this is treated as an edit.
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "7" } });
        var result = await sut.ConsumePendingSavedAsync();

        Assert.Null(result);
        Assert.Equal(wasExpanded, matched.IsExpanded);
        Assert.Equal(wasHighlighted, matched.IsHighlighted);
        Assert.Single(sut.AllPrayerCards, c => c.Id == 7);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_MatchedButAddedByRefresh_HighlightsAndExpands()
    {
        // Real-world Galaxy Ultra flow: PrayerCardPage.SaveAsync calls GoToAsync("..?saved=42").
        // Shell fires ApplyQueryAttributes BEFORE OnAppearing. At that moment, card 42 is NOT
        // in AllPrayerCards (it was just inserted into the DB). Snapshot captures
        // wasAlreadyInList=false. THEN OnAppearing's SyncAsync runs, GetCardsAsync now
        // returns card 42, and the diff loop adds it to AllPrayerCards. THEN
        // ConsumePendingSavedAsync runs: matched != null but wasAlreadyInList=false →
        // highlight + return for scroll.
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync(); // 0 cards loaded — card 42 not yet present

        // Stage the saved-id while AllPrayerCards is empty (snapshots wasAlreadyInList=false).
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        // Simulate SyncAsync's diff loop adding card 42 between ApplyQueryAttributes
        // and ConsumePendingSavedAsync (the order Shell + OnAppearing produce).
        var newCard = new PrayerCard { Id = 42, Title = "Just Created", BoxId = 0 };
        var newVm = new PrayerCardViewModel(newCard, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
        { Parent = sut };
        sut.AllPrayerCards.Add(newVm);

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.True(result.IsHighlighted);
        Assert.True(result.IsExpanded);
        Assert.Same(newVm, result); // returned the existing VM, not a duplicate
        Assert.Single(sut.AllPrayerCards, c => c.Id == 42);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewViaRefresh_ExpandsMatchedAndCollapsesSiblings()
    {
        // PERF-1: the new card gets IsExpanded + IsHighlighted, and the cascade loop
        // still collapses other expanded cards (UX requirement: only one expanded at
        // a time). The suppression flag inside ConsumePendingSavedAsync only short-
        // circuits the *per-collapse* RebuildSections call; one explicit RebuildSections
        // at the end of the orchestrated mutation covers all section work in one pass.
        SetupSystemBoxes();
        var existing1 = new PrayerCard { Id = 1, Title = "A", BoxId = 0 };
        var existing2 = new PrayerCard { Id = 2, Title = "B", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing1, existing2 }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        // User has card 1 expanded before the save begins.
        var card1Vm = sut.AllPrayerCards.First(c => c.Id == 1);
        sut.ExpandedCardId = card1Vm.Id;

        // Save flow: a new card 3 has been inserted. Diff loop adds it to AllPrayerCards.
        var newCard = new PrayerCard { Id = 3, Title = "C", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing1, existing2, newCard }.AsReadOnly());
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "3" } });
        await sut.SyncAsync();
        var result = await sut.ConsumePendingSavedAsync();

        // The new card got highlighted + expanded.
        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
        Assert.True(result.IsHighlighted);
        // Card 1 was collapsed by the cascade loop — UX requirement preserved.
        // The fix only suppresses the per-collapse RebuildSections call, not the
        // collapse itself. One explicit RebuildSections at the end of
        // ConsumePendingSavedAsync covers all the section-level work in one pass.
        Assert.False(card1Vm.IsExpanded);
    }

    [Fact]
    public void HighlightCardRequested_EventIsRemoved()
    {
        // Reflection guard: this VM→View C# event was the crash path. If a future
        // change re-introduces it, this test fails as a tripwire.
        var evt = typeof(PrayerCardsViewModel).GetEvent("HighlightCardRequested");
        Assert.Null(evt);
    }

    // ── BUG-76 — Newly-saved card hidden inside collapsed parent section ─────
    // 1.3.0 iOS UAT 2026-04-26: after Save, the new card itself is correctly
    // expanded, but if its parent BoxSection is collapsed the card is invisible
    // inside the collapsed group. ConsumePendingSavedAsync must auto-expand the
    // parent section (matched by BoxId — Contains() returns false on a collapsed
    // section because ApplyExpansionState clears the observable) and persist.

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCardInCollapsedSection_ExpandsParentSectionAndPersists()
    {
        SetupDefaultSyncMocks();
        // No saved expansion state → every section starts collapsed.
        var existing = new PrayerCard { Id = 1, Title = "Existing", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        var unboxed = sut.BoxSections.First(s => s.BoxId == 0);
        Assert.False(unboxed.IsExpanded);

        // Save flow: new card 42 lands in the (still-collapsed) Unboxed section.
        var newCard = new PrayerCard { Id = 42, Title = "Brand New", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing, newCard }.AsReadOnly());
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });
        await sut.SyncAsync();

        _settings.ClearReceivedCalls();
        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(unboxed.IsExpanded,
            "Parent section must auto-expand so the newly-saved card is actually visible");
        _settings.Received().ExpandedSectionIds = Arg.Is<string>(s => s.Contains("0"));
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCardInExpandedSection_DoesNotRewriteSettings()
    {
        // No-op guarantee: if the parent section is already expanded, don't
        // re-persist (no thrash on the settings store, no spurious telemetry).
        SetupDefaultSyncMocks();
        _settings.ExpandedSectionIds.Returns("0"); // Unboxed pre-expanded
        var existing = new PrayerCard { Id = 1, Title = "Existing", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        var unboxed = sut.BoxSections.First(s => s.BoxId == 0);
        Assert.True(unboxed.IsExpanded);

        var newCard = new PrayerCard { Id = 42, Title = "Brand New", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { existing, newCard }.AsReadOnly());
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });
        await sut.SyncAsync();

        _settings.ClearReceivedCalls();
        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(unboxed.IsExpanded);
        _settings.DidNotReceiveWithAnyArgs().ExpandedSectionIds = Arg.Any<string>();
    }

    // ── BUG-78 — Imported card opens with empty prayer list ──────────────
    // ConfirmImportViewModel.SaveAsync persists the card AND its prayers, then
    // navigates ?saved=cardId. ConsumePendingSavedAsync flipped IsExpanded=true
    // without loading prayers (the only loader was ToggleExpandedAsync, gated
    // on !IsExpanded), so an imported card revealed an empty body. Pre-fix
    // tests asserted IsExpanded but never Prayers.Count.

    private PrayerRequestDetailViewModel BuildStubPrayerRowVm(Prayer p)
        => new(p, _prayerService, _tagService, _cardService, _onboardingService,
            _notificationService, _navigationService, _accessibilityService, _settings,
            _boxService, _confidentialAccessService);

    private void SetupBug78EmptySync(int cardId, params Prayer[] prayers)
    {
        SetupSystemBoxes();
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _prayerService.GetPrayersByCardAsync(cardId).Returns(prayers.ToList().AsReadOnly());
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCardViaSync_LoadsPrayers()
    {
        SetupBug78EmptySync(42,
            new Prayer { Id = 100, PrayerCardId = 42, Title = "Pray for Mom", IsImported = true },
            new Prayer { Id = 101, PrayerCardId = 42, Title = "Pray for Dad", IsImported = true },
            new Prayer { Id = 102, PrayerCardId = 42, Title = "Pray for Sis", IsImported = true });

        var sut = CreateSut();
        await sut.SyncAsync();
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        // Simulate SyncAsync's diff loop adding card 42 between ApplyQueryAttributes
        // and ConsumePendingSavedAsync (the new-via-sync branch).
        var newCard = new PrayerCard { Id = 42, Title = "Imported May 2", BoxId = 0, IsImported = true };
        var newVm = new PrayerCardViewModel(newCard, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
        {
            PrayerRowFactory = BuildStubPrayerRowVm,
            Parent = sut
        };
        sut.AllPrayerCards.Add(newVm);

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
        Assert.Equal(3, result.Prayers.Count);
    }

    // ── #82: "Save & Add Another" — Created broadcast patches the expanded card ──
    // Repro: from an expanded card, Save & Add Another saves a prayer but does NOT
    // navigate, so ApplyQueryAttributes never runs. The PrayerChangedMessage(Created)
    // broadcast is the only signal the cards page gets — it must insert the new row
    // in place. Before the fix the Created broadcast hit SyncCoreAsync's skipReload
    // guard (changeKind != null) and the new prayer never entered the expanded card's
    // Prayers collection until a later full sync (the second nav-away/back).
    [Fact]
    public async Task PrayerChangedMessage_Created_OnExpandedCard_InsertsPrayerInPlace()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        // Card 1 starts with one prayer; the just-created Save+ prayer (id 200) is not in it.
        var existing = new Prayer { Id = 100, PrayerCardId = 1, Title = "Existing" };
        _prayerService.GetPrayersByCardAsync(1).Returns(new List<Prayer> { existing }.AsReadOnly());
        // Active-Record load for the newly-created prayer (AddOrUpdatePrayerAsync → Prayer.LoadAsync).
        var created = new Prayer { Id = 200, PrayerCardId = 1, Title = "Brand New" };
        var prayerDb = Substitute.For<IDBService>();
        Prayer.SetDBService(prayerDb);
        prayerDb.GetByIdAsync<Prayer>(200).Returns(created);

        // Inject the stub row factory so the in-place insert avoids the DI-bound ctor.
        var sut = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, _tagService, _settings, _boxService,
            _confidentialAccessService, _messenger,
            cardVmFactory: pc => new PrayerCardViewModel(pc, _cardService, _prayerService,
                _onboardingService, _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
            {
                PrayerRowFactory = BuildStubPrayerRowVm
            });
        await sut.SyncAsync();
        var cardVm = sut.AllPrayerCards.Single(c => c.Id == 1);
        sut.ExpandedCardId = 1;            // user has the card expanded behind the detail page
        await cardVm.LoadPrayersAsync();   // expanded card's rows are realized
        Assert.Single(cardVm.Prayers);

        // Act — Save & Add Another fires this without navigating.
        _messenger.Send(new PrayerChangedMessage(200, 1, ChangeKind.Created));
        // The handler is fire-and-forget with a deeper chain than the file's
        // 2x-Task.Yield convention (AddOrUpdatePrayerAsync → Prayer.LoadAsync, then
        // SyncAsync), so poll the observable condition with a bounded yield loop.
        for (int i = 0; i < 100 && cardVm.Prayers.All(p => p.Id != 200); i++)
            await Task.Yield();

        // Assert — the new prayer is now live in the expanded card (was dropped before the fix).
        Assert.Contains(cardVm.Prayers, p => p.Id == 200);
        Assert.Equal(2, cardVm.Prayers.Count);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_NewCardViaDb_LoadsPrayers()
    {
        SetupBug78EmptySync(42,
            new Prayer { Id = 100, PrayerCardId = 42, Title = "Pray for Mom", IsImported = true },
            new Prayer { Id = 101, PrayerCardId = 42, Title = "Pray for Dad", IsImported = true });
        SetupCardLoadMock(42, new PrayerCard { Id = 42, Title = "Imported May 2", BoxId = 0, IsImported = true });

        // Override the factory at the SUT-construction site so the new-via-db
        // branch (which builds its own VM via CreateCardViewModel) gets the stub.
        var sut = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, _tagService, _settings, _boxService,
            _confidentialAccessService, _messenger,
            cardVmFactory: pc => new PrayerCardViewModel(pc, _cardService, _prayerService,
                _onboardingService, _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
            {
                PrayerRowFactory = BuildStubPrayerRowVm
            });
        await sut.SyncAsync();
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { "saved", "42" } });

        // No SyncAsync between Apply and Consume → falls through to the
        // new-via-db branch (load card from DB, add to AllPrayerCards).
        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
        Assert.Equal(2, result.Prayers.Count);
    }

    // ── Move-prayer (ApplyQueryAttributes prayerSaved+oldCardId) ─────────

    [Fact]
    public async Task MovePrayer_OldCardId_BranchInApplyQueryAttributes_RemovesFromOldCard()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var vmAlpha = sut.AllPrayerCards.First(c => c.Id == 1);
        var vmBeta  = sut.AllPrayerCards.First(c => c.Id == 2);

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        // Synchronous effects: target expanded, source not expanded, sync suppressed.
        Assert.True(vmBeta.IsExpanded);
        Assert.False(vmAlpha.IsExpanded);
        Assert.True(sut.SuppressNextOnAppearingSync);
    }

    [Fact]
    public async Task MovePrayer_AlwaysAutoExpandsTarget_CollapsesPriorExpandedCard()
    {
        // The move flow saves a prayer to a different card. The user should land
        // on the target card expanded so they see the saved prayer in context.
        // Whatever was previously expanded (source A or unrelated X) collapses —
        // the structural ExpandedCardId design only signals prev+next, so there's
        // no R-1 cascade across unrelated cards.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        var card3 = new PrayerCard { Id = 3, Title = "Xray",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2, card3 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var vmBeta = sut.AllPrayerCards.First(c => c.Id == 2);
        var vmX = sut.AllPrayerCards.First(c => c.Id == 3);
        sut.ExpandedCardId = vmX.Id;

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        Assert.Equal(2, sut.ExpandedCardId);
        Assert.True(vmBeta.IsExpanded);
        Assert.False(vmX.IsExpanded);
    }

    [Fact]
    public async Task RealizeStormCanary_PrayerChangedMessageOnExpandedCard_DoesNotReloadPrayers()
    {
        // BUG-79/80 guard: SyncAsync(ChangeKind.Updated) skips ReloadPrayers when
        // changeKind != null. Regression canary — must stay green after every refactor.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var vmAlpha = sut.AllPrayerCards.First(c => c.Id == 1);
        sut.ExpandedCardId = vmAlpha.Id;

        _prayerService.ClearReceivedCalls();
        await sut.SyncAsync(ChangeKind.Updated);

        _prayerService.DidNotReceive().GetPrayersByCardAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task EditPrayer_NotMove_AutoExpandsParentEvenWhenAnotherCardExpanded()
    {
        // R-1 guard semantic: edits (no oldCardId) ALWAYS auto-expand the parent.
        // Only MOVE flows defer to the user's current selection.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.ExpandedCardId = 1; // user has Alpha expanded

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" }
                // no OldCardId — this is an edit, not a move
            });

        Assert.Equal(2, sut.ExpandedCardId);
    }

    [Fact]
    public async Task MovePrayer_ToCardNotInList_SuppressNextSyncNotSet()
    {
        // When parentCardId is not in AllPrayerCards (card created in the same save
        // flow, not synced yet), matched == null — branch exits without expanding or
        // suppressing. The next OnAppearing SyncAsync adds the card.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "99" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        Assert.False(sut.SuppressNextOnAppearingSync);
        Assert.DoesNotContain(sut.AllPrayerCards, c => c.Id == 99);
    }

    [Fact]
    public async Task MovePrayer_WhileSearchActive_MoveStillAppliedToAllPrayerCards()
    {
        // ApplyQueryAttributes operates on AllPrayerCards, not the filtered BoxSections.
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.SearchText = "Alpha"; // filters BoxSections to Alpha only
        var vmBeta = sut.AllPrayerCards.First(c => c.Id == 2);

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        Assert.True(vmBeta.IsExpanded); // AllPrayerCards updated regardless of filter
        Assert.True(sut.SuppressNextOnAppearingSync);
    }

    [Fact]
    public async Task MovePrayer_SetsPendingSavedIdentifierOnTarget()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        Assert.Equal("2", sut.PendingSavedIdentifier);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_MoveTarget_ReturnsTargetCard()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_MoveTarget_DoesNotHighlightTarget()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta",  BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        await sut.ConsumePendingSavedAsync();

        var vmBeta = sut.AllPrayerCards.First(c => c.Id == 2);
        Assert.False(vmBeta.IsHighlighted);
    }

    // ── Move-prayer to a SYSTEM card target (issue #42) ───────────────────

    [Fact]
    public async Task MovePrayer_ToSystemCardTarget_SetsExpandedCardIdToTarget()
    {
        // #42: moving a prayer to a SYSTEM card target (e.g. "Shared with me",
        // in the System box, sort ~900 / off-screen) must auto-expand that card
        // exactly like a user-card target — the post-move auto-expand path is
        // IsSystem-blind. The original #42 defect lived in the View's lazy
        // ExpandedSubtreeHost realization (an off-screen system cell never
        // realized, so its inlined prayer never showed); the VM has always set
        // ExpandedCardId regardless of IsSystem. This test is the regression lock
        // that the VM stays IsSystem-blind after the shape-(i) realization
        // retirement — a future refactor must not re-introduce an IsSystem guard
        // on the move target. Mirrors MovePrayer_AlwaysAutoExpandsTarget_
        // CollapsesPriorExpandedCard, with the target in the System box.
        SetupDefaultSyncMocks();
        var source = new PrayerCard { Id = 1, Title = "Family", BoxId = 0 };
        var systemTarget = new PrayerCard
        {
            Id = 2,
            Title = PrayerCard.TitleSharedWithMe,
            IsSystem = true,
            BoxId = 10 // System box (see SetupSystemBoxes)
        };
        _cardService.GetCardsAsync().Returns(
            new List<PrayerCard> { source, systemTarget }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var vmSource = sut.AllPrayerCards.First(c => c.Id == 1);
        var vmSystem = sut.AllPrayerCards.First(c => c.Id == 2);

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" }, // the system card
                { Routes.QueryKeys.OldCardId, "1" }     // moved out of the user card
            });

        Assert.Equal(2, sut.ExpandedCardId);   // system target auto-expanded
        Assert.True(vmSystem.IsExpanded);      // its inlined subtree will be visible
        Assert.False(vmSource.IsExpanded);     // source collapses (prayer left it)
        Assert.True(sut.SuppressNextOnAppearingSync);
        Assert.Equal("2", sut.PendingSavedIdentifier); // staged for scroll-to
    }

    [Fact]
    public async Task ConsumePendingSavedAsync_MoveToSystemTarget_ExpandsSectionAndReturnsCard()
    {
        // #42 continuation: ConsumePendingSavedAsync must return the system target
        // (so the View scrolls to it) AND expand its (collapsed-by-default) System
        // section, so the moved prayer is revealed in context rather than hidden
        // inside a collapsed group. EnsureSectionExpandedFor is also IsSystem-blind.
        SetupDefaultSyncMocks();
        var source = new PrayerCard { Id = 1, Title = "Family", BoxId = 0 };
        var systemTarget = new PrayerCard
        {
            Id = 2,
            Title = PrayerCard.TitleSharedWithMe,
            IsSystem = true,
            BoxId = 10 // System box
        };
        _cardService.GetCardsAsync().Returns(
            new List<PrayerCard> { source, systemTarget }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        // System section is collapsed by default (no persisted expanded ids).
        var systemSection = sut.BoxSections.First(s => s.BoxId == 10);
        Assert.False(systemSection.IsExpanded);

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object>
            {
                { Routes.QueryKeys.PrayerSaved, "10" },
                { Routes.QueryKeys.ParentCardId, "2" },
                { Routes.QueryKeys.OldCardId, "1" }
            });

        var result = await sut.ConsumePendingSavedAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Id);            // View scrolls to the system target
        Assert.True(systemSection.IsExpanded); // group opened so the card is reachable
    }

    // ── ApplyQueryAttributes ImportedToExisting ───────────────────────────

    [Fact]
    public async Task ApplyQueryAttributes_ImportedToExisting_ExpandsTargetCard()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { Routes.QueryKeys.ImportedToExisting, "2" } });

        Assert.Equal(2, sut.ExpandedCardId);
    }

    [Fact]
    public async Task ApplyQueryAttributes_ImportedToExisting_StagesMoveTargetScroll()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { Routes.QueryKeys.ImportedToExisting, "2" } });

        var result = await sut.ConsumePendingSavedAsync();
        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
    }

    [Fact]
    public async Task ApplyQueryAttributes_ImportedToExisting_UnknownCardId_IsNoOp()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        var expandedBefore = sut.ExpandedCardId;

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { Routes.QueryKeys.ImportedToExisting, "99" } });

        Assert.Equal(expandedBefore, sut.ExpandedCardId);
        Assert.Null(sut.PendingSavedIdentifier);
    }

    [Fact]
    public async Task ApplyQueryAttributes_ImportedToExisting_SuppressesSync()
    {
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { Routes.QueryKeys.ImportedToExisting, "2" } });

        Assert.True(sut.SuppressNextOnAppearingSync);
    }

    [Fact]
    public async Task ImportedToExisting_SuppressesOnAppearingSync_ButBulkChangedMessageStillRefreshesAsync()
    {
        // UAT-107 item 3 contract lock: SuppressNextOnAppearingSync = true on the
        // ImportedToExisting path is only safe because BulkChangedMessage (raised by
        // ConfirmImportViewModel.SaveAsync at PrayerApp/ViewModels/ConfirmImportViewModel.cs:442)
        // drives the actual list refresh. This test locks the composition: after
        // ApplyQueryAttributes flips the suppression flag, a BulkChangedMessage MUST
        // still trigger SyncAsync (observed via _cardService.GetCardsAsync()).
        // Pairs with ApplyQueryAttributes_ImportedToExisting_SuppressesSync (suppression
        // half) and BulkChangedMessage_TriggersSyncAsync (bulk-message → sync half).
        SetupDefaultSyncMocks();
        var card1 = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 0 };
        var card2 = new PrayerCard { Id = 2, Title = "Beta", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card1, card2 }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();
        _cardService.ClearReceivedCalls();

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { { Routes.QueryKeys.ImportedToExisting, "2" } });

        // Pre-condition: suppression set (asserted in the sibling test, repeated here
        // so the composition this test guards is self-evident from the body).
        Assert.True(sut.SuppressNextOnAppearingSync);

        _messenger.Send(new BulkChangedMessage());
        await Task.Yield();
        await Task.Yield();

        // The suppression flag intentionally only gates OnAppearing's sync, not the
        // messenger-driven sync. If a future change ever short-circuited
        // BulkChangedMessage on this flag, the import-to-existing list would silently
        // stop refreshing — exactly the bug this regression net catches.
        await _cardService.Received().GetCardsAsync();
    }

    // ── MoveSelectedAsync — Archive option ────────────────────────────────

    [Fact]
    public async Task MoveSelectedCommand_Archive_AssignsArchivedFolderIdToNonSystemCards()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        }.AsReadOnly());
        _settings.ArchivedFolderId.Returns(20);
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Archive");
        _navigationService
            .DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        await ((IAsyncRelayCommand)sut.MoveSelectedCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(Arg.Any<PrayerCard>(), 20);
    }

    [Fact]
    public async Task MoveSelectedCommand_Archive_Cancelled_DoesNotAssignBox()
    {
        SetupSystemBoxes();
        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 0 };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        }.AsReadOnly());
        _settings.ArchivedFolderId.Returns(20);
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Archive");
        _navigationService
            .DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);   // user cancels the "Archive Cards?" dialog

        var sut = CreateSut();
        await sut.SyncAsync();
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);

        await ((IAsyncRelayCommand)sut.MoveSelectedCommand).ExecuteAsync(null);

        await _cardService.DidNotReceive().AssignBoxAsync(Arg.Any<PrayerCard>(), Arg.Any<int>());
    }

    [Fact]
    public async Task MoveSelectedCommand_Archive_SkipsSystemCards()
    {
        SetupSystemBoxes();
        var userCard = new PrayerCard { Id = 1, Title = "User", BoxId = 0 };
        var systemCard = new PrayerCard { Id = 2, Title = "Quick Add", BoxId = 0, IsSystem = true };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { userCard, systemCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem, SortOrder = 900 },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived, SortOrder = 999 }
        }.AsReadOnly());
        _settings.ArchivedFolderId.Returns(20);
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Archive");
        _navigationService
            .DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var sut = CreateSut();
        await sut.SyncAsync();

        // Select both cards
        sut.EnterMultiSelectMode(sut.AllPrayerCards[0]);
        foreach (var c in sut.AllPrayerCards) c.IsMultiSelected = true;

        await ((IAsyncRelayCommand)sut.MoveSelectedCommand).ExecuteAsync(null);

        // Only the non-system card should be assigned
        await _cardService.Received(1).AssignBoxAsync(Arg.Is<PrayerCard>(c => !c.IsSystem), 20);
        await _cardService.DidNotReceive().AssignBoxAsync(Arg.Is<PrayerCard>(c => c.IsSystem), Arg.Any<int>());
    }

    // ── Issue #253 — hidden-exclusion + "Show confidential" reveal ────

    [Fact]
    public async Task SyncAsync_EffectivelyHiddenCard_SessionLocked_ExcludedFromSections()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var visibleCard = new PrayerCard { Id = 1, Title = "Visible", BoxId = 0 };
        var hiddenCard = new PrayerCard { Id = 2, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { visibleCard, hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        var visible = GetAllSectionMemberCards(sut);
        Assert.Contains(visible, c => c.Id == 1);
        Assert.DoesNotContain(visible, c => c.Id == 2);
    }

    [Fact]
    public async Task SyncAsync_EffectivelyHiddenCard_SessionUnlocked_IncludedInSections()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var hiddenCard = new PrayerCard { Id = 2, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        var visible = GetAllSectionMemberCards(sut);
        Assert.Contains(visible, c => c.Id == 2);
    }

    [Fact]
    public async Task SyncAsync_BoxCascadesHidden_SessionLocked_ExcludesDeferringCard()
    {
        // Card defers (ProtectionMode.None) but the box cascades Hidden via
        // ProtectAllCards — GetEffectiveProtectionMode(card, box) must be consulted
        // at section-build time, not just the card's own ProtectionMode.
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var cascadeBox = new CardBox { Id = 7, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.Hidden };
        _boxService.GetBoxesAsync().Returns(new List<CardBox> { cascadeBox }.AsReadOnly());
        var card = new PrayerCard { Id = 1, Title = "Cascaded Secret", BoxId = 7, ProtectionMode = CardProtectionMode.None };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.DoesNotContain(GetAllSectionMemberCards(sut), c => c.Id == 1);
    }

    [Fact]
    public void IsSessionUnlocked_ReflectsConfidentialAccessService()
    {
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var sut = CreateSut();

        Assert.True(sut.IsSessionUnlocked);
    }

    [Fact]
    public async Task ShowConfidentialCommand_AuthenticateSucceeds_RebuildsSectionsToRevealHidden()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var hiddenCard = new PrayerCard { Id = 2, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();
        Assert.DoesNotContain(GetAllSectionMemberCards(sut), c => c.Id == 2);

        // Authenticate succeeds — flip the fake's reported unlock state (mirrors how
        // the real ConfidentialAccessService.IsSessionUnlocked flips after a
        // successful AuthenticateAsync) and invoke the command.
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>())
            .Returns(callInfo =>
            {
                _confidentialAccessService.IsSessionUnlocked.Returns(true);
                return Task.FromResult(true);
            });

        await ((IAsyncRelayCommand)sut.ShowConfidentialCommand).ExecuteAsync(null);

        Assert.Contains(GetAllSectionMemberCards(sut), c => c.Id == 2);
    }

    [Fact]
    public async Task ShowConfidentialCommand_AuthenticateFails_DoesNotReveal()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var hiddenCard = new PrayerCard { Id = 2, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();
        _confidentialAccessService.AuthenticateAsync(Arg.Any<string>()).Returns(false);

        await ((IAsyncRelayCommand)sut.ShowConfidentialCommand).ExecuteAsync(null);

        Assert.DoesNotContain(GetAllSectionMemberCards(sut), c => c.Id == 2);
    }

    // ── Issue #254: SessionRelockedMessage — re-lock visible on resume ──────
    // App.xaml.cs publishes SessionRelockedMessage right after RelockSession() on
    // Window.Deactivated. These tests fire the same message over the fixture's
    // shared _messenger (mirrors how the other EntityChangedMessage handlers are
    // exercised elsewhere in this file) and assert the VM re-derives its lock-gated
    // projections without a DB re-sync.

    [Fact]
    public async Task SessionRelockedMessage_HiddenCardWasRevealed_ReExcludedAfterRelock()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var hiddenCard = new PrayerCard { Id = 2, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();
        Assert.Contains(GetAllSectionMemberCards(sut), c => c.Id == 2);

        // Simulate App.xaml.cs's Window.Deactivated handler: RelockSession() flips the
        // shared singleton's reported state, then the message is broadcast.
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _messenger.Send(new PrayerApp.Messages.SessionRelockedMessage());

        Assert.DoesNotContain(GetAllSectionMemberCards(sut), c => c.Id == 2);
    }

    [Fact]
    public void SessionRelockedMessage_LockedVisibleCardWasUnmasked_ReMaskedAfterRelock()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var card = new PrayerCard { Id = 3, Title = "Secret Card", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        var sut = CreateSut();
        var vm = new PrayerCardViewModel(card, _cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, _boxService, _settings, _confidentialAccessService)
        { Parent = sut };
        sut.AllPrayerCards.Add(vm);
        Assert.False(vm.IsLockedVisible);
        Assert.Equal("Secret Card", vm.DisplayTitle);

        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _messenger.Send(new PrayerApp.Messages.SessionRelockedMessage());

        Assert.True(vm.IsLockedVisible);
        Assert.Equal("Protected", vm.DisplayTitle);
    }

    [Fact]
    public async Task SessionRelockedMessage_ExpandedProtectedCard_CollapsesAndHidesRealContent()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var card = new PrayerCard { Id = 4, Title = "Secret Card", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        _prayerService.GetPrayersByCardAsync(4).Returns(new List<Prayer>());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();
        sut.ExpandedCardId = 4; // already unlocked — expand directly, no gate involved here
        var cardVm = sut.AllPrayerCards.Single(c => c.Id == 4);
        Assert.True(cardVm.IsExpanded);

        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _messenger.Send(new PrayerApp.Messages.SessionRelockedMessage());

        Assert.Null(sut.ExpandedCardId);
        Assert.False(cardVm.IsExpanded);
    }

    [Fact]
    public async Task SessionRelockedMessage_ExpandedUnprotectedCard_StaysExpanded()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var card = new PrayerCard { Id = 5, Title = "Open Card", BoxId = 0, ProtectionMode = CardProtectionMode.None };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { card }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        _prayerService.GetPrayersByCardAsync(5).Returns(new List<Prayer>());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();
        sut.ExpandedCardId = 5;
        var cardVm = sut.AllPrayerCards.Single(c => c.Id == 5);

        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        _messenger.Send(new PrayerApp.Messages.SessionRelockedMessage());

        // Unprotected card — relock has nothing to hide, so it stays expanded.
        Assert.Equal(5, sut.ExpandedCardId);
        Assert.True(cardVm.IsExpanded);
    }

    // ── Issue #255 — search exclusion of effectively-protected cards while locked ──
    // Unlike #253's RebuildSections (which only excludes Hidden — LockedVisible cards
    // stay in the list, masked), a search/tag filter must drop ALL effectively-protected
    // cards (Hidden AND LockedVisible) while locked — a masked "Protected" row surfacing
    // as a search hit would defeat the point of typing a search term.

    [Fact]
    public async Task ApplyFilter_SearchMatchesLockedVisibleCard_SessionLocked_ExcludedFromResults()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var protectedCard = new PrayerCard { Id = 3, Title = "Secret Diagnosis", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        var openCard = new PrayerCard { Id = 4, Title = "Secret Recipe", BoxId = 0, ProtectionMode = CardProtectionMode.None };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { protectedCard, openCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();

        sut.SearchText = "Secret";

        var visible = GetAllSectionMemberCards(sut);
        Assert.DoesNotContain(visible, c => c.Id == 3);
        Assert.Contains(visible, c => c.Id == 4);
    }

    [Fact]
    public async Task ApplyFilter_SearchMatchesLockedVisibleCard_SessionUnlocked_IncludedInResults()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var protectedCard = new PrayerCard { Id = 3, Title = "Secret Diagnosis", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { protectedCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();

        sut.SearchText = "Secret";

        Assert.Contains(GetAllSectionMemberCards(sut), c => c.Id == 3);
    }

    [Fact]
    public async Task ApplyFilter_NoActiveFilter_SessionLocked_LockedVisibleCardStaysInDefaultList()
    {
        // Regression guard: clearing a search (or deselecting the last tag) while locked
        // must NOT drop LockedVisible cards from the default (no-filter) list — that list
        // must match RebuildSections' base exclusion (Hidden only), per #253. Only an
        // ACTIVE filter applies the stricter "drop LockedVisible too" rule.
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var lockedVisibleCard = new PrayerCard { Id = 3, Title = "Secret Diagnosis", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        var hiddenCard = new PrayerCard { Id = 4, Title = "Hidden Secret", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { lockedVisibleCard, hiddenCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag>().AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();

        // Set a search (excludes LockedVisible too), then clear it back to empty.
        sut.SearchText = "Secret";
        sut.SearchText = string.Empty;

        var visible = GetAllSectionMemberCards(sut);
        Assert.Contains(visible, c => c.Id == 3); // LockedVisible stays, masked
        Assert.DoesNotContain(visible, c => c.Id == 4); // Hidden stays excluded
    }

    [Fact]
    public async Task ApplyFilter_TagToggledOffAfterOn_SessionLocked_LockedVisibleCardStaysInDefaultList()
    {
        SetupSystemBoxes();
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var lockedVisibleCard = new PrayerCard { Id = 3, Title = "Secret Diagnosis", BoxId = 0, ProtectionMode = CardProtectionMode.LockedVisible };
        var tag = new PrayerTag { Id = 100, Name = "Health" };
        _cardService.GetCardsAsync().Returns(new List<PrayerCard> { lockedVisibleCard }.AsReadOnly());
        _tagService.GetTagsAsync().Returns(new List<PrayerTag> { tag }.AsReadOnly());
        _prayerService.GetAllPrayersAsync().Returns(new List<Prayer>().AsReadOnly());
        SetupDbMocks(new List<PrayerCardTag>());
        var sut = CreateSut();
        await sut.SyncAsync();

        var chip = sut.AvailableTags.Single(c => c.Tag.Id == 100);
        chip.ToggleCommand.Execute(null); // select — active filter now, LockedVisible excluded
        chip.ToggleCommand.Execute(null); // deselect — back to no-filter

        var visible = GetAllSectionMemberCards(sut);
        Assert.Contains(visible, c => c.Id == 3);
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private void SetupDbMocks(List<PrayerCardTag> junctions)
    {
        var db = Substitute.For<IDBService>();
        PrayerCardTag.SetDBService(db);
        db.GetAllAsync<PrayerCardTag>().Returns(junctions);
    }

    /// <summary>Configure PrayerCard.LoadAsync(id) to return the supplied card (or null).</summary>
    private static IDBService SetupCardLoadMock(int cardId, PrayerCard? card)
    {
        var db = Substitute.For<IDBService>();
        PrayerCard.SetDBService(db);
        db.GetByIdAsync<PrayerCard>(cardId).Returns(card!);
        return db;
    }
}
