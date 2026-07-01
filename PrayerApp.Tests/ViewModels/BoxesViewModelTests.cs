using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class BoxesViewModelTests
{
    private readonly IBoxService _boxService = Substitute.For<IBoxService>();
    private readonly ICardService _cardService = Substitute.For<ICardService>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();
    private readonly IAccessibilityService _accessibilityService = Substitute.For<IAccessibilityService>();
    private readonly IMessenger _messenger = new WeakReferenceMessenger();

    public BoxesViewModelTests()
    {
        CardBox.SetDBService(Substitute.For<IDBService>());
    }

    private BoxesViewModel CreateSut() =>
        new(_boxService, _cardService, _navigationService, _accessibilityService, _messenger);

    private void SetupBoxes(params CardBox[] boxes)
    {
        _boxService.GetBoxesAsync().Returns(boxes.ToList().AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
    }

    // ── SyncAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_PopulatesBoxesFromService()
    {
        SetupBoxes(
            new CardBox { Id = 1, Name = "Family" },
            new CardBox { Id = 2, Name = "Ministry" });

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Equal(2, sut.Boxes.Count);
        Assert.Contains(sut.Boxes, b => b.Name == "Family");
        Assert.Contains(sut.Boxes, b => b.Name == "Ministry");
    }

    [Fact]
    public async Task SyncAsync_EmptyService_ProducesEmptyCollection()
    {
        SetupBoxes();

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Empty(sut.Boxes);
    }

    [Fact]
    public async Task SyncAsync_SetsIsLoadingFalse_AfterCompletion()
    {
        SetupBoxes();

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.False(sut.IsLoading);
    }

    [Fact]
    public async Task SyncAsync_CalculatesCardCounts()
    {
        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 5, Name = "Family" }
        }.AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>
        {
            new() { Id = 1, Title = "Card A", BoxId = 5 },
            new() { Id = 2, Title = "Card B", BoxId = 5 },
            new() { Id = 3, Title = "Card C", BoxId = 0 } // different box
        }.AsReadOnly());

        var sut = CreateSut();
        await sut.SyncAsync();

        Assert.Equal(2, sut.Boxes[0].CardCount);
    }

    // ── SyncAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_AddsNewBoxes()
    {
        SetupBoxes(new CardBox { Id = 1, Name = "Existing" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 1, Name = "Existing" },
            new() { Id = 2, Name = "New Box" }
        }.AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        await sut.SyncAsync();

        Assert.Equal(2, sut.Boxes.Count);
        Assert.Contains(sut.Boxes, b => b.Name == "New Box");
    }

    [Fact]
    public async Task SyncAsync_RemovesDeletedBoxes()
    {
        SetupBoxes(
            new CardBox { Id = 1, Name = "Keep" },
            new CardBox { Id = 2, Name = "Box To Delete" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 1, Name = "Keep" }
        }.AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        await sut.SyncAsync();

        Assert.Single(sut.Boxes);
        Assert.Equal("Keep", sut.Boxes[0].Name);
    }

    [Fact]
    public async Task SyncAsync_UpdatesExistingBoxName()
    {
        SetupBoxes(new CardBox { Id = 1, Name = "Old Name" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _boxService.GetBoxesAsync().Returns(new List<CardBox>
        {
            new() { Id = 1, Name = "New Name" }
        }.AsReadOnly());
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());
        await sut.SyncAsync();

        Assert.Equal("New Name", sut.Boxes[0].Name);
    }

    // ── Selection ─────────────────────────────────────────────────────

    [Fact]
    public async Task SelectCommand_TogglesIsSelected()
    {
        SetupBoxes(new CardBox { Id = 1, Name = "Test" });
        var sut = CreateSut();
        await sut.SyncAsync();
        var item = sut.Boxes[0];

        Assert.False(item.IsSelected);
        item.SelectCommand.Execute(null);
        Assert.True(item.IsSelected);
        item.SelectCommand.Execute(null);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public async Task SelectCommand_DeselectsOtherItems()
    {
        SetupBoxes(
            new CardBox { Id = 1, Name = "A" },
            new CardBox { Id = 2, Name = "B" });
        var sut = CreateSut();
        await sut.SyncAsync();

        sut.Boxes[0].SelectCommand.Execute(null);
        Assert.True(sut.Boxes[0].IsSelected);

        sut.Boxes[1].SelectCommand.Execute(null);
        Assert.False(sut.Boxes[0].IsSelected);
        Assert.True(sut.Boxes[1].IsSelected);
    }

    [Fact]
    public async Task DeselectAll_DeselectsAllItems()
    {
        SetupBoxes(new CardBox { Id = 1, Name = "A" });
        var sut = CreateSut();
        await sut.SyncAsync();

        sut.Boxes[0].SelectCommand.Execute(null);
        sut.DeselectAll();

        Assert.False(sut.Boxes[0].IsSelected);
    }

    // ── Delete ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCommand_Unassign_CallsDeleteBoxAsyncWithFalse()
    {
        SetupBoxes(new CardBox { Id = 5, Name = "Family" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns(BoxItemViewModel.ActionUnassign);

        await ((AsyncRelayCommand)sut.Boxes[0].DeleteCommand).ExecuteAsync(null);

        await _boxService.Received(1).DeleteBoxAsync(5, false);
    }

    [Fact]
    public async Task DeleteCommand_DeleteAll_CallsDeleteBoxAsyncWithTrue()
    {
        SetupBoxes(new CardBox { Id = 5, Name = "Family" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns(BoxItemViewModel.ActionDeleteAll);

        await ((AsyncRelayCommand)sut.Boxes[0].DeleteCommand).ExecuteAsync(null);

        await _boxService.Received(1).DeleteBoxAsync(5, true);
    }

    [Fact]
    public async Task DeleteCommand_Cancel_DoesNotDelete()
    {
        SetupBoxes(new CardBox { Id = 5, Name = "Family" });
        var sut = CreateSut();
        await sut.SyncAsync();

        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns("Cancel");

        await ((AsyncRelayCommand)sut.Boxes[0].DeleteCommand).ExecuteAsync(null);

        await _boxService.DidNotReceive().DeleteBoxAsync(Arg.Any<int>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task DeleteCommand_BackdropDismiss_DoesNotDelete()
    {
        SetupBoxes(new CardBox { Id = 5, Name = "Family" });
        var sut = CreateSut();
        await sut.SyncAsync();

        // Backdrop tap returns null on both Android and iOS
        _navigationService.DisplayActionSheetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string[]>())
            .Returns((string?)null);

        await ((AsyncRelayCommand)sut.Boxes[0].DeleteCommand).ExecuteAsync(null);

        await _boxService.DidNotReceive().DeleteBoxAsync(Arg.Any<int>(), Arg.Any<bool>());
    }

    // ── RemoveBox ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveBox_RemovesFromCollection()
    {
        SetupBoxes(new CardBox { Id = 1, Name = "Remove Me" });
        var sut = CreateSut();
        await sut.SyncAsync();
        var item = sut.Boxes[0];

        sut.RemoveBox(item);

        Assert.Empty(sut.Boxes);
    }

    // ── Messenger-driven sync ─────────────────────────────────────────

    [Fact]
    public async Task BulkChangedMessage_TriggersSyncAsync()
    {
        var sut = CreateSut();
        SetupBoxes(new CardBox { Id = 1, Name = "FromBulk" });

        _messenger.Send(new BulkChangedMessage());

        await Task.Yield();
        await Task.Yield();

        Assert.Contains(sut.Boxes, b => b.Name == "FromBulk");
    }

    // ── Slice 6a — single-flight + coalesce-pending SyncAsync ─────────

    [Fact]
    public async Task SyncAsync_BurstOfThreeConcurrent_CoalescesToTwoFetches()
    {
        // See PrayerCardsViewModelTests for full context. Same coalesce contract
        // applied per-VM: a burst of N concurrent triggers collapses to exactly
        // 2 fetches (one in-flight + one coalesced follow-up).
        var gate = new TaskCompletionSource<IReadOnlyList<CardBox>>();
        _boxService.GetBoxesAsync().Returns(gate.Task);
        _cardService.GetCardsAsync().Returns(new List<PrayerCard>().AsReadOnly());

        var sut = CreateSut();

        var t1 = sut.SyncAsync();
        var t2 = sut.SyncAsync();
        var t3 = sut.SyncAsync();

        gate.SetResult(new List<CardBox>().AsReadOnly());
        await Task.WhenAll(t1, t2, t3);

        await _boxService.Received(2).GetBoxesAsync();
    }

    // ── Delete gated by IsSystem (E2E-cull conversion) ────────────────
    // Replaces the deleted Boxes_SystemCollections_NoDeleteAction E2E (BoxTests.cs).
    // BoxItemViewModel wires DeleteCommand with canExecute () => !_box.IsSystem
    // (BoxesViewModel.cs:192): a system box's delete is disabled, a normal box's enabled.

    [Fact]
    public async Task DeleteCommand_SystemBox_CannotExecute_NonSystemBox_CanExecute()
    {
        SetupBoxes(
            new CardBox { Id = 1, Name = "System", IsSystem = true },
            new CardBox { Id = 2, Name = "Family", IsSystem = false });
        var sut = CreateSut();
        await sut.SyncAsync();

        var systemBox = sut.Boxes.First(b => b.IsSystem);
        var normalBox = sut.Boxes.First(b => !b.IsSystem);

        Assert.False(((AsyncRelayCommand)systemBox.DeleteCommand).CanExecute(null));
        Assert.True(((AsyncRelayCommand)normalBox.DeleteCommand).CanExecute(null));
    }
}
