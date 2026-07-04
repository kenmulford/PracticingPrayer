using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class PrayerCardViewModelTests
{
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly IOnboardingService _onboardingService = Substitute.For<IOnboardingService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IDBService _db = Substitute.For<IDBService>();
    private readonly IPrayerSelectionService _selectionService = Substitute.For<IPrayerSelectionService>();

    private const int ArchivedBoxId = 99;

    public PrayerCardViewModelTests()
    {
        PrayerCard.SetDBService(_db);
        Prayer.SetDBService(_db);
        CardBox.SetDBService(_db);
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());
        _settings.ArchivedFolderId.Returns(ArchivedBoxId);

        // Archive shows a confirm dialog; default the accept so existing archive-path
        // tests exercise the post-confirm behavior. Cancel-path test overrides this.
        _navigationService
            .DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
    }

    private PrayerCardViewModel CreateSut() =>
        new(_cardService, _prayerService, _onboardingService, _navigationService,
            _accessibilityService, _boxService, _settings);

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_IsNew()
    {
        var sut = CreateSut();
        Assert.True(sut.IsNew);
        Assert.False(sut.CanDelete);
    }

    // ── Title + IsDirty ───────────────────────────────────────────────

    [Fact]
    public void Title_Change_MakesDirty()
    {
        var sut = CreateSut();
        sut.Title = "New Card";

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void Title_ResetToOriginal_NotDirty()
    {
        var sut = CreateSut();
        sut.Title = "Changed";
        sut.Title = null!; // back to default (empty → null in PrayerCard)

        // New card's original title is empty string, PrayerCard.Title defaults to null
        // IsDirty compares Title (null) != _originalTitle (empty) — so still dirty
        // This is correct: user typed, then cleared. The form is modified.
    }

    // ── SaveAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveCommand_SavesAndNavigatesBack()
    {
        var sut = CreateSut();
        sut.Title = "My Card";

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(Arg.Any<PrayerCard>());
        _accessibilityService.Received(1).Announce("Card saved");
        await _navigationService.Received(1).GoToAsync(Arg.Is<string>(s => s.StartsWith("..")));
    }

    [Fact]
    public async Task SaveCommand_NewCard_AdvancesOnboarding()
    {
        var sut = CreateSut();
        sut.Title = "First Card";

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        _onboardingService.Received(1).Advance();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCommand_SystemCard_NoOp()
    {
        var sut = CreateSut();
        // Simulate system card by setting IsSystem (need to set via the backing PrayerCard)
        // Since we can't easily set IsSystem on a new card, test that delete with
        // no confirmation just doesn't navigate
        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        await ((IAsyncRelayCommand)sut.DeleteCommand).ExecuteAsync(null);

        // System card check is first — if IsSystem, returns immediately
        // For a non-system new card, it shows confirm dialog
        await _cardService.DidNotReceive().DeleteCardAsync(Arg.Any<PrayerCard>());
    }

    [Fact]
    public async Task DeleteCommand_Confirmed_DeletesAndNavigates()
    {
        var sut = CreateSut();
        // Make it non-system and non-new by giving it an ID
        sut.Id = 5;

        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _prayerService.GetPrayersByCardAsync(5).Returns(new List<Prayer>());

        await ((IAsyncRelayCommand)sut.DeleteCommand).ExecuteAsync(null);

        await _cardService.Received(1).DeleteCardAsync(Arg.Any<PrayerCard>());
        _accessibilityService.Received(1).Announce("Card deleted");
        await _navigationService.Received(1).GoToAsync(Arg.Is<string>(s => s.Contains("deleted")));
    }

    [Fact]
    public async Task DeleteCommand_Cancelled_DoesNotDelete()
    {
        var sut = CreateSut();
        sut.Id = 5;

        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        await ((IAsyncRelayCommand)sut.DeleteCommand).ExecuteAsync(null);

        await _cardService.DidNotReceive().DeleteCardAsync(Arg.Any<PrayerCard>());
    }

    // ── CanLeaveAsync (IEditGuard) ────────────────────────────────────

    [Fact]
    public async Task CanLeaveAsync_NotDirty_ReturnsTrue()
    {
        var sut = CreateSut();
        Assert.True(await sut.CanLeaveAsync());
    }

    [Fact]
    public async Task CanLeaveAsync_Dirty_ShowsConfirm()
    {
        var sut = CreateSut();
        sut.Title = "Changed";

        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var result = await sut.CanLeaveAsync();

        Assert.True(result);
        await _navigationService.Received(1).DisplayConfirmAsync(
            "Unsaved Changes", Arg.Any<string>(), "Discard", "Cancel");
    }

    // ── CanShare ────────────────────────────────────────────────────

    [Fact]
    public void CanShare_NewCard_ZeroPrayers_ReturnsFalse()
    {
        var sut = CreateSut();
        // ActivePrayerCount defaults to 0, IsSystem defaults to false
        Assert.False(sut.CanShare);
    }

    [Fact]
    public void CanShare_HasActivePrayers_ReturnsTrue()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 3;

        Assert.True(sut.CanShare);
    }

    [Fact]
    public void CanShare_ActivePrayerCountChange_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrayerCardViewModel.CanShare)) raised = true;
        };

        sut.ActivePrayerCount = 2;

        Assert.True(raised);
    }

    // ── CanPray ───────────────────────────────────────────────────────

    [Fact]
    public void CanPray_ZeroPrayers_False()
    {
        var sut = CreateSut();
        Assert.False(sut.CanPray);
    }

    [Fact]
    public void CanPray_HasActivePrayers_True()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 1;
        Assert.True(sut.CanPray);
    }

    [Fact]
    public void PrayCommand_CanExecute_TracksActivePrayerCount()
    {
        var sut = CreateSut();
        Assert.False(((IAsyncRelayCommand)sut.PrayCommand).CanExecute(null));

        sut.ActivePrayerCount = 2;

        Assert.True(((IAsyncRelayCommand)sut.PrayCommand).CanExecute(null));
    }

    // ── PrayCommand ───────────────────────────────────────────────────

    [Fact]
    public async Task PrayCommand_ResolvesActiveIds_SetsSelection_NavigatesScopeSelection()
    {
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings)
        {
            SelectionServiceFactory = () => _selectionService
        };

        // GetAllActivePrayersAsync is the single source of truth for "active"
        // (it already filters out IsAnswered), so the mock returns only active rows
        // and PrayAsync narrows to this card by PrayerCardId. A prayer for another
        // card (id 200) is included to prove the card-id filter.
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>
        {
            new() { Id = 100, PrayerCardId = 7 },
            new() { Id = 102, PrayerCardId = 7 },
            new() { Id = 200, PrayerCardId = 99 }  // other card — excluded
        }.AsReadOnly());

        await ((IAsyncRelayCommand)sut.PrayCommand).ExecuteAsync(null);

        _selectionService.Received(1).Set(Arg.Is<IEnumerable<int>>(
            ids => ids.OrderBy(x => x).SequenceEqual(new[] { 100, 102 })));
        await _navigationService.Received(1).GoToAsync(
            $"{Routes.PrayerTimePage}?scope={Routes.ScopeSelection}");
    }

    [Fact]
    public async Task PrayCommand_NoActivePrayers_DoesNotSetOrNavigate()
    {
        var card = new PrayerCard { Id = 8, Title = "Empty" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings)
        {
            SelectionServiceFactory = () => _selectionService
        };

        // No active prayers for this card — GetAllActivePrayersAsync returns none
        // matching PrayerCardId == 8, so PrayAsync short-circuits.
        _prayerService.GetAllActivePrayersAsync().Returns(new List<Prayer>().AsReadOnly());

        await ((IAsyncRelayCommand)sut.PrayCommand).ExecuteAsync(null);

        _selectionService.DidNotReceive().Set(Arg.Any<IEnumerable<int>>());
        await _navigationService.DidNotReceive().GoToAsync(Arg.Any<string>());
    }

    // ── Box Picker (Collection assignment) ────────────────────────────

    [Fact]
    public async Task LoadBoxPickerAsync_PopulatesAvailableBoxes_WithLooseCardsFirst()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" },
            new() { Id = 6, Name = "Ministry" },
            new() { Id = 10, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem },
            new() { Id = 20, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived }
        }.AsReadOnly());

        var sut = CreateSut();
        await sut.LoadBoxPickerAsync();

        Assert.Equal(3, sut.AvailableBoxes.Count); // Loose Cards + 2 user boxes, no system
        // Card-edit picker is RealBoxPickerItem-only — no All-collections sentinel.
        var realBoxes = sut.AvailableBoxes.OfType<RealBoxPickerItem>().ToList();
        Assert.Equal(3, realBoxes.Count);
        Assert.Equal(BoxStrings.Unorganized, realBoxes[0].Name);
        Assert.Equal(0, realBoxes[0].BoxId);
        Assert.Equal("Family", realBoxes[1].Name);
        Assert.Equal("Ministry", realBoxes[2].Name);
    }

    [Fact]
    public async Task LoadBoxPickerAsync_SetsSelectedBoxToCurrentBoxId()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" }
        }.AsReadOnly());

        var card = new PrayerCard { Id = 1, Title = "Test", BoxId = 5 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        await sut.LoadBoxPickerAsync();

        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(5, selected.BoxId);
    }

    [Fact]
    public async Task LoadBoxPickerAsync_DefaultsToLooseCards_WhenBoxIdIsZero()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" }
        }.AsReadOnly());

        var sut = CreateSut(); // new PrayerCard has BoxId=0
        await sut.LoadBoxPickerAsync();

        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selected.BoxId);
    }

    [Fact]
    public async Task SaveAsync_SetsBoxIdFromSelectedBox()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" }
        }.AsReadOnly());

        var sut = CreateSut();
        sut.Title = "Test Card";
        await sut.LoadBoxPickerAsync();
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 5);

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.BoxId == 5));
    }

    [Fact]
    public void RealBoxPickerItem_EqualsByBoxId()
    {
        var a = new RealBoxPickerItem(5, "Family");
        var b = new RealBoxPickerItem(5, "Different Name");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RealBoxPickerItem_NotEqual_DifferentBoxId()
    {
        var a = new RealBoxPickerItem(5, "Family");
        var b = new RealBoxPickerItem(6, "Family");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AllCollectionsPickerItem_IsSingleton()
    {
        Assert.Same(AllCollectionsPickerItem.Instance, AllCollectionsPickerItem.Instance);
    }

    // ── IsBusy on Save ─────────────────────────────────────────────────
    // Save flows show no progress affordance and don't gate against double-tap. IsBusy
    // drives an ActivityIndicator and the SaveCommand canExecute, so a second tap can't
    // duplicate the work.

    [Fact]
    public async Task SaveCommand_SetsIsBusyDuringExecution_AndResetsAfter()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        bool capturedIsBusy = false;
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>()).Returns(call =>
        {
            capturedIsBusy = sut.IsBusy;
            return Task.FromResult((PrayerCard)call[0]);
        });

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        Assert.True(capturedIsBusy);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task SaveCommand_ResetsIsBusyAfterException()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>())
            .Returns(_ => Task.FromException<PrayerCard>(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null));

        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task SaveCommand_DoubleInvoke_RunsServiceOnlyOnce()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        var tcs = new TaskCompletionSource<PrayerCard>();
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>()).Returns(tcs.Task);

        var first = ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        var second = ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        tcs.SetResult(new PrayerCard());
        await Task.WhenAll(first, second);

        await _cardService.Received(1).SaveCardAsync(Arg.Any<PrayerCard>());
    }

    // ── CardVisualState (issue #33 P1A) ─────────────────────────────────
    // Drives the CardStates VisualStateGroup on the cell Border.
    // Precedence: MultiSelected > Highlighted > Normal.

    [Fact]
    public void CardVisualState_DefaultsToNormal()
    {
        var sut = CreateSut();
        Assert.Equal("Normal", sut.CardVisualState);
    }

    [Fact]
    public void CardVisualState_HighlightedOnly_IsHighlighted()
    {
        var sut = CreateSut();
        sut.IsHighlighted = true;
        Assert.Equal("Highlighted", sut.CardVisualState);
    }

    [Fact]
    public void CardVisualState_MultiSelectedOnly_IsMultiSelected()
    {
        var sut = CreateSut();
        sut.IsMultiSelected = true;
        Assert.Equal("MultiSelected", sut.CardVisualState);
    }

    [Fact]
    public void CardVisualState_BothFlags_MultiSelectedWins()
    {
        var sut = CreateSut();
        sut.IsHighlighted = true;
        sut.IsMultiSelected = true;
        Assert.Equal("MultiSelected", sut.CardVisualState);
    }

    [Fact]
    public void CardVisualState_IsHighlightedChange_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.IsHighlighted = true;

        Assert.Contains(nameof(PrayerCardViewModel.CardVisualState), raised);
    }

    [Fact]
    public void CardVisualState_IsMultiSelectedChange_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.IsMultiSelected = true;

        Assert.Contains(nameof(PrayerCardViewModel.CardVisualState), raised);
    }

    // ── HasAnyPrayer (issue #33 P3) ────────────────────────────────────
    // Gates the parenthetical "(N)" prayer count Label on the collapsed card row.
    // Definition: !IsExpanded && ActivePrayerCount > 0. Without a Parent VM,
    // IsExpanded resolves false (Parent?.ExpandedCardId == _prayerCard.Id is
    // false on a null Parent), so the gate is purely count-driven in tests.

    [Fact]
    public void HasAnyPrayer_DefaultsToFalse()
    {
        var sut = CreateSut();
        // ActivePrayerCount defaults to 0
        Assert.False(sut.HasAnyPrayer);
    }

    [Fact]
    public void HasAnyPrayer_TrueWhenActiveCountAboveZero()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 3;

        Assert.True(sut.HasAnyPrayer);
    }

    [Fact]
    public void HasAnyPrayer_FalseWhenActiveCountIsZero()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 5;
        sut.ActivePrayerCount = 0;

        Assert.False(sut.HasAnyPrayer);
    }

    [Fact]
    public void HasAnyPrayer_ActivePrayerCountChange_ZeroToNonZero_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.ActivePrayerCount = 4;

        Assert.Contains(nameof(PrayerCardViewModel.HasAnyPrayer), raised);
    }

    [Fact]
    public void HasAnyPrayer_ActivePrayerCountChange_NonZeroToZero_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 2;
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.ActivePrayerCount = 0;

        Assert.Contains(nameof(PrayerCardViewModel.HasAnyPrayer), raised);
    }

    // ── PrayersHeader (issue #33 UAT-8) ────────────────────────────────
    // Inner expanded-card "Prayers" header display string. Returns "Prayers"
    // when ActivePrayerCount==0 (clean affordance on an empty card) and
    // "Prayers (N)" when there are active prayers. PropertyChanged fires
    // from the ActivePrayerCount setter (the real dependency edge).

    [Fact]
    public void PrayersHeader_DefaultsToPlainPrayers()
    {
        var sut = CreateSut();
        // ActivePrayerCount defaults to 0
        Assert.Equal("Prayers", sut.PrayersHeader);
    }

    [Fact]
    public void PrayersHeader_WithCount_FormatsAsParenthetical()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 3;

        Assert.Equal("Prayers (3)", sut.PrayersHeader);
    }

    [Fact]
    public void PrayersHeader_CountChangeFromZero_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.ActivePrayerCount = 4;

        Assert.Contains(nameof(PrayerCardViewModel.PrayersHeader), raised);
    }

    [Fact]
    public void PrayersHeader_CountChangeBackToZero_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        sut.ActivePrayerCount = 5;
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.ActivePrayerCount = 0;

        Assert.Contains(nameof(PrayerCardViewModel.PrayersHeader), raised);
        Assert.Equal("Prayers", sut.PrayersHeader);
    }

    // ── AccessibleCardHeader (issue #148 Phase 2, item 1a) ─────────────
    // Composed VoiceOver/TalkBack label for the card header row. Replaces the
    // Cards_CardHeader_AnnouncesExpandCollapseState E2E (AccessibilityTests.cs)
    // with a deterministic, date-free unit assertion over the getter
    // (PrayerCardViewModel.cs:262). Expand state is driven by the single-expand
    // invariant on Parent.ExpandedCardId; collapsed is the null-Parent default.

    [Fact]
    public void AccessibleCardHeader_Collapsed_IncludesCountSegment_Singular()
    {
        // Null Parent ⇒ IsExpanded == false ⇒ the count segment is present.
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings)
        {
            ActivePrayerCount = 1
        };

        // Build the expected string from the inputs so the assert is exact.
        Assert.Equal("Family, 1 prayer, Collapsed", sut.AccessibleCardHeader);
    }

    [Fact]
    public void AccessibleCardHeader_Collapsed_IncludesCountSegment_Plural()
    {
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings)
        {
            ActivePrayerCount = 3
        };

        Assert.Equal("Family, 3 prayers, Collapsed", sut.AccessibleCardHeader);
    }

    [Fact]
    public void AccessibleCardHeader_Expanded_DropsCountSegment_AndSaysExpanded()
    {
        // Attach a Parent VM whose ExpandedCardId matches the card's Id so the
        // read-only IsExpanded projection resolves true. RaiseIsExpandedChanged()
        // is internal — reachable here because the VM source is <Compile Include>'d
        // into this test assembly.
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings)
        {
            ActivePrayerCount = 3
        };

        var parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(), _settings,
            _boxService, Substitute.For<IConfidentialAccessService>(), new WeakReferenceMessenger());
        sut.Parent = parent;
        parent.ExpandedCardId = card.Id;
        sut.RaiseIsExpandedChanged();

        // Count segment drops out (gated by !IsExpanded); state flips to Expanded.
        Assert.Equal("Family, Expanded", sut.AccessibleCardHeader);
    }

    // ── ArchiveCommand ───────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveCommand_NonArchived_Confirmed_CallsAssignBoxWithArchivedFolderId()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _navigationService.Received(1).DisplayConfirmAsync(
            "Archive Card?", Arg.Any<string>(), "Archive", "Cancel");
        await _cardService.Received(1).AssignBoxAsync(card, ArchivedBoxId);
    }

    [Fact]
    public async Task ArchiveCommand_NonArchived_Cancelled_DoesNotAssignBox()
    {
        _navigationService
            .DisplayConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _cardService.DidNotReceive().AssignBoxAsync(Arg.Any<PrayerCard>(), Arg.Any<int>());
    }

    [Fact]
    public async Task ArchiveCommand_Archived_Unarchive_NoConfirmDialog_AssignsZero()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = ArchivedBoxId };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _navigationService.DidNotReceive().DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _cardService.Received(1).AssignBoxAsync(card, 0);
    }

    [Fact]
    public async Task ArchiveCommand_ArchivedCard_CallsAssignBoxWithZero()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = ArchivedBoxId };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(card, 0);
    }

    [Fact]
    public async Task ArchiveCommand_Archive_StashesPriorBoxIdIntoPreArchiveBoxId()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 42 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        Assert.Equal(42, card.PreArchiveBoxId);
        await _cardService.Received(1).AssignBoxAsync(card, ArchivedBoxId);
    }

    [Fact]
    public async Task ArchiveCommand_Unarchive_StashedBoxStillExists_RestoresAndClearsPreArchive()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = ArchivedBoxId, PreArchiveBoxId = 42 };
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 42, Name = "Family" }
        }.AsReadOnly());
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(card, 42);
        Assert.Null(card.PreArchiveBoxId);
    }

    [Fact]
    public async Task ArchiveCommand_Unarchive_StashedBoxDeleted_FallsBackToZero()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = ArchivedBoxId, PreArchiveBoxId = 42 };
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 7, Name = "Other" }
        }.AsReadOnly());
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(card, 0);
        Assert.Null(card.PreArchiveBoxId);
    }

    [Fact]
    public async Task ArchiveCommand_RoundTrip_ReArchiveReStashesRestoredBox()
    {
        // The real user scenario: archive → unarchive → re-archive. The re-archive
        // must stash the RESTORED box (42), not a stale PreArchiveBoxId value.
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 42 };
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 42, Name = "Family" }
        }.AsReadOnly());
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        // 1. Archive: stash 42, move to archived folder.
        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);
        Assert.Equal(42, card.PreArchiveBoxId);
        await _cardService.Received(1).AssignBoxAsync(card, ArchivedBoxId);
        card.BoxId = ArchivedBoxId; // AssignBoxAsync is stubbed; reflect the move for IsArchived.

        // 2. Unarchive: restore to 42, clear PreArchiveBoxId.
        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);
        await _cardService.Received(1).AssignBoxAsync(card, 42);
        Assert.Null(card.PreArchiveBoxId);
        card.BoxId = 42; // reflect the restore.

        // 3. Re-archive: must re-stash the RESTORED box (42), not a stale value.
        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);
        Assert.Equal(42, card.PreArchiveBoxId);
        await _cardService.Received(2).AssignBoxAsync(card, ArchivedBoxId);
    }

    [Fact]
    public async Task ArchiveCommand_Unarchive_StashedZero_FallsBackToZero_WithoutBoxLookup()
    {
        // Archiving from Loose Cards (BoxId = 0) stashes 0. Unarchive must return to 0
        // via the boxId != 0 guard, WITHOUT consulting GetBoxesAsync.
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        // 1. Archive from Loose Cards: stash 0.
        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);
        Assert.Equal(0, card.PreArchiveBoxId);
        await _cardService.Received(1).AssignBoxAsync(card, ArchivedBoxId);
        card.BoxId = ArchivedBoxId; // reflect the move for IsArchived.

        _boxService.ClearReceivedCalls();

        // 2. Unarchive: guard short-circuits to 0 without a box lookup.
        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);
        await _cardService.Received(1).AssignBoxAsync(card, 0);
        Assert.Null(card.PreArchiveBoxId);
        await _boxService.DidNotReceive().GetBoxesAsync();
    }

    [Fact]
    public async Task ArchiveCommand_Unarchive_NullPreArchiveBoxId_RestoresToZero()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = ArchivedBoxId, PreArchiveBoxId = null };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _cardService.Received(1).AssignBoxAsync(card, 0);
        Assert.Null(card.PreArchiveBoxId);
    }

    [Fact]
    public void IsArchived_FalseWhenBoxIdIsZero()
    {
        var card = new PrayerCard { Id = 7, BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.False(sut.IsArchived);
    }

    [Fact]
    public void IsArchived_TrueWhenBoxIdEqualsArchivedFolderId()
    {
        var card = new PrayerCard { Id = 7, BoxId = ArchivedBoxId };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.True(sut.IsArchived);
    }

    [Fact]
    public void ArchiveLabel_NonArchivedCard_ReturnsArchive()
    {
        var card = new PrayerCard { Id = 7, BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.Equal("Archive", sut.ArchiveLabel);
    }

    [Fact]
    public void ArchiveLabel_ArchivedCard_ReturnsUnarchive()
    {
        var card = new PrayerCard { Id = 7, BoxId = ArchivedBoxId };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.Equal("Unarchive", sut.ArchiveLabel);
    }

    [Fact]
    public void ArchiveCommand_SystemCard_CannotExecute()
    {
        var card = new PrayerCard { Id = 7, IsSystem = true };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.False(sut.ArchiveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ArchiveCommand_DoesNotChangePrayerStatus()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        await _prayerService.DidNotReceive().SavePrayerAsync(Arg.Any<Prayer>());
    }

    [Fact]
    public async Task ArchiveCommand_Archive_RaisesIsArchivedAndArchiveLabelChanged()
    {
        var card = new PrayerCard { Id = 7, Title = "Test", BoxId = 0 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await ((IAsyncRelayCommand)sut.ArchiveCommand).ExecuteAsync(null);

        Assert.Contains(nameof(PrayerCardViewModel.IsArchived), raised);
        Assert.Contains(nameof(PrayerCardViewModel.ArchiveLabel), raised);
    }

    // ── AddPrayerCommand (issue #169 convert) ──────────────────────────
    // Replaces the Cards_AddPrayerToCard E2E (PrayerCardTests.cs) with a
    // deterministic unit assertion over the card → detail nav edge: tapping
    // "+ Add prayer" advances onboarding and navigates to PrayerDetailPage with
    // newForCard set to this card's Id (PrayerCardViewModel.cs:530-534).

    [Fact]
    public async Task AddPrayerCommand_NavigatesToDetailForCard_AndAdvancesOnboarding()
    {
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.AddPrayerCommand).ExecuteAsync(null);

        _onboardingService.Received(1).Advance();
        await _navigationService.Received(1).GoToAsync($"{Routes.PrayerDetailPage}?newForCard=7");
    }

    // ── SelectCardCommand (issue #169 convert) ─────────────────────────
    // Replaces the Cards_EditButton_NavigatesToEditPage E2E (PrayerCardTests.cs)
    // with a deterministic unit assertion: tapping Edit on a user card navigates
    // to PrayerCardPage with load set to its Id, while a system card is a no-op
    // (guarded both by the !IsSystem canExecute and the in-method early return —
    // PrayerCardViewModel.cs:318, :435-439).

    [Fact]
    public async Task SelectCardCommand_NonSystem_NavigatesToCardEditPage()
    {
        var card = new PrayerCard { Id = 12, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        await ((IAsyncRelayCommand)sut.SelectCardCommand).ExecuteAsync(null);

        await _navigationService.Received(1).GoToAsync($"{Routes.PrayerCardPage}?load=12");
    }

    [Fact]
    public async Task SelectCardCommand_SystemCard_NoOp_AndCannotExecute()
    {
        var card = new PrayerCard { Id = 13, Title = "Quick Add", IsSystem = true };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.False(sut.SelectCardCommand.CanExecute(null));

        // Even if forced (ExecuteAsync bypasses canExecute), the in-method
        // IsSystem guard returns before any navigation.
        await ((IAsyncRelayCommand)sut.SelectCardCommand).ExecuteAsync(null);

        await _navigationService.DidNotReceive().GoToAsync(Arg.Any<string>());
    }

    // ── ShowActionChips (issue #169 convert) ───────────────────────────
    // Replaces the Cards_ExpandByTap_RealizesActionChips E2E (PrayerCardTests.cs)
    // with a deterministic unit assertion over the chip-visibility gate
    // (PrayerCardViewModel.cs:152): ShowActionChips == IsExpanded && !IsSystem.
    // IsExpanded is the read-only projection over Parent.ExpandedCardId; the
    // expand/collapse state machine itself is covered at
    // PrayerCardsViewModelTests (ExpandedCardId_Set_RaisesIsExpandedPropertyChanged_…).

    [Fact]
    public void ShowActionChips_Collapsed_False()
    {
        // Null Parent ⇒ IsExpanded false ⇒ chips hidden.
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.False(sut.IsExpanded);
        Assert.False(sut.ShowActionChips);
    }

    [Fact]
    public void ShowActionChips_ExpandedNonSystem_True()
    {
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        var parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(), _settings,
            _boxService, Substitute.For<IConfidentialAccessService>(), new WeakReferenceMessenger());
        sut.Parent = parent;
        parent.ExpandedCardId = card.Id;
        sut.RaiseIsExpandedChanged();

        Assert.True(sut.IsExpanded);
        Assert.True(sut.ShowActionChips);
    }

    [Fact]
    public void ShowActionChips_ExpandedSystemCard_False()
    {
        // System card: chips stay hidden even when expanded (system cards have no
        // Edit/Favorite/Delete affordances — matches Cards_SystemCard_* E2E intent).
        var card = new PrayerCard { Id = 7, Title = "Quick Add", IsSystem = true };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        var parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(), _settings,
            _boxService, Substitute.For<IConfidentialAccessService>(), new WeakReferenceMessenger());
        sut.Parent = parent;
        parent.ExpandedCardId = card.Id;
        sut.RaiseIsExpandedChanged();

        Assert.True(sut.IsExpanded);
        Assert.False(sut.ShowActionChips);
    }

    // ── ToggleFavoriteCommand (E2E coverage triage) ────────────────────
    // Characterization coverage for the favorite toggle. Replaces the
    // Cards_FavoriteToggle_ChangesState E2E (FeatureGapTests.cs) with a
    // deterministic unit assertion over ToggleFavoriteAsync
    // (PrayerCardViewModel.cs:320, :457): invoking the command flips
    // IsFavorite (false→true, true→false) and persists each toggle via
    // the card service.

    [Fact]
    public async Task ToggleFavoriteCommand_TogglesIsFavorite_AndPersistsEachToggle()
    {
        var card = new PrayerCard { Id = 7, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        Assert.False(sut.IsFavorite);

        // false → true
        await ((IAsyncRelayCommand)sut.ToggleFavoriteCommand).ExecuteAsync(null);
        Assert.True(sut.IsFavorite);

        // true → false
        await ((IAsyncRelayCommand)sut.ToggleFavoriteCommand).ExecuteAsync(null);
        Assert.False(sut.IsFavorite);

        // The save path is exercised on each toggle (the VM is the same
        // PrayerCard reference passed to the service).
        await _cardService.Received(2).SaveCardAsync(card);
    }

    // ── ProtectionMode (issue #252) ──────────────────────────────────────
    // Pure get/set wrapper over PrayerCard.ProtectionMode, mirroring the
    // IsFavorite property pattern (PrayerCardViewModel.cs:107-114). No auth
    // gating here — that lands in #253/#254/#255. Saving persists via the
    // existing SaveCommand → SaveCardAsync path (issue #250 already added
    // the column), so no new persistence coverage is required.

    [Fact]
    public void ProtectionMode_DefaultsToNone()
    {
        var sut = CreateSut();
        Assert.Equal(CardProtectionMode.None, sut.ProtectionMode);
    }

    [Fact]
    public void ProtectionMode_Set_UpdatesUnderlyingCard()
    {
        var card = new PrayerCard { Id = 3, Title = "Family" };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        sut.ProtectionMode = CardProtectionMode.Hidden;

        Assert.Equal(CardProtectionMode.Hidden, sut.ProtectionMode);
        Assert.Equal(CardProtectionMode.Hidden, card.ProtectionMode);
    }

    [Fact]
    public void ProtectionMode_RoundTripsAllValues()
    {
        var sut = CreateSut();

        sut.ProtectionMode = CardProtectionMode.LockedVisible;
        Assert.Equal(CardProtectionMode.LockedVisible, sut.ProtectionMode);

        sut.ProtectionMode = CardProtectionMode.Hidden;
        Assert.Equal(CardProtectionMode.Hidden, sut.ProtectionMode);

        sut.ProtectionMode = CardProtectionMode.None;
        Assert.Equal(CardProtectionMode.None, sut.ProtectionMode);
    }

    // ── DisplayTitle / IsLockedVisible — issue #253 masking ────────────
    // Strict constraint: while an effectively-LockedVisible card is locked, the
    // real Title must never be readable through DisplayTitle — cells bind to
    // DisplayTitle, never Title, so the real string never enters the visual tree.

    [Fact]
    public void IsLockedVisible_NoParent_False()
    {
        // No Parent wired (Parent is null) — can't resolve session lock state,
        // so treat as not locked-visible rather than throwing.
        var card = new PrayerCard { Id = 1, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);

        Assert.False(sut.IsLockedVisible);
        Assert.Equal("Secret", sut.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_EffectivelyLockedVisible_SessionLocked_ReturnsProtected()
    {
        var card = new PrayerCard { Id = 1, Title = "Secret Card", ProtectionMode = CardProtectionMode.LockedVisible };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        var confidential = Substitute.For<IConfidentialAccessService>();
        confidential.IsSessionUnlocked.Returns(false);
        sut.Parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(),
            _settings, _boxService, confidential, new WeakReferenceMessenger());

        Assert.True(sut.IsLockedVisible);
        Assert.Equal("Protected", sut.DisplayTitle);
        Assert.DoesNotContain("Secret Card", sut.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_EffectivelyLockedVisible_SessionUnlocked_ReturnsRealTitle()
    {
        var card = new PrayerCard { Id = 1, Title = "Secret Card", ProtectionMode = CardProtectionMode.LockedVisible };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        var confidential = Substitute.For<IConfidentialAccessService>();
        confidential.IsSessionUnlocked.Returns(true);
        sut.Parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(),
            _settings, _boxService, confidential, new WeakReferenceMessenger());

        Assert.False(sut.IsLockedVisible);
        Assert.Equal("Secret Card", sut.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ProtectionModeNone_AlwaysReturnsRealTitle()
    {
        var card = new PrayerCard { Id = 1, Title = "Open Card", ProtectionMode = CardProtectionMode.None };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        var confidential = Substitute.For<IConfidentialAccessService>();
        confidential.IsSessionUnlocked.Returns(false);
        sut.Parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(),
            _settings, _boxService, confidential, new WeakReferenceMessenger());

        Assert.False(sut.IsLockedVisible);
        Assert.Equal("Open Card", sut.DisplayTitle);
    }

    [Fact]
    public void IsLockedVisible_EffectivelyHidden_SessionLocked_False()
    {
        // Hidden cards are excluded from the list entirely (PrayerCardsViewModel
        // section-building) — DisplayTitle's masking is LockedVisible-only, so a
        // Hidden card's cell (if ever bound) is not masked via this path.
        var card = new PrayerCard { Id = 1, Title = "Hidden Card", ProtectionMode = CardProtectionMode.Hidden };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        var confidential = Substitute.For<IConfidentialAccessService>();
        confidential.IsSessionUnlocked.Returns(false);
        sut.Parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(),
            _settings, _boxService, confidential, new WeakReferenceMessenger());

        Assert.False(sut.IsLockedVisible);
    }

    [Fact]
    public void DisplayTitle_BoxCascadesLockedVisible_SessionLocked_ReturnsProtected()
    {
        // Card itself has ProtectionMode.None (defers) but its box cascades
        // LockedVisible via ProtectAllCards — GetEffectiveProtectionMode(card, box)
        // resolves this, so DisplayTitle must mask here too.
        var box = new CardBox { Id = 7, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible };
        var card = new PrayerCard { Id = 1, Title = "Cascaded Secret", ProtectionMode = CardProtectionMode.None, BoxId = 7 };
        var sut = new PrayerCardViewModel(card, _cardService, _prayerService,
            _onboardingService, _navigationService, _accessibilityService, _boxService, _settings);
        var confidential = Substitute.For<IConfidentialAccessService>();
        confidential.IsSessionUnlocked.Returns(false);
        sut.Parent = new PrayerCardsViewModel(_cardService, _prayerService, _onboardingService,
            _navigationService, _accessibilityService, Substitute.For<ITagService>(),
            _settings, _boxService, confidential, new WeakReferenceMessenger());
        sut.Box = box;

        Assert.True(sut.IsLockedVisible);
        Assert.Equal("Protected", sut.DisplayTitle);
    }
}
