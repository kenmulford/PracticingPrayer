using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class PrayerRequestDetailViewModelTests
{
    private readonly IPrayerService _prayerService = Substitute.For<IPrayerService>();
    private readonly ITagService _tagService = Substitute.For<ITagService>();
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly IOnboardingService _onboardingService = Substitute.For<IOnboardingService>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly ISettings _settings = Substitute.For<ISettings>();
    private readonly IDBService _db = Substitute.For<IDBService>();
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    // Issue #255: gates ShareCommand on effective-protection + lock state. Fake mirrors
    // the #251/#253/#254 seam-faking approach — defaults to locked (IsSessionUnlocked=false)
    // so existing tests exercise the unprotected path unless a card explicitly sets
    // ProtectionMode.
    private readonly IConfidentialAccessService _confidentialAccessService = Substitute.For<IConfidentialAccessService>();

    public PrayerRequestDetailViewModelTests()
    {
        Prayer.SetDBService(_db);
        PrayerTag.SetDBService(_db);
        PrayerCard.SetDBService(_db);
        PrayerCardTag.SetDBService(_db);
        _settings.DefaultNotifyHour.Returns(9);
        _settings.DefaultNotifyMinute.Returns(0);
        _boxService.GetBoxesAsync().Returns(new List<CardBox>().AsReadOnly());
    }

    private PrayerRequestDetailViewModel CreateSut() =>
        new(_prayerService, _tagService, _cardService, _onboardingService,
            _notificationService, _navigationService, _accessibilityService, _settings,
            _boxService, _confidentialAccessService);

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_IsNew()
    {
        var sut = CreateSut();
        Assert.True(sut.IsNew);
        // IsDirty may be true if CanNotify setter fires during construction
        // due to mock setup — just verify IsNew is correct
    }

    // ── IsDirty — basic fields ────────────────────────────────────────

    [Fact]
    public void IsDirty_TitleChange()
    {
        var sut = CreateSut();
        sut.Title = "New prayer";
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void IsDirty_DetailsChange()
    {
        var sut = CreateSut();
        sut.Details = "Some details";
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void IsDirty_CanNotifyChange()
    {
        var sut = CreateSut();
        sut.CanNotify = true;
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void IsDirty_FrequencyChange()
    {
        var sut = CreateSut();
        sut.PrayerFrequency = PrayerFrequency.Weekly;
        Assert.True(sut.IsDirty);
    }

    // ── IsDirty — notification time fields (audit fix #22c) ──────────

    [Fact]
    public void IsDirty_NotifyTimeChange()
    {
        var sut = CreateSut();
        sut.NotifyTime = new TimeSpan(14, 30, 0); // change from default 9:00
        Assert.True(sut.IsDirty);
    }

    // ── IsDirty — editable answered date (issue #108) ────────────────

    [Fact]
    public async Task IsDirty_AnsweredAtChange()
    {
        // Establish an ANSWERED baseline first: mark answered + save so CaptureOriginals
        // runs and the answered state (including its date) becomes the captured baseline.
        // Then change ONLY the date — so the new AnsweredAt != _originalAnsweredAt clause
        // is the SOLE thing that can flip IsDirty (IsAnswered is unchanged from baseline).
        var sut = CreateSut();
        sut.Title = "Answered prayer";
        sut.IsAnswered = true;
        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        Assert.False(sut.IsDirty); // clean baseline, answered captured

        sut.AnsweredAt = DateTime.Today.AddDays(-3);

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void AnsweredAt_Set_DoesNotChangeIsAnswered()
    {
        var sut = CreateSut();
        sut.IsAnswered = true;
        sut.AnsweredAt = DateTime.Today.AddDays(-5);
        Assert.True(sut.IsAnswered); // editing the date must not flip status
    }

    [Fact]
    public void Unanswering_ClearsAnsweredAtToNull()
    {
        var sut = CreateSut();
        sut.IsAnswered = true;
        sut.AnsweredAt = DateTime.Today.AddDays(-5);

        sut.IsAnswered = false;

        Assert.Null(sut.AnsweredAt);
    }

    [Fact]
    public async Task AnsweredAt_Edit_PersistsThroughSave()
    {
        var edited = DateTime.Today.AddDays(-7);
        Prayer? saved = null;
        _prayerService.SavePrayerAsync(Arg.Do<Prayer>(p => saved = p))
            .Returns(call => Task.FromResult((Prayer)call[0]));

        var sut = CreateSut();
        sut.Title = "Answered prayer";
        sut.IsAnswered = true;
        sut.AnsweredAt = edited;

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(edited, saved!.AnsweredAt);
        // Originals are recaptured after save, so the edited value is now the baseline.
        Assert.False(sut.IsDirty);
    }

    [Fact]
    public async Task AnsweredAt_DatePickerRoundTrip_NotSpuriouslyDirty()
    {
        // Models the production risk the review caught: mark-answered writes a date-only
        // default, save captures it as the baseline, then the DatePicker round-trips the
        // value back as a date-only (midnight) DateTime — exactly what the picker emits.
        // The form must NOT report dirty, and the date must survive intact. Pre-fix this
        // failed because mark-answered wrote DateTime.Now (time included) while the picker
        // wrote midnight, so the baseline and the writeback never compared equal.
        var sut = CreateSut();
        sut.Title = "Answered prayer";
        sut.IsAnswered = true; // default answered-on value
        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        Assert.False(sut.IsDirty);

        var roundTripped = sut.AnsweredAt;          // what the picker reads
        sut.AnsweredAt = roundTripped!.Value.Date;  // what the picker writes back (midnight)

        Assert.False(sut.IsDirty);                  // no spurious dirty
        Assert.Equal(DateTime.Today, sut.AnsweredAt); // date preserved, no drift
    }

    [Fact]
    public async Task AnsweredAt_TimeBearingStoredValue_LoadThenRoundTrip_NotSpuriouslyDirty()
    {
        // Cross-flow regression: a prayer answered during Prayer Time (PrayerTimeViewModel
        // writes DateTime.Now, time included) or a legacy row answered before this fix is
        // stored with a time component and round-trips that time intact on reload. When the
        // detail page loads it, CaptureOriginals captures the baseline via the VM getter; the
        // getter MUST normalize to date-only or the time-bearing baseline (14:00) won't equal
        // the picker's midnight writeback → spurious dirty + silent time loss.
        // This FAILS with a raw getter (get => _prayer.AnsweredAt) and PASSES with ?.Date.
        var stored = DateTime.Today.AddHours(14); // simulates a Prayer-Time / legacy value
        _db.GetByIdAsync<Prayer>(42).Returns(Task.FromResult(
            new Prayer { Id = 42, Title = "Answered in Prayer Time", IsAnswered = true, AnsweredAt = stored }));
        _prayerService.GetInteractionCountByPrayerAsync(42).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(42).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        var sut = CreateSut();
        // Drive the real load → CaptureOriginals path (edit mode so the picker is live).
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "42", ["edit"] = "true" });

        // Drain the SafeFireAndForget load chain.
        for (int i = 0; i < 20 && sut.AnsweredAt is null; i++)
            await Task.Yield();
        Assert.False(sut.IsDirty); // clean baseline straight off the time-bearing load

        var roundTripped = sut.AnsweredAt;          // what the picker reads (already date-only)
        sut.AnsweredAt = roundTripped!.Value.Date;  // what the picker writes back (midnight)

        Assert.False(sut.IsDirty);                  // no spurious dirty from the stored time
        Assert.Equal(DateTime.Today, sut.AnsweredAt); // time dropped, date preserved
    }

    // ── CanLeaveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CanLeaveAsync_AfterSave_ReturnsTrue()
    {
        // After saving, originals are captured so IsDirty is false
        var sut = CreateSut();
        sut.Title = "Test";
        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        Assert.True(await sut.CanLeaveAsync());
    }

    [Fact]
    public async Task CanLeaveAsync_Dirty_PromptsUser()
    {
        var sut = CreateSut();
        sut.Title = "Modified";
        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var result = await sut.CanLeaveAsync();

        Assert.True(result);
        await _navigationService.Received(1).DisplayConfirmAsync(
            "Unsaved Changes", Arg.Any<string>(), "Discard", "Cancel");
    }

    // ── SaveCommand ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveCommand_SavesPrayerAndAnnounces()
    {
        var sut = CreateSut();
        sut.Title = "Test";

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>());
        _accessibilityService.Received(1).Announce("Prayer saved");
    }

    [Fact]
    public async Task SaveCommand_NewPrayer_AdvancesOnboarding()
    {
        var sut = CreateSut();
        sut.Title = "First prayer";

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        _onboardingService.Received(1).Advance();
    }

    [Fact]
    public async Task SaveCommand_WithNotifications_Schedules()
    {
        var sut = CreateSut();
        sut.Title = "Reminder prayer";
        sut.CanNotify = true;

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _notificationService.Received(1).ScheduleAsync(Arg.Any<Prayer>());
    }

    [Fact]
    public async Task SaveCommand_WithoutNotifications_Cancels()
    {
        var sut = CreateSut();
        sut.Title = "No reminder";
        sut.CanNotify = false;

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _notificationService.Received(1).CancelAsync(Arg.Any<int>(), Arg.Any<PrayerFrequency>());
    }

    // ── DeleteCommand ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCommand_Confirmed_DeletesAndAnnounces()
    {
        var sut = CreateSut();
        // Give it an ID so it's not "new"
        sut.Title = "To delete";
        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        await ((IAsyncRelayCommand)sut.DeleteCommand).ExecuteAsync(null);

        await _prayerService.Received(1).DeletePrayerAsync(Arg.Any<Prayer>());
        _accessibilityService.Received(1).Announce("Prayer deleted");
    }

    [Fact]
    public async Task DeleteCommand_Cancelled_DoesNotDelete()
    {
        var sut = CreateSut();
        _navigationService.DisplayConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        await ((IAsyncRelayCommand)sut.DeleteCommand).ExecuteAsync(null);

        await _prayerService.DidNotReceive().DeletePrayerAsync(Arg.Any<Prayer>());
    }

    // ── SaveAndNewCommand ─────────────────────────────────────────────

    [Fact]
    public async Task SaveAndNewCommand_EmptyTitle_ShowsAlert()
    {
        var sut = CreateSut();
        sut.Title = "";

        await ((IAsyncRelayCommand)sut.SaveAndNewCommand).ExecuteAsync(null);

        await _navigationService.Received(1).DisplayAlertAsync("Required", Arg.Any<string>(), "OK");
    }

    [Fact]
    public async Task SaveAndNewCommand_ValidTitle_SavesAndResetsForm()
    {
        var sut = CreateSut();
        sut.Title = "First prayer";
        sut.ReturnToCards = true;

        await ((IAsyncRelayCommand)sut.SaveAndNewCommand).ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>());
        // FormResetRequested event was replaced by PendingFocusTitle property
        // (see SaveAndNewCommand_ValidTitle_SetsPendingFocusTitleTrue for the explicit
        // assertion); here we only confirm the form-reset side effects.
        Assert.True(sut.IsNew); // form was reset
    }

    // ── ShowSaveAndNew ────────────────────────────────────────────────

    [Fact]
    public void ShowSaveAndNew_NewAndReturnToCards_True()
    {
        var sut = CreateSut();
        sut.ReturnToCards = true;

        Assert.True(sut.ShowSaveAndNew);
    }

    [Fact]
    public void ShowSaveAndNew_NotReturnToCards_False()
    {
        var sut = CreateSut();
        sut.ReturnToCards = false;

        Assert.False(sut.ShowSaveAndNew);
    }

    // ── CanNotify triggers permission request ─────────────────────────

    [Fact]
    public void CanNotify_Enable_RequestsPermissionWhenAllowed()
    {
        _settings.AllowNotifications.Returns(true);
        var sut = CreateSut();

        sut.CanNotify = true;

        _notificationService.Received(1).RequestPermissionAsync();
    }

    [Fact]
    public void CanNotify_Enable_SkipsPermissionWhenDisallowed()
    {
        _settings.AllowNotifications.Returns(false);
        var sut = CreateSut();

        sut.CanNotify = true;

        _notificationService.DidNotReceive().RequestPermissionAsync();
    }

    // ── Reload guard ─────────────────────────────────────────────────

    [Fact]
    public void Reload_NewPrayer_DoesNotNavigateAway()
    {
        var sut = CreateSut();

        sut.Reload();

        _navigationService.DidNotReceive().GoToAsync(Arg.Any<string>());
    }

    // ── ApplyQueryAttributes re-entry guard ──────────────────────────

    [Fact]
    public void ApplyQueryAttributes_CalledTwice_DoesNotResetPrayer()
    {
        var sut = CreateSut();
        var query = new Dictionary<string, object> { ["new"] = "true" };

        ((IQueryAttributable)sut).ApplyQueryAttributes(query);
        sut.Title = "My Prayer";

        // Simulate Shell re-applying sticky params on modal dismiss
        ((IQueryAttributable)sut).ApplyQueryAttributes(query);

        Assert.Equal("My Prayer", sut.Title);
    }

    // ── PendingFocusTitle (replaces FormResetRequested event) ──
    // Same defect family as the card-create crash: a C# event whose handler
    // touched native UI state. Replaced with an observable property the View
    // consumes via PropertyChanged on the lifecycle channel.

    [Fact]
    public async Task SaveAndNewCommand_ValidTitle_SetsPendingFocusTitleTrue()
    {
        var sut = CreateSut();
        sut.Title = "First prayer";
        sut.ReturnToCards = true;
        Assert.False(sut.PendingFocusTitle);

        await ((IAsyncRelayCommand)sut.SaveAndNewCommand).ExecuteAsync(null);

        Assert.True(sut.PendingFocusTitle);
        Assert.True(sut.IsNew); // form was reset
    }

    [Fact]
    public async Task ConsumePendingFocusTitle_WhenTrue_ClearsFlag()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        sut.ReturnToCards = true;
        await ((IAsyncRelayCommand)sut.SaveAndNewCommand).ExecuteAsync(null);
        Assert.True(sut.PendingFocusTitle);

        sut.ConsumePendingFocusTitle();

        Assert.False(sut.PendingFocusTitle);
    }

    [Fact]
    public void ConsumePendingFocusTitle_WhenFalse_StaysFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.PendingFocusTitle);

        sut.ConsumePendingFocusTitle();

        Assert.False(sut.PendingFocusTitle);
    }

    [Fact]
    public void FormResetRequested_EventIsRemoved()
    {
        // Reflection guard tripwire: the VM→View C# event was the same defect family
        // as the card-create crash. Don't reintroduce.
        var evt = typeof(PrayerRequestDetailViewModel).GetEvent("FormResetRequested");
        Assert.Null(evt);
    }

    // ── IsBusy on Save ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveCommand_SetsIsBusyDuringExecution_AndResetsAfter()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        bool capturedIsBusy = false;
        _prayerService.SavePrayerAsync(Arg.Any<Prayer>()).Returns(call =>
        {
            capturedIsBusy = sut.IsBusy;
            return Task.FromResult((Prayer)call[0]);
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
        _prayerService.SavePrayerAsync(Arg.Any<Prayer>())
            .Returns(_ => Task.FromException<Prayer>(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null));

        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task SaveCommand_DoubleInvoke_RunsServiceOnlyOnce()
    {
        var sut = CreateSut();
        sut.Title = "Test";
        var tcs = new TaskCompletionSource<Prayer>();
        _prayerService.SavePrayerAsync(Arg.Any<Prayer>()).Returns(tcs.Task);

        var first = ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        var second = ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);
        tcs.SetResult(new Prayer());
        await Task.WhenAll(first, second);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>());
    }

    // ── EditPrayerCommand ─────────────────────────────────────────────
    // Contract pin for #72a: PrayerListTests' Prayers_EditPrayer drives the view
    // out of read-only via Edit, then asserts the editable surface. This locks the
    // VM-side contract that UITest depends on, so a regression shows up as a fast
    // unit failure instead of a flaky UITest red.

    [Fact]
    public void EditPrayerCommand_FlipsToEditMode_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        sut.IsReadOnly = true; // opened view-only (load + viewOnly)
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.EditPrayerCommand.Execute(null);

        Assert.False(sut.IsReadOnly);
        Assert.True(sut.IsEditable);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.IsReadOnly), raised);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.IsEditable), raised);
    }

    // ── PrayedSummary ─────────────────────────────────────────────────
    // PrayedSummary is driven by LoadPrayerAsync (called via ApplyQueryAttributes "load").
    // Each test drives the real load path so a regression in the wiring (e.g. removing
    // the GetInteractionCountByPrayerAsync call) would turn these red.

    [Fact]
    public async Task PrayedSummary_TwelveTimes_FormatsCorrectly()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(5)
            .Returns(Task.FromResult(new Prayer { Id = 5, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(5).Returns(Task.FromResult(12));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(5).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "5", ["viewOnly"] = "true" });

        // Drain the SafeFireAndForget chain (multiple nested awaits) with a bounded poll.
        for (int i = 0; i < 20 && !sut.PrayedSummary.Contains("Mar 3, 2026"); i++)
            await Task.Yield();

        Assert.Equal("Prayed for 12 times since Mar 3, 2026", sut.PrayedSummary);
    }

    [Fact]
    public async Task PrayedSummary_OneTime_UsesSingular()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(6)
            .Returns(Task.FromResult(new Prayer { Id = 6, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(6).Returns(Task.FromResult(1));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(6).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "6", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && !sut.PrayedSummary.Contains("Mar 3, 2026"); i++)
            await Task.Yield();

        Assert.Equal("Prayed for 1 time since Mar 3, 2026", sut.PrayedSummary);
    }

    [Fact]
    public async Task PrayedSummary_ZeroTimes_ShowsZero()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(7)
            .Returns(Task.FromResult(new Prayer { Id = 7, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(7).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(7).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "7", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && !sut.PrayedSummary.Contains("Mar 3, 2026"); i++)
            await Task.Yield();

        Assert.Equal("Prayed for 0 times since Mar 3, 2026", sut.PrayedSummary);
    }

    // ── HasBeenPrayedFor (issue #123) ─────────────────────────────────
    // Gates the PrayedSummary label's visibility: hidden at count 0, shown at 1+.
    // Driven through the real load path so a regression in the recompute wiring
    // (e.g. dropping the HasBeenPrayedFor notify) turns these red.

    [Fact]
    public async Task HasBeenPrayedFor_ZeroTimes_False()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(8)
            .Returns(Task.FromResult(new Prayer { Id = 8, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(8).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(8).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "8", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && !sut.PrayedSummary.Contains("Mar 3, 2026"); i++)
            await Task.Yield();

        Assert.False(sut.HasBeenPrayedFor);
    }

    [Fact]
    public async Task HasBeenPrayedFor_OneTime_True()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(9)
            .Returns(Task.FromResult(new Prayer { Id = 9, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(9).Returns(Task.FromResult(1));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(9).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "9", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && !sut.PrayedSummary.Contains("Mar 3, 2026"); i++)
            await Task.Yield();

        Assert.True(sut.HasBeenPrayedFor);
    }

    // ── CreatedAtDisplay (issue #107) ─────────────────────────────────
    // Absolute "Started {date}" caption shown beneath the title on the detail view.
    // Driven through the real load path so a regression in the wiring turns this red.

    [Fact]
    public async Task CreatedAtDisplay_FormatsStartedAbsoluteDate()
    {
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(10)
            .Returns(Task.FromResult(new Prayer { Id = 10, Title = "Test", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(10).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(10).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "10", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && sut.CreatedAtDisplay != "Started Mar 3, 2026"; i++)
            await Task.Yield();

        Assert.Equal("Started Mar 3, 2026", sut.CreatedAtDisplay);
    }

    // ── AccessibleSummary positive composition (issue #148 Phase 2, item 1b) ──
    // Replaces the Cards_PrayerRow_HasAccessibleSummary E2E (AccessibilityTests.cs)
    // with a deterministic unit assertion over the getter
    // (PrayerRequestDetailViewModel.cs:382). The getter comma-joins up to three
    // parts: CardTitle (when non-empty), Title (always), AnsweredAtDisplay (when
    // answered). AnsweredAtDisplay is culture/date dependent ("✓ {MMM d}"), so the
    // expected string is built from the VM's own AnsweredAtDisplay rather than a
    // hardcoded date.

    [Fact]
    public void AccessibleSummary_AllThreeParts_JoinsCardTitleTitleAnsweredInOrder()
    {
        var sut = CreateSut();
        sut.CardTitle = "Family";
        sut.Title = "Healing";
        sut.IsAnswered = true; // populates AnsweredAt = today ⇒ AnsweredAtDisplay non-empty

        Assert.NotEmpty(sut.AnsweredAtDisplay);
        Assert.Equal(
            string.Join(", ", new[] { "Family", "Healing", sut.AnsweredAtDisplay }),
            sut.AccessibleSummary);
    }

    [Fact]
    public void AccessibleSummary_NoCardTitle_OmitsCardTitlePart()
    {
        var sut = CreateSut();
        // CardTitle defaults to empty ⇒ excluded by the IsNullOrEmpty guard.
        sut.Title = "Healing";
        sut.IsAnswered = true;

        Assert.Equal(
            string.Join(", ", new[] { "Healing", sut.AnsweredAtDisplay }),
            sut.AccessibleSummary);
    }

    [Fact]
    public void AccessibleSummary_Unanswered_OmitsAnsweredPart()
    {
        var sut = CreateSut();
        sut.CardTitle = "Family";
        sut.Title = "Healing";
        // IsAnswered defaults false ⇒ AnsweredAtDisplay empty ⇒ part excluded.

        Assert.Empty(sut.AnsweredAtDisplay);
        Assert.Equal("Family, Healing", sut.AccessibleSummary);
    }

    [Fact]
    public async Task AccessibleSummary_ExcludesStartedDate()
    {
        // The detail view shows the start date in its own Label (Detail_Label_StartedDate),
        // which screen readers announce verbatim. Folding it into AccessibleSummary too would
        // double-read it, so the composite row summary must NOT contain the started caption.
        var sut = CreateSut();
        _db.GetByIdAsync<Prayer>(12)
            .Returns(Task.FromResult(new Prayer { Id = 12, Title = "Healing", CreatedAt = new DateTime(2026, 3, 3) }));
        _prayerService.GetInteractionCountByPrayerAsync(12).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(12).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));

        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = "12", ["viewOnly"] = "true" });

        for (int i = 0; i < 20 && sut.Title != "Healing"; i++)
            await Task.Yield();

        Assert.DoesNotContain("Started", sut.AccessibleSummary);
    }

    // ── FrequencyOptions (issue #169 convert) ─────────────────────────
    // Replaces the Reminders_FrequencyPicker_HasOptions E2E (ReminderTests.cs)
    // with a deterministic unit assertion over the picker's item source
    // (PrayerRequestDetailViewModel.cs:395): the list is non-empty and exposes
    // the headline Daily/Weekly cadences the E2E probed for on Android.

    [Fact]
    public void FrequencyOptions_IsNonEmpty_AndContainsDailyAndWeekly()
    {
        var sut = CreateSut();

        Assert.NotEmpty(sut.FrequencyOptions);
        Assert.Contains(PrayerFrequency.Daily, sut.FrequencyOptions);
        Assert.Contains(PrayerFrequency.Weekly, sut.FrequencyOptions);
    }

    // ── Reminder picker visibility (issue #169 convert) ───────────────
    // Replaces the Reminders_Toggle_ShowsThenHidesPickers E2E (ReminderTests.cs)
    // with deterministic unit assertions over the derived visibility gates and
    // their PropertyChanged edges: ShowNotifyTime == CanNotify;
    // ShowDayOfWeek == CanNotify && Weekly; ShowDayOfMonth == CanNotify && Monthly
    // (PrayerRequestDetailViewModel.cs:270-272). The CanNotify setter re-raises all
    // three (:202-204) and the PrayerFrequency setter re-raises the two day pickers
    // (:223-224), which is what drives the toggle show/hide on the form.

    [Fact]
    public void ReminderPickers_AllHidden_WhenCanNotifyFalse()
    {
        var sut = CreateSut();
        // CanNotify defaults false.
        Assert.False(sut.ShowNotifyTime);
        Assert.False(sut.ShowDayOfWeek);
        Assert.False(sut.ShowDayOfMonth);
    }

    [Fact]
    public void ShowNotifyTime_FollowsCanNotify_AndRaisesPickerPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.CanNotify = true;

        Assert.True(sut.ShowNotifyTime);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.ShowNotifyTime), raised);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.ShowDayOfWeek), raised);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.ShowDayOfMonth), raised);
    }

    [Fact]
    public void ShowDayOfWeek_VisibleOnly_WhenCanNotifyAndWeekly()
    {
        var sut = CreateSut();
        sut.CanNotify = true;

        sut.PrayerFrequency = PrayerFrequency.Weekly;
        Assert.True(sut.ShowDayOfWeek);
        Assert.False(sut.ShowDayOfMonth);

        sut.PrayerFrequency = PrayerFrequency.Daily;
        Assert.False(sut.ShowDayOfWeek);
    }

    [Fact]
    public void ShowDayOfMonth_VisibleOnly_WhenCanNotifyAndMonthly()
    {
        var sut = CreateSut();
        sut.CanNotify = true;

        sut.PrayerFrequency = PrayerFrequency.Monthly;
        Assert.True(sut.ShowDayOfMonth);
        Assert.False(sut.ShowDayOfWeek);
    }

    [Fact]
    public void PrayerFrequencyChange_RaisesDayPickerPropertyChanged()
    {
        var sut = CreateSut();
        sut.CanNotify = true;
        // A new Prayer defaults to Weekly (Prayer.cs:39); establish a different
        // known baseline BEFORE subscribing so the Daily→Weekly transition below
        // genuinely changes the value and fires the setter's notify chain.
        sut.PrayerFrequency = PrayerFrequency.Daily;
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.PrayerFrequency = PrayerFrequency.Weekly;

        Assert.Contains(nameof(PrayerRequestDetailViewModel.ShowDayOfWeek), raised);
        Assert.Contains(nameof(PrayerRequestDetailViewModel.ShowDayOfMonth), raised);
    }

    // ── MarkAnsweredCommand (E2E-cull conversion) ─────────────────────
    // Sole unit coverage for the deleted Prayers_MarkAnswered E2E
    // (PrayerListTests.cs). Characterizes MarkAnsweredAsync
    // (PrayerRequestDetailViewModel.cs:651): flip IsAnswered → persist via
    // SavePrayerAsync → announce "Prayer marked as answered" → pop back (GoToAsync "..").

    [Fact]
    public async Task MarkAnsweredCommand_MarksAnswered_SavesAnnouncesAndNavigatesBack()
    {
        var sut = CreateSut();
        sut.Title = "Answered prayer";
        Assert.False(sut.IsAnswered);

        await ((IAsyncRelayCommand)sut.MarkAnsweredCommand).ExecuteAsync(null);

        Assert.True(sut.IsAnswered);
        await _prayerService.Received(1).SavePrayerAsync(Arg.Any<Prayer>());
        _accessibilityService.Received(1).Announce("Prayer marked as answered");
        await _navigationService.Received(1).GoToAsync(Arg.Is<string>(s => s.StartsWith("..")));
    }

    // ── Move prayer between cards (E2E-cull conversion) ───────────────
    // SOLE unit coverage for the three deleted move E2Es in PrayerCardTests.cs
    // (Cards_MovePrayerBetweenCards_BothCardsReflect and the two stale-margin /
    // target-expand regressions). The card picker's SelectedCard setter
    // (PrayerRequestDetailViewModel.cs:412) reassigns PrayerCardId and dirties the
    // form (IsDirty clause at :121); SaveAsync then persists the prayer under its
    // new PrayerCardId — the reparent the E2Es exercised on-screen.

    [Fact]
    public async Task SelectedCard_Reparents_MarksDirty_AndSavePersistsNewCard()
    {
        var sut = CreateSut();
        var source = new PrayerCard { Id = 3, Title = "Family" };
        var target = new PrayerCard { Id = 7, Title = "Work" };
        sut.AvailableCards.Add(source);
        sut.AvailableCards.Add(target);
        sut.Title = "Move me";

        sut.SelectedCard = target;

        Assert.Equal(7, sut.PrayerCardId);
        Assert.True(sut.IsDirty);

        Prayer? saved = null;
        _prayerService.SavePrayerAsync(Arg.Do<Prayer>(p => saved = p))
            .Returns(call => Task.FromResult((Prayer)call[0]));

        await ((IAsyncRelayCommand)sut.SaveCommand).ExecuteAsync(null);

        await _prayerService.Received(1).SavePrayerAsync(Arg.Is<Prayer>(p => p.PrayerCardId == 7));
        Assert.Equal(7, saved!.PrayerCardId);
    }

    // ── CanShare / ShareCommand — issue #255 share-block on effective-protection + lock ──
    // Gates the affordance (disable, don't hide) per .project/design-system.md
    // "Required states — Disabled: dim / disable the control … reflect state, don't hide it."
    // Card + box are resolved from AvailableCards (already loaded by LoadCardsAsync) and a
    // single GetBoxesAsync lookup — no extra per-share service round-trip.

    private void SetupLoadWithCard(int prayerId, PrayerCard card)
    {
        _db.GetByIdAsync<Prayer>(prayerId)
            .Returns(Task.FromResult(new Prayer { Id = prayerId, Title = "Test", PrayerCardId = card.Id, CreatedAt = DateTime.Today }));
        _prayerService.GetInteractionCountByPrayerAsync(prayerId).Returns(Task.FromResult(0));
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard> { card }));
        _tagService.GetTagsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
        _tagService.GetTagsByRequestIdAsync(prayerId).Returns(Task.FromResult<IReadOnlyList<PrayerTag>>(new List<PrayerTag>()));
    }

    private async Task LoadAndDrainAsync(PrayerRequestDetailViewModel sut, int prayerId)
    {
        ((IQueryAttributable)sut).ApplyQueryAttributes(
            new Dictionary<string, object> { ["load"] = prayerId.ToString(), ["viewOnly"] = "true" });
        for (int i = 0; i < 20 && sut.SelectedCard is null; i++)
            await Task.Yield();
    }

    [Fact]
    public async Task CanShare_ProtectedAndLocked_ReturnsFalse()
    {
        var card = new PrayerCard { Id = 3, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        SetupLoadWithCard(50, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var sut = CreateSut();

        await LoadAndDrainAsync(sut, 50);

        Assert.False(sut.CanShare);
    }

    [Fact]
    public async Task CanShare_ProtectedButUnlocked_ReturnsTrue()
    {
        var card = new PrayerCard { Id = 3, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        SetupLoadWithCard(51, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(true);
        var sut = CreateSut();

        await LoadAndDrainAsync(sut, 51);

        Assert.True(sut.CanShare);
    }

    [Fact]
    public async Task CanShare_UnprotectedAndLocked_ReturnsTrue()
    {
        var card = new PrayerCard { Id = 3, Title = "Open", ProtectionMode = CardProtectionMode.None };
        SetupLoadWithCard(52, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var sut = CreateSut();

        await LoadAndDrainAsync(sut, 52);

        Assert.True(sut.CanShare);
    }

    [Fact]
    public async Task CanShare_ProtectedViaBoxCascade_Locked_ReturnsFalse()
    {
        var box = new CardBox { Id = 9, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.Hidden };
        _boxService.GetBoxesAsync().Returns(new List<CardBox> { box }.AsReadOnly());
        var card = new PrayerCard { Id = 3, Title = "Cascaded", ProtectionMode = CardProtectionMode.None, BoxId = 9 };
        SetupLoadWithCard(53, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var sut = CreateSut();

        await LoadAndDrainAsync(sut, 53);

        Assert.False(sut.CanShare);
    }

    [Fact]
    public async Task ShareCommand_ProtectedAndLocked_CanExecuteFalse()
    {
        var card = new PrayerCard { Id = 3, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        SetupLoadWithCard(54, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var sut = CreateSut();

        await LoadAndDrainAsync(sut, 54);

        Assert.False(((IRelayCommand)sut.ShareCommand).CanExecute(null));
    }

    [Fact]
    public async Task ShareAsync_ProtectedAndLocked_NoOpGuard_DoesNotThrow()
    {
        // Defense in depth (per issue #255): ShareAsync itself guards even if somehow
        // invoked while blocked (e.g. direct ExecuteAsync bypassing canExecute). The
        // unguarded body reaches IPlatformApplication.Current (untestable service-locator
        // resolution) — a no-op return before that line means this completes without
        // exception.
        var card = new PrayerCard { Id = 3, Title = "Secret", ProtectionMode = CardProtectionMode.LockedVisible };
        SetupLoadWithCard(55, card);
        _confidentialAccessService.IsSessionUnlocked.Returns(false);
        var sut = CreateSut();
        await LoadAndDrainAsync(sut, 55);

        await ((IAsyncRelayCommand)sut.ShareCommand).ExecuteAsync(null);

        // Reaching this line without an exception proves the guard returned early.
        Assert.False(sut.CanShare);
    }
}
