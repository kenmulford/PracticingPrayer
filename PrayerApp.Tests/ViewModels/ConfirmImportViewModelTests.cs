using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class ConfirmImportViewModelTests
{
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly IImportPayloadService _payloadService = Substitute.For<IImportPayloadService>();
    private readonly ITextSelectionParser _parser = Substitute.For<ITextSelectionParser>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();

    private readonly IMessenger _messenger = new WeakReferenceMessenger();
    private readonly object _recipient = new();
    private readonly List<BulkChangedMessage> _bulkMessages = new();

    public ConfirmImportViewModelTests()
    {
        // Default service stubs — return the passed entity so the VM's local
        // reference is unchanged (NSubstitute auto-stubs Task<T> to
        // completed/null; explicit Returns ensures the VM gets the same
        // instance back, which matters for Id reads after save).
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>())
            .Returns(ci => Task.FromResult((PrayerCard)ci[0]));
        _prayerService.SavePrayerAsync(Arg.Any<Prayer>(), Arg.Any<bool>())
            .Returns(ci => Task.FromResult((Prayer)ci[0]));
        // Default to empty box list — picker tests override before calling
        // CreateSut. Default lives here (not in CreateSut) so per-test
        // .Returns() calls aren't clobbered when CreateSut runs.
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        _messenger.Register<object, BulkChangedMessage>(_recipient, (_, m) => _bulkMessages.Add(m));
    }

    private ConfirmImportViewModel CreateSut() =>
        new(_cardService, _prayerService, _navigationService, _accessibilityService,
            _messenger, _payloadService, _parser, _boxService);

    private static ParseResult Result(string suggestedTitle, params (string Title, string? Details)[] prayers) =>
        new(prayers.Select(p => new ParsedPrayer(p.Title, p.Details)).ToList().AsReadOnly(), suggestedTitle);

    // ── ConsumePending ──────────────────────────

    [Fact]
    public void ConsumePending_WithStagedPayload_ParsesAndPopulates()
    {
        _payloadService.ConsumePayload().Returns("1. Mom\n2. Dad");
        _parser.Parse("1. Mom\n2. Dad").Returns(Result("Imported May 1",
            ("Mom", null), ("Dad", null)));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.Equal("Imported May 1", sut.CardTitle);
        Assert.Equal(2, sut.Prayers.Count);
        Assert.Equal("Mom", sut.Prayers[0].Title);
        Assert.Equal("Dad", sut.Prayers[1].Title);
    }

    // ── IsDetailsExpanded default on parse (#17 / UX-38) ──────────────

    [Fact]
    public void ConsumePending_PrayerWithDetails_RowDetailsExpanded()
    {
        _payloadService.ConsumePayload().Returns("payload");
        _parser.Parse("payload").Returns(Result("Imported",
            ("Sis", "graduating this weekend, please pray")));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.True(sut.Prayers[0].IsDetailsExpanded);
    }

    [Fact]
    public void ConsumePending_PrayerWithEmptyDetails_RowCollapsed()
    {
        _payloadService.ConsumePayload().Returns("payload");
        _parser.Parse("payload").Returns(Result("Imported", ("Mom", null)));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.False(sut.Prayers[0].IsDetailsExpanded);
    }

    [Fact]
    public void ConsumePending_PrayerWithWhitespaceDetails_RowCollapsed()
    {
        _payloadService.ConsumePayload().Returns("payload");
        _parser.Parse("payload").Returns(Result("Imported", ("Dad", "   ")));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.False(sut.Prayers[0].IsDetailsExpanded);
    }

    [Fact]
    public void ConsumePending_MixedList_ExpandsOnlyRowsWithDetails()
    {
        // Issue #17 repro: row 1 has prefilled details (parser found a body),
        // row 2 does not → row 1 expanded, row 2 collapsed.
        _payloadService.ConsumePayload().Returns("payload");
        _parser.Parse("payload").Returns(Result("Imported",
            ("Sis is graduating from college this weekend, please pray", "please pray"),
            ("Pray for Dad", null)));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.True(sut.Prayers[0].IsDetailsExpanded);
        Assert.False(sut.Prayers[1].IsDetailsExpanded);
    }

    [Fact]
    public void AddPrayerCommand_NewBlankRow_IsCollapsed()
    {
        var sut = CreateSut();

        sut.AddPrayerCommand.Execute(null);

        Assert.False(sut.Prayers[^1].IsDetailsExpanded);
    }

    [Fact]
    public void ConsumePending_WithNoStagedPayload_LeavesDefaults()
    {
        _payloadService.ConsumePayload().Returns((string?)null);
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.Equal(string.Empty, sut.CardTitle);
        Assert.Empty(sut.Prayers);
        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    [Fact]
    public void ConsumePending_CalledTwice_DoesNotReParse()
    {
        // Modal OnAppearing fires on initial show AND on resume from background;
        // re-parsing would clobber the user's in-progress edits.
        _payloadService.ConsumePayload().Returns("1. Mom", (string?)null);
        _parser.Parse("1. Mom").Returns(Result("Imported May 1", ("Mom", null)));
        var sut = CreateSut();

        sut.ConsumePending();
        sut.Prayers[0].Title = "Mom edited";
        sut.ConsumePending();

        Assert.Equal("Mom edited", sut.Prayers[0].Title);
        _parser.Received(1).Parse(Arg.Any<string>());
    }

    [Fact]
    public void ConsumePending_WithStructured_PopulatesDirectlyAndSkipsParser()
    {
        // Deep-link / .prayercard inbound: payload is already structured JSON,
        // parsed inside DeepLinkService. Re-running it through the
        // text-selection parser would mangle clauses (e.g., split a notes
        // string on ';' inside a sentence).
        var staged = new ParseResult(new[]
        {
            new ParsedPrayer("Mom", "chemo starts Tuesday; pray for nausea relief"),
            new ParsedPrayer("Dad", null),
        }, "My Card");
        _payloadService.ConsumeStructured().Returns(staged);
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.Equal("My Card", sut.CardTitle);
        Assert.Equal(2, sut.Prayers.Count);
        Assert.Equal("Mom", sut.Prayers[0].Title);
        Assert.Equal("chemo starts Tuesday; pray for nausea relief", sut.Prayers[0].Details);
        Assert.Equal("Dad", sut.Prayers[1].Title);
        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    [Fact]
    public void ConsumePending_StructuredAndRaw_PrefersStructured()
    {
        // If both slots happen to be staged (shouldn't in practice), prefer
        // the structured payload — it's already authoritative; raw would
        // re-parse the same content lossy.
        var staged = new ParseResult(new[] { new ParsedPrayer("S1", null) }, "Structured Title");
        _payloadService.ConsumeStructured().Returns(staged);
        _payloadService.ConsumePayload().Returns("raw");
        _parser.Parse("raw").Returns(Result("Raw Title", ("R1", null)));
        var sut = CreateSut();

        sut.ConsumePending();

        Assert.Equal("Structured Title", sut.CardTitle);
        Assert.Single(sut.Prayers);
        Assert.Equal("S1", sut.Prayers[0].Title);
        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    // ── SaveCommand.CanExecute ──────────────────────────

    [Fact]
    public void Save_CannotExecute_WhenCardTitleBlank()
    {
        _payloadService.ConsumePayload().Returns("1. Mom");
        _parser.Parse(Arg.Any<string>()).Returns(Result("", ("Mom", null)));
        var sut = CreateSut();
        sut.ConsumePending();

        sut.CardTitle = "   ";

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_CannotExecute_WhenPrayersEmpty()
    {
        _payloadService.ConsumePayload().Returns((string?)null);
        var sut = CreateSut();
        sut.ConsumePending();
        sut.CardTitle = "My card";

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_CanExecute_WhenTitleAndAtLeastOneRow()
    {
        _payloadService.ConsumePayload().Returns("1. Mom");
        _parser.Parse(Arg.Any<string>()).Returns(Result("Imported May 1", ("Mom", null)));
        var sut = CreateSut();
        sut.ConsumePending();

        Assert.True(sut.SaveCommand.CanExecute(null));
    }

    // ── SaveCommand effects ──────────────────────────

    [Fact]
    public async Task Save_CreatesCardWithTitleAndIsImported()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "Family";

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.Title == "Family" && c.IsImported == true),
            false);
    }

    [Fact]
    public async Task Save_TrimsCardTitle()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "  Family  ";

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.Title == "Family"),
            false);
    }

    [Fact]
    public async Task Save_CreatesPrayerPerNonBlankRow()
    {
        var sut = SetupSutWithRows(("Mom", "Surgery"), ("Dad", null));

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Mom" && p.Details == "Surgery" && p.IsImported == true && p.CanNotify == false),
            false);
        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Dad" && p.Details == null),
            false);
    }

    [Fact]
    public async Task Save_SkipsRowsWithBlankTitle()
    {
        var sut = SetupSutWithRows(("Mom", null), ("   ", null), ("Dad", null));

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(2).SavePrayerAsync(Arg.Any<Prayer>(), false);
    }

    [Fact]
    public async Task Save_TrimsPrayerTitleAndDetails()
    {
        var sut = SetupSutWithRows(("  Mom  ", "  Surgery  "));

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Mom" && p.Details == "Surgery"),
            false);
    }

    [Fact]
    public async Task Save_BlankDetails_StoredAsNull()
    {
        var sut = SetupSutWithRows(("Mom", "   "));

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Details == null),
            false);
    }

    [Fact]
    public async Task Save_DelegatesPersistenceThroughServicesWithPublishMessageFalse()
    {
        // The VM must route card + prayer persistence through the service layer
        // with publishMessage:false, so the service handles InvalidateCache
        // internally and the VM only fires a single BulkChangedMessage afterward.
        var sut = SetupSutWithRows(("Mom", null));

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(Arg.Any<PrayerCard>(), false);
        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>(), false);
    }

    [Fact]
    public async Task Save_SendsBulkChangedMessage()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null));

        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bulkMessages);
    }

    [Fact]
    public async Task Save_AnnouncesPrayerCountAndCardTitle()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null), ("Sis", null));
        sut.CardTitle = "Family";

        await sut.SaveCommand.ExecuteAsync(null);

        _accessibilityService.Received(1).Announce(Arg.Is<string>(s =>
            s.Contains("3") && s.Contains("Family")));
    }

    [Fact]
    public async Task Save_NavigatesToCardsTabWithSavedCardIdInUrl()
    {
        // Card id must flow through the route so PrayerCardsViewModel.ApplyQueryAttributes
        // stages PendingSavedIdentifier — without it the imported card and its containing
        // section never get auto-expanded and the user can't see the import landed.
        // The service sets the Id on the card instance (same object, mutated in place).
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>())
            .Returns(ci =>
            {
                ((PrayerCard)ci[0]).Id = 42;
                return Task.FromResult((PrayerCard)ci[0]);
            });
        var sut = SetupSutWithRows(("Mom", null));

        await sut.SaveCommand.ExecuteAsync(null);

        await _navigationService.Received(1).GoToAsync(Routes.PrayerCardsTabImported(42));
    }

    // ── CancelCommand ──────────────────────────

    [Fact]
    public async Task Cancel_PopsModal()
    {
        var sut = CreateSut();

        await sut.CancelCommand.ExecuteAsync(null);

        await _navigationService.Received(1).PopModalAsync();
    }

    [Fact]
    public async Task Cancel_DrainsBothPayloadSlots()
    {
        // If the user dismisses before OnAppearing fires ConsumePending, a
        // stale payload (raw OR structured) could surface on the next launch.
        // Cancel must drain both channels to be safe.
        var sut = CreateSut();

        await sut.CancelCommand.ExecuteAsync(null);

        _payloadService.Received(1).ConsumePayload();
        _payloadService.Received(1).ConsumeStructured();
    }

    // ── DrainIfNotConsumed (swipe-dismiss safety net) ─────────────────────

    [Fact]
    public void DrainIfNotConsumed_FirstCall_DrainsBothChannels()
    {
        // iOS PageSheet swipe-dismiss does not fire CancelCommand. The page's
        // OnDisappearing wires DrainIfNotConsumed so a swipe-down doesn't
        // leak a stale payload to the next launch.
        var sut = CreateSut();

        sut.DrainIfNotConsumed();

        _payloadService.Received(1).ConsumePayload();
        _payloadService.Received(1).ConsumeStructured();
    }

    [Fact]
    public void DrainIfNotConsumed_SecondCall_IsNoOp()
    {
        // OnDisappearing can fire more than once across the page lifecycle;
        // the guard must keep the drain to a single round trip per ViewModel.
        var sut = CreateSut();

        sut.DrainIfNotConsumed();
        sut.DrainIfNotConsumed();

        _payloadService.Received(1).ConsumePayload();
        _payloadService.Received(1).ConsumeStructured();
    }

    [Fact]
    public async Task SaveAsync_ThenDrainIfNotConsumed_DoesNotDrainAgain()
    {
        // Save's path through ConsumePending already pulled both channels.
        // SaveAsync flips the consumed flag so OnDisappearing's safety-net
        // call is a no-op rather than re-invoking the (now-empty) Consume*.
        var sut = SetupSutWithRows(("Mom", null));
        // Reset call counts: SetupSutWithRows ran ConsumePending which already
        // hit ConsumePayload once; the assertion below targets the post-save
        // window only.
        _payloadService.ClearReceivedCalls();

        await sut.SaveCommand.ExecuteAsync(null);
        sut.DrainIfNotConsumed();

        _payloadService.DidNotReceive().ConsumePayload();
        _payloadService.DidNotReceive().ConsumeStructured();
    }

    [Fact]
    public async Task CancelAsync_ThenDrainIfNotConsumed_DoesNotDrainAgain()
    {
        // Cancel itself drains both channels via DrainIfNotConsumed; a
        // follow-on OnDisappearing must not trigger a second round trip.
        var sut = CreateSut();

        await sut.CancelCommand.ExecuteAsync(null);
        sut.DrainIfNotConsumed();

        _payloadService.Received(1).ConsumePayload();
        _payloadService.Received(1).ConsumeStructured();
    }

    // ── AddPrayer / RemovePrayer ──────────────────────────

    [Fact]
    public void AddPrayer_AppendsEmptyRow()
    {
        var sut = CreateSut();
        var initialCount = sut.Prayers.Count;

        sut.AddPrayerCommand.Execute(null);

        Assert.Equal(initialCount + 1, sut.Prayers.Count);
        Assert.Equal(string.Empty, sut.Prayers[^1].Title);
    }

    [Fact]
    public void RemovePrayer_RemovesTargetedRow()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null), ("Sis", null));
        var middle = sut.Prayers[1];

        sut.RemovePrayerCommand.Execute(middle);

        Assert.Equal(2, sut.Prayers.Count);
        Assert.DoesNotContain(middle, sut.Prayers);
    }

    // ── PrayersHeader ──────────────────────────

    [Fact]
    public void PrayersHeader_ReflectsCurrentCount()
    {
        var sut = CreateSut();
        Assert.Equal("Prayers (0)", sut.PrayersHeader);

        sut.Prayers.Add(new EditablePrayer());
        sut.Prayers.Add(new EditablePrayer());
        Assert.Equal("Prayers (2)", sut.PrayersHeader);

        sut.Prayers.RemoveAt(0);
        Assert.Equal("Prayers (1)", sut.PrayersHeader);
    }

    [Fact]
    public void PrayersHeader_RaisesPropertyChanged_OnCollectionChange()
    {
        // Without INPC notification on collection change, the bound Label
        // would stay at "Prayers (0)" after parse populates the list.
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.Prayers.Add(new EditablePrayer());

        Assert.Contains(nameof(sut.PrayersHeader), raised);
    }

    // ── Collection picker (LoadBoxesAsync + SaveAsync BoxId) ──────────

    [Fact]
    public async Task LoadBoxesAsync_LooseCardsFirst_FollowedByUserBoxes()
    {
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 8, Name = "Work", IsSystem = false },
        }));
        var sut = CreateSut();

        await sut.LoadBoxesAsync();

        Assert.Equal(3, sut.AvailableBoxes.Count);
        // Picker is RealBoxPickerItem-only outside Existing-Card mode.
        var realBoxes = sut.AvailableBoxes.OfType<RealBoxPickerItem>().ToList();
        Assert.Equal(3, realBoxes.Count);
        Assert.Equal(0, realBoxes[0].BoxId);
        Assert.Equal("Loose Cards", realBoxes[0].Name);
        Assert.Equal(7, realBoxes[1].BoxId);
        Assert.Equal(8, realBoxes[2].BoxId);
    }

    [Fact]
    public async Task LoadBoxesAsync_DefaultsSelectedBoxToLooseCards()
    {
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();

        await sut.LoadBoxesAsync();

        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selected.BoxId);
    }

    [Fact]
    public async Task LoadBoxesAsync_ExcludesSystemBoxes()
    {
        // Mirror PrayerCardViewModel.LoadBoxPickerAsync: System and Archived
        // are out of scope for the import landing page (users shouldn't be
        // pushed toward archiving an import).
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 99, Name = "Archived", IsSystem = true },
        }));
        var sut = CreateSut();

        await sut.LoadBoxesAsync();

        Assert.Equal(2, sut.AvailableBoxes.Count); // Loose + Family only
        Assert.DoesNotContain(sut.AvailableBoxes, b => b.Name == "Archived");
    }

    [Fact]
    public async Task LoadBoxesAsync_CalledTwice_DoesNotReloadOrClobberSelection()
    {
        // Modal OnAppearing fires on initial show AND on resume from background.
        // Reloading would clobber a user's mid-flow Collection pick.
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);

        await sut.LoadBoxesAsync();

        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(7, selected.BoxId);
        Assert.Equal(2, sut.AvailableBoxes.Count);
        await _boxService.Received(1).GetBoxesAsync();
    }

    [Fact]
    public async Task Save_WithSelectedUserBox_AssignsCardToThatBox()
    {
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "Family imports";
        await sut.LoadBoxesAsync();
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(Arg.Is<PrayerCard>(c => c.BoxId == 7), false);
    }

    [Fact]
    public async Task Save_WithoutLoadingBoxes_DefaultsToBoxIdZero()
    {
        // Defensive: if the picker was never wired (or LoadBoxesAsync failed),
        // saved imports still land in Loose Cards — matches the prior
        // hardcoded behavior.
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "Imported May 1";

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(Arg.Is<PrayerCard>(c => c.BoxId == 0), false);
    }

    [Fact]
    public async Task Save_WithDefaultLooseCardsSelection_AssignsBoxIdZero()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "Imported May 1";
        await sut.LoadBoxesAsync(); // default selection = Loose Cards

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(Arg.Is<PrayerCard>(c => c.BoxId == 0), false);
    }

    // ── ImportMode switching ──────────────────────────────────────────────

    [Fact]
    public void ImportMode_DefaultIsNewCard()
    {
        var sut = CreateSut();

        Assert.Equal(ImportMode.NewCard, sut.ImportMode);
    }

    [Fact]
    public void SetNewCardModeCommand_SetsImportModeToNewCard()
    {
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);

        sut.SetNewCardModeCommand.Execute(null);

        Assert.Equal(ImportMode.NewCard, sut.ImportMode);
    }

    [Fact]
    public void SetExistingCardModeCommand_SetsImportModeToExistingCard()
    {
        var sut = CreateSut();

        sut.SetExistingCardModeCommand.Execute(null);

        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
    }

    // ── CanSave per mode ──────────────────────────────────────────────────

    [Fact]
    public void CanSave_ExistingCardMode_NullSelectedCard_WithPrayers_ReturnsFalse()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        // SelectedCard is null by default

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_ExistingCardMode_SelectedCardSet_WithPrayers_ReturnsTrue()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 1, Title = "Family" };

        Assert.True(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_ExistingCardMode_SelectedCardSet_NoPrayers_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 1, Title = "Family" };

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingImportMode_FiresSaveCommandCanExecuteChanged()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "My Card";
        var canExecuteChangedFired = false;
        sut.SaveCommand.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        sut.SetExistingCardModeCommand.Execute(null);

        Assert.True(canExecuteChangedFired);
    }

    [Fact]
    public void CanSave_NewCardMode_BlankCardTitle_WithPrayers_ReturnsFalse()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.CardTitle = "   ";
        // ImportMode is NewCard by default

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_NewCardMode_AllPrayerTitlesBlank_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.CardTitle = "My Card";
        sut.AddPrayerCommand.Execute(null); // adds row with blank Title
        // ImportMode defaults to NewCard, prayer count > 0 but all titles blank

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_ExistingCardMode_AllPrayerTitlesBlank_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 1, Title = "Family" };
        sut.AddPrayerCommand.Execute(null); // adds row with blank Title

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    // ── AvailableCardGroups filtering (hybrid card picker) ────────────────

    [Fact]
    public async Task LoadCardGroupsAsync_AllCollectionsSentinel_ShowsAllCardsGrouped()
    {
        // Default ExistingCard mode pre-selects the All-collections sentinel;
        // every non-system card surfaces, partitioned by collection.
        var alpha = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        var beta = new PrayerCard { Id = 2, Title = "Beta", BoxId = 8, IsSystem = false };
        var loose = new PrayerCard { Id = 3, Title = "Loose1", BoxId = 0, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { alpha, beta, loose }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 8, Name = "Work", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();

        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        // 3 groups: Family, Loose Cards, Work
        Assert.Equal(3, sut.AvailableCardGroups.Count);
        var totalCards = sut.AvailableCardGroups.Sum(g => g.Cards.Count);
        Assert.Equal(3, totalCards);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_SpecificBox_FiltersToThatCollection()
    {
        var alpha = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        var beta = new PrayerCard { Id = 2, Title = "Beta", BoxId = 8, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { alpha, beta }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 8, Name = "Work", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        Assert.Single(sut.AvailableCardGroups);
        Assert.Equal("Family", sut.AvailableCardGroups[0].CollectionName);
        Assert.Single(sut.AvailableCardGroups[0].Cards);
        Assert.Equal("Alpha", sut.AvailableCardGroups[0].Cards[0].Title);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_SortsCardsAlphabeticallyWithinGroup()
    {
        var zulu = new PrayerCard { Id = 1, Title = "Zulu", BoxId = 7, IsSystem = false };
        var alpha = new PrayerCard { Id = 2, Title = "Alpha", BoxId = 7, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { zulu, alpha }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        Assert.Equal("Alpha", sut.AvailableCardGroups[0].Cards[0].Title);
        Assert.Equal("Zulu", sut.AvailableCardGroups[0].Cards[1].Title);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_SortsGroupsByCollectionName()
    {
        var workCard = new PrayerCard { Id = 1, Title = "WC", BoxId = 8, IsSystem = false };
        var familyCard = new PrayerCard { Id = 2, Title = "FC", BoxId = 7, IsSystem = false };
        var apostlesCard = new PrayerCard { Id = 3, Title = "AC", BoxId = 9, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { workCard, familyCard, apostlesCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 8, Name = "Work", IsSystem = false },
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 9, Name = "Apostles", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();

        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal("Apostles", sut.AvailableCardGroups[0].CollectionName);
        Assert.Equal("Family", sut.AvailableCardGroups[1].CollectionName);
        Assert.Equal("Work", sut.AvailableCardGroups[2].CollectionName);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_LooseCardsAppearAsGroup_BoxId0()
    {
        // BoxId 0 cards must surface under the "Loose Cards" header — without
        // a name lookup hit, they would fall into "Unknown" and look broken.
        var loose = new PrayerCard { Id = 1, Title = "Stray", BoxId = 0, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { loose }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();

        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        Assert.Single(sut.AvailableCardGroups);
        Assert.Equal("Loose Cards", sut.AvailableCardGroups[0].CollectionName);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_ExcludesSystemCards()
    {
        var user = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        var system = new PrayerCard { Id = 2, Title = "System", BoxId = 7, IsSystem = true };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { user, system }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();

        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        var allCards = sut.AvailableCardGroups.SelectMany(g => g.Cards).ToList();
        Assert.DoesNotContain(allCards, c => c.CardId == 2);
        Assert.Single(allCards);
    }

    [Fact]
    public async Task LoadCardGroupsAsync_BoxChange_NullsSelectedCard()
    {
        var alpha = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { alpha }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);
        sut.SelectedCard = sut.AvailableCardGroups[0].Cards.FirstOrDefault();
        Assert.NotNull(sut.SelectedCard);

        // Change to a different box → SelectedCard must clear so save-gate stays accurate.
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 0);
        await Task.Delay(50);

        Assert.Null(sut.SelectedCard);
    }

    // ── ImportMode flip — All-sentinel insertion / removal ────────────────

    [Fact]
    public async Task EnteringExistingCardMode_AddsAllSentinelAndSelectsIt()
    {
        // The All-collections sentinel only exists in ExistingCard mode; it
        // would be misleading in NewCard mode (a new card needs ONE BoxId).
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        Assert.DoesNotContain(sut.AvailableBoxes, b => b is AllCollectionsPickerItem);

        sut.SetExistingCardModeCommand.Execute(null);

        Assert.IsType<AllCollectionsPickerItem>(sut.AvailableBoxes[0]);
        Assert.IsType<AllCollectionsPickerItem>(sut.SelectedBox);
    }

    [Fact]
    public async Task LeavingExistingCardMode_RemovesAllSentinel()
    {
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        Assert.Contains(sut.AvailableBoxes, b => b is AllCollectionsPickerItem);

        sut.SetNewCardModeCommand.Execute(null);

        Assert.DoesNotContain(sut.AvailableBoxes, b => b is AllCollectionsPickerItem);
        // SelectedBox restored to Loose Cards so a NewCard save still has a valid BoxId.
        var selectedAfter = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selectedAfter.BoxId);
    }

    [Fact]
    public async Task SelectedBoxChange_InExistingCardMode_TriggersReload()
    {
        var alpha = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { alpha }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);
        _cardService.ClearReceivedCalls();

        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        await _cardService.Received().GetCardsAsync();
    }

    [Fact]
    public async Task SelectedBoxChange_InNewCardMode_DoesNotTriggerCardLoad()
    {
        // NewCard mode: the picker drives the new card's BoxId. There is no
        // card list to refresh, and pulling cards on every box change is waste.
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        _cardService.ClearReceivedCalls();

        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        await _cardService.DidNotReceive().GetCardsAsync();
    }

    // ── HasNoAvailableCards (empty-state Label) ───────────────────────────

    [Fact]
    public void HasNoAvailableCards_TrueWhenAllGroupsEmpty_FalseOtherwise()
    {
        var sut = CreateSut();

        // NewCard mode with no groups → false (empty state is only meaningful
        // in ExistingCard mode; the list is hidden anyway).
        Assert.False(sut.HasNoAvailableCards);

        sut.SetExistingCardModeCommand.Execute(null);
        // ExistingCard mode + no groups → true (empty surface)
        Assert.True(sut.HasNoAvailableCards);
    }

    [Fact]
    public void HasNoAvailableCards_FalseWhenAtLeastOneGroupHasCards()
    {
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        Assert.True(sut.HasNoAvailableCards);

        sut.AvailableCardGroups.Add(new CardCollectionGroup
        {
            CollectionName = "Family",
            Cards = { new CardPickerItem { CardId = 1, Title = "Alpha" } },
        });

        Assert.False(sut.HasNoAvailableCards);
    }

    [Fact]
    public void HasNoAvailableCards_RaisesPropertyChanged_OnImportModeSwitch()
    {
        var sut = CreateSut();
        var fired = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfirmImportViewModel.HasNoAvailableCards))
                fired++;
        };

        sut.SetExistingCardModeCommand.Execute(null);

        Assert.True(fired >= 1);
    }

    [Fact]
    public void HasNoAvailableCards_RaisesPropertyChanged_OnAvailableCardGroupsChange()
    {
        // CollectionChanged → property changed; the XAML binding only
        // refreshes when PropertyChanged fires, so this wiring matters.
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        var fired = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfirmImportViewModel.HasNoAvailableCards))
                fired++;
        };

        sut.AvailableCardGroups.Add(new CardCollectionGroup
        {
            CollectionName = "Family",
            Cards = { new CardPickerItem { CardId = 1, Title = "Alpha" } },
        });
        sut.AvailableCardGroups.Clear();

        Assert.True(fired >= 2);
    }

    // ── SelectCardCommand ─────────────────────────────────────────────────

    [Fact]
    public void SelectCardCommand_SetsSelectedCard()
    {
        var sut = CreateSut();
        var item = new CardPickerItem { CardId = 1, Title = "Family" };

        sut.SelectCardCommand.Execute(item);

        Assert.Same(item, sut.SelectedCard);
    }

    [Fact]
    public void SelectCardCommand_ClearsIsSelectedOnPreviousItem()
    {
        var sut = CreateSut();
        var prev = new CardPickerItem { CardId = 1, Title = "Old" };
        var next = new CardPickerItem { CardId = 2, Title = "New" };
        sut.SelectCardCommand.Execute(prev);
        Assert.True(prev.IsSelected);

        sut.SelectCardCommand.Execute(next);

        Assert.False(prev.IsSelected);
    }

    [Fact]
    public void SelectCardCommand_SetsIsSelectedOnNewItem()
    {
        var sut = CreateSut();
        var item = new CardPickerItem { CardId = 1, Title = "Family" };

        sut.SelectCardCommand.Execute(item);

        Assert.True(item.IsSelected);
    }

    [Fact]
    public void SelectCardCommand_AnnouncesSelectedCardTitle()
    {
        // The checkmark on the selected card appears via an IsSelected DataTrigger,
        // which TalkBack does not announce (#30). Announce the selection so a
        // screen-reader user gets feedback that their tap registered.
        var sut = CreateSut();
        var item = new CardPickerItem { CardId = 1, Title = "Family" };

        sut.SelectCardCommand.Execute(item);

        _accessibilityService.Received(1).Announce(Arg.Is<string>(s => s.Contains("Family")));
    }

    // ── SaveAsync existing-card path ──────────────────────────────────────

    [Fact]
    public async Task Save_ExistingCard_CreatesPrayerWithCorrectCardId_NoNewPrayerCardCreated()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Is<Prayer>(p => p.PrayerCardId == 42), false);
        await _cardService.DidNotReceive().SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Save_ExistingCard_TrimsAndNormalizesQuotes()
    {
        // Use curly quotes to verify NormalizeQuotes runs, and leading/trailing spaces for Trim
        var sut = SetupSutWithRows(("  “Mom”  ", "  heal's concern  "));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "\"Mom\"" && p.Details == "heal's concern"),
            false);
    }

    [Fact]
    public async Task Save_ExistingCard_SkipsRowsWithBlankTitle()
    {
        var sut = SetupSutWithRows(("Mom", null), ("   ", null), ("Dad", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(2).SavePrayerAsync(Arg.Any<Prayer>(), false);
    }

    [Fact]
    public async Task Save_ExistingCard_DelegatesPersistenceThroughPrayerServiceWithPublishMessageFalse()
    {
        // Replaces the old cache test: the VM now delegates to the service layer
        // which handles InvalidateCache internally. Assert the correct delegation.
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>(), false);
        await _cardService.DidNotReceive().SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Save_ExistingCard_SendsBulkChangedMessage()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bulkMessages);
    }

    [Fact]
    public async Task Save_ExistingCard_NavigatesToImportedToExistingRoute()
    {
        var sut = SetupSutWithRows(("Mom", null));
        sut.SetExistingCardModeCommand.Execute(null);
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };

        await sut.SaveCommand.ExecuteAsync(null);

        await _navigationService.Received(1).GoToAsync(Routes.PrayerCardsTabImportedToExisting(42));
        await _navigationService.DidNotReceive().GoToAsync(
            Arg.Is<string>(s => s.Contains("saved=")));
    }

    // ── Re-entrancy: in-flight LoadBoxesAsync ─────────────────────────────

    [Fact]
    public async Task LoadBoxesAsync_ReentrantWhileInFlight_DoesNotDoubleLoad()
    {
        // Race window: caller A awaits LoadBoxesAsync; mid-await on
        // GetBoxesAsync, caller B (e.g. re-entrant OnAppearing on resume)
        // calls LoadBoxesAsync. The in-flight guard keeps the second call
        // from re-issuing GetBoxesAsync and from double-populating
        // AvailableBoxes.
        var tcs = new TaskCompletionSource<IReadOnlyList<CardBox>>();
        _boxService.GetBoxesAsync().Returns(tcs.Task);
        var sut = CreateSut();

        var first = sut.LoadBoxesAsync();
        var second = sut.LoadBoxesAsync(); // re-entrant before first completes
        tcs.SetResult(new[] { new CardBox { Id = 7, Name = "Family", IsSystem = false } });
        await Task.WhenAll(first, second);

        Assert.Equal(2, sut.AvailableBoxes.Count); // Loose + Family — not 4
        await _boxService.Received(1).GetBoxesAsync();
    }

    // ── CardCollectionGroup keyed on BoxId ────────────────────────────────

    [Fact]
    public async Task LoadCardGroupsAsync_TwoBoxesWithSameName_DoesNotMerge()
    {
        // CardBox.Name uniqueness is not enforced; data drift can produce
        // two rows with the same display name. Grouping by name would
        // silently merge their card lists. Group key is BoxId.
        var c1 = new PrayerCard { Id = 1, Title = "A", BoxId = 7, IsSystem = false };
        var c2 = new PrayerCard { Id = 2, Title = "B", BoxId = 8, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { c1, c2 }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
            new CardBox { Id = 8, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(2, sut.AvailableCardGroups.Count);
        var ids = sut.AvailableCardGroups.Select(g => g.BoxId).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 7, 8 }, ids);
    }

    // ── HasNoAvailableCards change-detection guard ────────────────────────

    [Fact]
    public void HasNoAvailableCards_OnlyFiresPropertyChangedWhenValueActuallyChanges()
    {
        // Clear()+N×Add() fires N+1 CollectionChanged events on
        // AvailableCardGroups, but HasNoAvailableCards transitions at most
        // once (true → false on the first non-empty add). Without the cache,
        // the bound binding would receive a PropertyChanged storm.
        var sut = CreateSut();
        sut.SetExistingCardModeCommand.Execute(null);
        var fired = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfirmImportViewModel.HasNoAvailableCards))
                fired++;
        };

        sut.AvailableCardGroups.Add(new CardCollectionGroup
        {
            BoxId = 1, CollectionName = "A",
            Cards = { new CardPickerItem { CardId = 1, Title = "x" } }
        });
        sut.AvailableCardGroups.Add(new CardCollectionGroup
        {
            BoxId = 2, CollectionName = "B",
            Cards = { new CardPickerItem { CardId = 2, Title = "y" } }
        });
        sut.AvailableCardGroups.Add(new CardCollectionGroup
        {
            BoxId = 3, CollectionName = "C",
            Cards = { new CardPickerItem { CardId = 3, Title = "z" } }
        });

        Assert.Equal(1, fired);
    }

    // ── SelectedBox persistence across mode toggle ────────────────────────

    [Fact]
    public async Task ToggleToNewCardMode_WithRealBoxSelected_PreservesSelectedBox()
    {
        // Arrange: boxes loaded, switch to ExistingCard, pick a non-Loose box.
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        // SelectedBox is now AllCollectionsPickerItem; pick the real Family box.
        var familyBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        sut.SelectedBox = familyBox;
        Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(7, ((RealBoxPickerItem)sut.SelectedBox).BoxId);

        // Act: toggle back to NewCard mode.
        sut.SetNewCardModeCommand.Execute(null);

        // Assert: SelectedBox is still the Family box, not reset to Loose Cards.
        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(7, selected.BoxId);
    }

    // ── Dispose idempotency ───────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Page lifecycle can call Dispose() more than once via OnDisappearing
        // (and Dispose() also runs implicitly via the test harness after
        // SaveAsync/CancelAsync). Second call must not throw.
        var sut = CreateSut();

        sut.Dispose();
        var ex = Record.Exception(() => sut.Dispose());

        Assert.Null(ex);
    }

    // ── Helpers ──────────────────────────

    private ConfirmImportViewModel SetupSutWithRows(params (string Title, string? Details)[] rows)
    {
        var raw = "raw payload";
        _payloadService.ConsumePayload().Returns(raw);
        _parser.Parse(raw).Returns(Result("Imported May 1", rows));
        var sut = CreateSut();
        sut.ConsumePending();
        return sut;
    }

    // ── Accessible position re-stamp (#15) ───────────────────────────────

    [Fact]
    public void ConsumePending_StampsPositionAndTotalOnEveryRow()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null), ("Sis", null));

        Assert.Equal("Prayer title, item 1 of 3", sut.Prayers[0].TitleAccessibleDescription);
        Assert.Equal("Prayer title, item 2 of 3", sut.Prayers[1].TitleAccessibleDescription);
        Assert.Equal("Prayer title, item 3 of 3", sut.Prayers[2].TitleAccessibleDescription);
    }

    [Fact]
    public void RemovePrayer_RestampsRemainingRows()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null), ("Sis", null));

        // Remove the middle row.
        sut.RemovePrayerCommand.Execute(sut.Prayers[1]);

        Assert.Equal(2, sut.Prayers.Count);
        Assert.Equal("Prayer title, item 1 of 2", sut.Prayers[0].TitleAccessibleDescription);
        Assert.Equal("Prayer title, item 2 of 2", sut.Prayers[1].TitleAccessibleDescription);
        // Details + Remove variants re-stamp on the same trigger.
        Assert.Equal("Prayer details, item 2 of 2", sut.Prayers[1].DetailsAccessibleDescription);
        Assert.Equal("Remove prayer, item 2 of 2", sut.Prayers[1].RemoveAccessibleDescription);
    }

    [Fact]
    public void AddPrayer_RestampsAllRowsWithNewTotal()
    {
        var sut = SetupSutWithRows(("Mom", null), ("Dad", null));

        sut.AddPrayerCommand.Execute(null);

        Assert.Equal(3, sut.Prayers.Count);
        Assert.Equal("Prayer title, item 1 of 3", sut.Prayers[0].TitleAccessibleDescription);
        Assert.Equal("Prayer title, item 2 of 3", sut.Prayers[1].TitleAccessibleDescription);
        Assert.Equal("Prayer title, item 3 of 3", sut.Prayers[2].TitleAccessibleDescription);
    }

    // ── EntryMode — defaults and Manual mode ─────────────────────────────

    [Fact]
    public void EntryMode_DefaultsToImport()
    {
        // EntryMode.Import is the default so the existing import flow is
        // UNCHANGED when the VM is resolved for the import path.
        var sut = CreateSut();

        Assert.Equal(EntryMode.Import, sut.EntryMode);
    }

    [Fact]
    public void InitializeManualEntry_SeedsExactlyOneEmptyRow()
    {
        // Manual mode requires exactly one empty row ready to type — the
        // zero-tap fast path: open → type → Save → done.
        var sut = CreateSut();

        sut.InitializeManualEntry();

        Assert.Single(sut.Prayers);
        Assert.Equal(string.Empty, sut.Prayers[0].Title);
    }

    [Fact]
    public void InitializeManualEntry_SetsExistingCardMode()
    {
        // Manual entry defaults to ExistingCard mode with the Quick Add card
        // preselected — the user doesn't need to pick or name a card.
        var sut = CreateSut();

        sut.InitializeManualEntry();

        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
    }

    [Fact]
    public void InitializeManualEntry_SetsEntryModeToManual()
    {
        var sut = CreateSut();

        sut.InitializeManualEntry();

        Assert.Equal(EntryMode.Manual, sut.EntryMode);
    }

    [Fact]
    public void InitializeManualEntry_DoesNotConsumePendingPayload()
    {
        // Manual entry must NOT touch the import payload channels — that would
        // drain a legitimately staged import sitting in the queue.
        var sut = CreateSut();

        sut.InitializeManualEntry();

        _payloadService.DidNotReceive().ConsumePayload();
        _payloadService.DidNotReceive().ConsumeStructured();
    }

    [Fact]
    public void InitializeManualEntry_DoesNotFireNormalLoadCardGroupsAsync()
    {
        // InitializeManualEntry sets ImportMode = ExistingCard, whose setter
        // normally calls LoadCardGroupsAsync (which calls GetCardsAsync and
        // excludes system cards — it would exclude the Quick Add card).
        // In Manual mode that side-effect must be suppressed so
        // LoadManualCardGroupsAsync remains the sole authoritative loader.
        // Evidence: GetCardsAsync is NOT called during InitializeManualEntry
        // (it is only called later, explicitly, from LoadManualCardGroupsAsync).
        var sut = CreateSut();

        sut.InitializeManualEntry();

        // GetCardsAsync is the distinguishing call of LoadCardGroupsAsync;
        // it must NOT be invoked during initialization.
        _cardService.DidNotReceive().GetCardsAsync();
    }

    [Fact]
    public async Task Manual_LooseCardsHasQuickAddCard_PreselectsIt()
    {
        // Edge case: if the Quick Add card itself lives in Loose Cards (BoxId 0)
        // — e.g. a fresh DB where no System box exists yet, so GetOrCreateSystemCardAsync
        // falls back to BoxId 0 — then with Loose Cards as the default selection it
        // is the only card present and gets preselected (existing-card fast path).
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        Assert.NotNull(sut.SelectedCard);
        Assert.Equal(99, sut.SelectedCard!.CardId);
    }

    [Fact]
    public async Task Manual_Save_WritesIsImportedFalse()
    {
        // Prayers added via Manual mode are NOT flagged as imported — they are
        // user-authored, not ingested from an external source. Quick Add card at
        // BoxId 0 so it sits in the default Loose Cards view and is preselected.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();
        sut.Prayers[0].Title = "Trust God";

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.IsImported == false && p.PrayerCardId == 99),
            false);
    }

    [Fact]
    public async Task Manual_Save_EmptyRow_DoesNotSave()
    {
        // Validation: an empty single row must block save — matches prior
        // QuickAdd validation (empty title → no save).
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add" };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();
        // Row is empty (default seeded by InitializeManualEntry)

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Manual_Save_NewCardMode_IsImportedFalse()
    {
        // When user switches to New Card in Manual mode, the created card AND
        // its prayers must still have IsImported = false.
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            Array.Empty<PrayerCard>()));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();

        // Switch to New Card mode and fill in the form
        sut.SetNewCardModeCommand.Execute(null);
        sut.CardTitle = "My New Card";
        sut.Prayers.Add(new EditablePrayer { Title = "Healing" });

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.IsImported == false),
            false);
        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.IsImported == false),
            false);
    }

    [Fact]
    public async Task Import_Save_IsImportedTrue_RegressionTest()
    {
        // Import path regression: prayers saved via ConsumePending + Save must
        // still carry IsImported = true. EntryMode.Import is the default.
        var sut = SetupSutWithRows(("Mom", null));
        // sut is in NewCard / Import mode with CardTitle "Imported May 1"

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.IsImported == true),
            false);
        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.IsImported == true),
            false);
    }

    // ── Manual mode — Loose Cards default (#122) ─────────────────────────

    [Fact]
    public async Task Manual_AfterLoadBoxesAndCards_SelectedBoxIsLooseCards()
    {
        // #122: After InitializeManualEntry + LoadBoxesAsync + LoadManualCardGroupsAsync,
        // SelectedBox must be the Loose Cards RealBoxPickerItem (BoxId 0), NOT the
        // All-collections sentinel — Quick Add opens defaulted to Loose Cards so a
        // new prayer lands there by default.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selected.BoxId);
    }

    [Fact]
    public async Task Manual_LooseCardsHasUserCards_StaysExistingCardMode_ShowingLooseCards()
    {
        // #122 + #119: when Loose Cards (the default selection) already holds
        // existing cards, the page stays in ExistingCard mode and shows them —
        // no flip to NewCard. The Quick Add system card lives in the System box
        // (BoxId != 0) on-device, so it is correctly filtered out of the Loose
        // Cards view (modelled here with BoxId 5).
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 5 };
        var looseCard = new PrayerCard { Id = 1, Title = "Stray", BoxId = 0, IsSystem = false };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard, looseCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
        var allCards = sut.AvailableCardGroups.SelectMany(g => g.Cards).ToList();
        Assert.Contains(allCards, c => c.CardId == 1);   // loose card visible
        Assert.DoesNotContain(allCards, c => c.CardId == 99); // Quick Add filtered out
    }

    [Fact]
    public async Task Manual_LooseCardsEmpty_FlipsToNewCardMode_WithLooseCardsSelected()
    {
        // #122 + #119: a brand-new user with no loose cards opens Quick Add →
        // Loose Cards is the default but resolves to an empty card list →
        // #119's flip to NewCard fires so the Card Title field is shown and the
        // user can type straight into a new card. SelectedBox stays Loose Cards
        // (the new card lands in BoxId 0). The Quick Add card lives in the System
        // box (BoxId 5), so it is filtered out of the empty Loose Cards view.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 5 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        Assert.Equal(ImportMode.NewCard, sut.ImportMode);
        var selected = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selected.BoxId);
    }

    [Fact]
    public async Task Manual_LooseCardsEmpty_Save_CreatesLooseCardWithDerivedTitle()
    {
        // #122 end-to-end: empty Loose Cards → flip to NewCard → user types only a
        // prayer title → Save creates exactly one card in Loose Cards (BoxId 0)
        // with the title derived from the prayer, IsImported = false.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 5 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>())
            .Returns(ci =>
            {
                ((PrayerCard)ci[0]).Id = 123;
                return Task.FromResult((PrayerCard)ci[0]);
            });
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        sut.Prayers[0].Title = "Trust God";

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.BoxId == 0 && c.Title == "Trust God" && c.IsImported == false),
            false);
        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Trust God" && p.PrayerCardId == 123 && p.IsImported == false),
            false);
    }

    [Fact]
    public async Task Manual_ChangingSelectedBox_RefiltersCardGroups()
    {
        // In Manual mode, changing SelectedBox to a specific collection re-filters
        // the card list — same behavior as import Existing mode.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        var userCard = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard, userCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        // Switch to the Family collection — should show only Alpha, not Quick Add
        var familyBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        sut.SelectedBox = familyBox;
        await Task.Delay(50); // let SafeFireAndForget complete

        var allCards = sut.AvailableCardGroups.SelectMany(g => g.Cards).ToList();
        Assert.DoesNotContain(allCards, c => c.CardId == 99); // Quick Add excluded from Family filter
        Assert.Contains(allCards, c => c.CardId == 1);        // Alpha present
    }

    // ── Manual mode — SelectedBox preselection race (#171) ───────────────

    [Fact]
    public async Task Manual_LooseCardsHasCards_PreselectsCard_StaysExistingCardMode()
    {
        // #171 (designed-preselection invariant): when the default real box
        // (Loose Cards, BoxId 0) holds a card the loader can preselect (the
        // Quick Add card, modelled here in BoxId 0), the authoritative Manual
        // load preselects it, stays in ExistingCard mode, and the grouped list
        // is non-empty — no spurious flip to NewCard on a populated collection.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        var looseCard = new PrayerCard { Id = 1, Title = "Stray", BoxId = 0, IsSystem = false };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard, looseCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        var sut = CreateSut();

        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        Assert.NotNull(sut.SelectedCard);                       // a card IS preselected
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);  // no spurious flip
        Assert.NotEmpty(sut.AvailableCardGroups.SelectMany(g => g.Cards));
    }

    [Fact]
    public async Task Manual_SelectedBoxPushedDuringAuthoritativeLoad_SuppressesRacingReload()
    {
        // #171 (root-cause repro, red→green): on-device the Picker's two-way
        // binding can push SelectedBox WHILE LoadManualCardGroupsAsync is mid
        // flight. Before the fix, the setter's un-awaited LoadCardGroupsAsync
        // interleaves with the authoritative load: a transient Clear() empties
        // AvailableCardGroups and trips FlipToNewCardIfEmptyManualRealBox on a
        // collection that actually has cards — flipping to NewCard. The fix
        // gates the setter while the authoritative loader owns the population,
        // so preselection survives and no racing reload fires.
        //
        // Determinism: the mocked services complete synchronously, so the
        // "race" is forced by firing the setter from inside the awaited
        // GetCardsAsync of the authoritative load (one-shot), mimicking the
        // Picker pushing null then restoring Loose Cards.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        var looseCard = new PrayerCard { Id = 1, Title = "Stray", BoxId = 0, IsSystem = false };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));

        ConfirmImportViewModel sut = null!;
        var injected = false;
        _cardService.GetCardsAsync().Returns(_ =>
        {
            // Fire the Picker-style SelectedBox push exactly once, during the
            // authoritative load. null differs from Loose Cards (Equals is by
            // BoxId) so the setter's SetProperty returns true and — pre-fix —
            // issues a racing reload; restoring Loose Cards mirrors the binding
            // settling back to the authoritative selection.
            if (!injected && sut is not null)
            {
                injected = true;
                var loose = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 0);
                sut.SelectedBox = null;
                sut.SelectedBox = loose;
            }
            return Task.FromResult<IReadOnlyList<PrayerCard>>(new[] { quickAddCard, looseCard });
        });

        sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        // The authoritative loader is the SINGLE card-group load; the setter's
        // racing reload was suppressed (each LoadCardGroupsAsync fetches once).
        await _cardService.Received(1).GetCardsAsync();
        // Preselection survived and no spurious flip occurred.
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
        Assert.NotNull(sut.SelectedCard);
        Assert.NotEmpty(sut.AvailableCardGroups.SelectMany(g => g.Cards));
    }

    [Fact]
    public async Task Import_LoadCardGroups_ExcludesSystemCards_StillTrueAfterManualRefactor()
    {
        // Regression: the refactor that makes LoadCardGroupsAsync Manual-aware
        // must not change import-mode behavior — system cards must still be excluded.
        var user = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        var system = new PrayerCard { Id = 2, Title = "QuickAdd", BoxId = 0, IsSystem = true };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { user, system }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut(); // Import mode (default)
        await sut.LoadBoxesAsync();

        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        var allCards = sut.AvailableCardGroups.SelectMany(g => g.Cards).ToList();
        Assert.DoesNotContain(allCards, c => c.CardId == 2); // System card excluded in import mode
        Assert.Single(allCards);
        Assert.Equal(1, allCards[0].CardId);
    }

    // ── CanSave re-evaluated on Title change (Manual mode bug fix) ────────

    [Fact]
    public void CanSave_Manual_TypingTitleIntoEmptyRow_FiresCanExecuteChanged()
    {
        // Regression: CanSave was only re-evaluated on Prayers.CollectionChanged
        // (rows added/removed). In Manual mode the page starts with ONE empty row
        // seeded by InitializeManualEntry. The user types a title — CollectionChanged
        // never fires — so the XAML button stayed disabled because
        // NotifyCanExecuteChanged was never called.
        // This test proves the fix: mutating Title on an existing row triggers
        // CanExecuteChanged WITHOUT any collection mutation (no Add, no Remove).
        var sut = CreateSut();
        sut.InitializeManualEntry();
        // Simulate the Quick Add card having been preselected (LoadManualCardGroupsAsync
        // would do this on-device; set it directly so the test is synchronous).
        sut.SelectedCard = new CardPickerItem { CardId = 99, Title = "Quick Add" };

        // Arm the listener AFTER setup so we only count changes caused by typing.
        var canExecuteChangedCount = 0;
        sut.SaveCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        // Type a title directly into the existing row — NO Add/Remove.
        sut.Prayers[0].Title = "x";

        // NotifyCanExecuteChanged must have fired at least once (the UI button
        // needs this signal to re-query CanExecute and flip to enabled).
        Assert.True(canExecuteChangedCount > 0,
            "CanExecuteChanged should fire when an existing row's Title changes");

        // And CanSave must now evaluate to true (belt-and-suspenders).
        Assert.True(sut.SaveCommand.CanExecute(null));
    }

    // ── Quick Add: empty collection falls back to New-card save (#119) ─────

    [Fact]
    public async Task Manual_SelectingEmptyCollection_FlipsToNewCardMode()
    {
        // #119: In Quick Add, selecting a collection that has no existing cards
        // must not dead-end Save. The VM falls back to New-card mode so the
        // prayer can save into a freshly created card in that collection.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        // Quick Add lives in Loose Cards (BoxId 0); the "Family" collection (7)
        // has NO cards — selecting it resolves to an empty card list.
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        // Select the empty Family collection.
        var familyBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        sut.SelectedBox = familyBox;
        await Task.Delay(50); // let SafeFireAndForget LoadCardGroupsAsync complete

        Assert.Equal(ImportMode.NewCard, sut.ImportMode);
    }

    [Fact]
    public async Task Manual_EmptyCollection_WithPrayerTitle_CanSave()
    {
        // #119: a non-empty prayer title is sufficient to save into an empty
        // collection — CardTitle is auto-derived, so Save is never blocked on
        // a card name.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        var familyBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        sut.SelectedBox = familyBox;
        await Task.Delay(50);

        // User types only a prayer title (no card title).
        sut.Prayers[0].Title = "Trust God";

        Assert.True(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Manual_EmptyCollection_Save_CreatesOneCardInCollectionWithDerivedTitle()
    {
        // #119: saving into an empty collection creates exactly ONE new card in
        // that collection (BoxId = selected box) whose title is derived from the
        // first prayer, and the prayer is attached to it.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 0 };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        // Assign an Id to the freshly created card so the prayer can reference it.
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>())
            .Returns(ci =>
            {
                ((PrayerCard)ci[0]).Id = 123;
                return Task.FromResult((PrayerCard)ci[0]);
            });
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        var familyBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        sut.SelectedBox = familyBox;
        await Task.Delay(50);

        sut.Prayers[0].Title = "Trust God";

        await sut.SaveCommand.ExecuteAsync(null);

        // Exactly one new card, in the selected collection, titled from the prayer.
        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.BoxId == 7 && c.Title == "Trust God" && c.IsImported == false),
            false);
        // The prayer is attached to that new card.
        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Trust God" && p.PrayerCardId == 123 && p.IsImported == false),
            false);
    }

    [Fact]
    public async Task Import_EmptyCollection_DoesNotFlip_AndStillRequiresCardTitle()
    {
        // #119 negative guard: the empty-collection → New-card fallback is gated
        // to Manual entry. In the default Import flow, selecting an empty real
        // collection must NOT auto-flip to New-card — and New-card's CardTitle
        // requirement must still hold (a prayer title alone is not enough, unlike
        // Quick Add where CardTitle is auto-derived). This is the invariant the
        // whole fix relies on but otherwise leaves untested.
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            Array.Empty<PrayerCard>()));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = SetupSutWithRows(("Trust God", null)); // EntryMode defaults to Import
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);

        // Select the empty Family collection (no cards).
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        // The Manual-only fallback must not fire in the Import flow.
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);

        // And New-card mode still requires a non-empty CardTitle — a prayer title
        // alone (blank CardTitle) cannot save.
        sut.SetNewCardModeCommand.Execute(null);
        sut.CardTitle = "   ";

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    // ── Quick Add: ExistingCard with no card selected falls back to New-card (#168) ──

    [Fact]
    public void CanSave_Manual_ExistingCardMode_NullSelectedCard_WithPrayerTitle_ReturnsTrue()
    {
        // #168 repro: Quick Add (Manual) opens in ExistingCard mode. When the
        // card-preselection race leaves SelectedCard null while real cards exist
        // (so #119 does NOT flip to NewCard), the old gate (SelectedCard is not
        // null) disabled Save → dead-end. Manual ExistingCard with a typed prayer
        // must allow Save; SaveAsync falls through to create a new card. Gated to
        // Manual the same way as #119's NewCard branch — Import still requires a
        // chosen card (see the Import guard below).
        var sut = CreateSut();
        sut.InitializeManualEntry();        // EntryMode.Manual, ImportMode.ExistingCard
        sut.Prayers[0].Title = "Trust God"; // SelectedCard stays null

        Assert.Null(sut.SelectedCard);
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
        Assert.True(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_Manual_ExistingCardMode_NullSelectedCard_CreatesNewCardInSelectedBox_AndNavigates()
    {
        // #168 end-to-end: a real card exists in Loose Cards (so the page stays in
        // ExistingCard mode — no #119 flip), but the Quick Add card lives in the
        // System box and is filtered out, so nothing is preselected → SelectedCard
        // is null. Saving must create ONE new card in the selected box (Loose
        // Cards, BoxId 0) with the title derived from the prayer, attach the
        // prayer (IsImported = false), and navigate to the Cards tab — no dead-end.
        var quickAddCard = new PrayerCard { Id = 99, Title = "Quick Add", IsSystem = true, SystemKey = "quick_add", BoxId = 5 };
        var looseCard = new PrayerCard { Id = 1, Title = "Stray", BoxId = 0, IsSystem = false };
        _cardService.GetOrCreateQuickAddCardAsync().Returns(Task.FromResult(quickAddCard));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(
            new[] { quickAddCard, looseCard }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(
            Array.Empty<CardBox>()));
        _cardService.SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>())
            .Returns(ci =>
            {
                ((PrayerCard)ci[0]).Id = 123;
                return Task.FromResult((PrayerCard)ci[0]);
            });
        var sut = CreateSut();
        sut.InitializeManualEntry();
        await sut.LoadBoxesAsync();
        await sut.LoadManualCardGroupsAsync();

        // Repro precondition: ExistingCard mode, real Loose Cards box selected,
        // a user card visible (no flip), but nothing preselected.
        Assert.Equal(ImportMode.ExistingCard, sut.ImportMode);
        Assert.Null(sut.SelectedCard);
        var selectedBox = Assert.IsType<RealBoxPickerItem>(sut.SelectedBox);
        Assert.Equal(0, selectedBox.BoxId);

        sut.Prayers[0].Title = "Trust God";
        Assert.True(sut.SaveCommand.CanExecute(null)); // RED before the #168 fix

        await sut.SaveCommand.ExecuteAsync(null);

        await _cardService.Received(1).SaveCardAsync(
            Arg.Is<PrayerCard>(c => c.BoxId == 0 && c.Title == "Trust God" && c.IsImported == false),
            false);
        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.Title == "Trust God" && p.PrayerCardId == 123 && p.IsImported == false),
            false);
        await _navigationService.Received(1).GoToAsync(Routes.PrayerCardsTabImported(123));
    }

    [Fact]
    public async Task Save_Manual_ExistingCardMode_WithSelectedCard_SavesIntoThatCard_NoNewCard()
    {
        // #168 positive guard: when a card IS selected in Manual ExistingCard
        // mode, the relaxation must not change behavior — the prayer saves into
        // the selected card and no new card is created.
        var sut = CreateSut();
        sut.InitializeManualEntry();
        sut.SelectedCard = new CardPickerItem { CardId = 42, Title = "Family" };
        sut.Prayers[0].Title = "Trust God";

        Assert.True(sut.SaveCommand.CanExecute(null));

        await sut.SaveCommand.ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(
            Arg.Is<Prayer>(p => p.PrayerCardId == 42 && p.IsImported == false),
            false);
        await _cardService.DidNotReceive().SaveCardAsync(Arg.Any<PrayerCard>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CanSave_ImportMode_ExistingCard_RealBoxWithCards_NullSelectedCard_StillFalse_Issue168Guard()
    {
        // #168 negative guard: the CanSave relaxation is Manual-only (gated the
        // same way as #119/#122's FlipToNewCardIfEmptyManualRealBox). In the
        // default Import flow — even with a real collection selected that HAS
        // cards — an ExistingCard save with no card chosen must STILL be disabled.
        // The fix must not let Import silently create a new card.
        var alpha = new PrayerCard { Id = 1, Title = "Alpha", BoxId = 7, IsSystem = false };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new[] { alpha }));
        _boxService.GetBoxesAsync().Returns(Task.FromResult<IReadOnlyList<CardBox>>(new[]
        {
            new CardBox { Id = 7, Name = "Family", IsSystem = false },
        }));
        var sut = SetupSutWithRows(("Mom", null)); // EntryMode defaults to Import
        await sut.LoadBoxesAsync();
        sut.SetExistingCardModeCommand.Execute(null);
        await Task.Delay(50);
        sut.SelectedBox = sut.AvailableBoxes.OfType<RealBoxPickerItem>().First(b => b.BoxId == 7);
        await Task.Delay(50);

        Assert.Equal(EntryMode.Import, sut.EntryMode);
        Assert.Null(sut.SelectedCard);
        Assert.False(sut.SaveCommand.CanExecute(null));
    }
}
