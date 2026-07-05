using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using PrayerApp.Messages;
using PrayerApp.Models;
using PrayerApp.Services;

namespace PrayerApp.Tests.Services;

public class BoxServiceTests
{
    private readonly IDBService _db;
    private readonly IPrayerService _prayerService;
    private readonly ICardService _cardService;
    private readonly IMessenger _messenger = new WeakReferenceMessenger();
    private readonly object _recipient = new();
    private readonly List<CardBoxChangedMessage> _boxMessages = new();
    private readonly List<BulkChangedMessage> _bulkMessages = new();
    private readonly BoxService _service;

    public BoxServiceTests()
    {
        _db = Substitute.For<IDBService>();
        _prayerService = Substitute.For<IPrayerService>();
        _cardService = Substitute.For<ICardService>();
        CardBox.SetDBService(_db);
        PrayerCard.SetDBService(_db);
        _messenger.Register<object, CardBoxChangedMessage>(_recipient, (_, m) => _boxMessages.Add(m));
        _messenger.Register<object, BulkChangedMessage>(_recipient, (_, m) => _bulkMessages.Add(m));
        _service = new BoxService(_db, _prayerService, _cardService, _messenger);
    }

    // ── GetBoxesAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoxesAsync_FirstCall_QueriesDatabase()
    {
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));

        await _service.GetBoxesAsync();

        await _db.Received(1).GetAllAsync<CardBox>();
    }

    [Fact]
    public async Task GetBoxesAsync_SecondCall_UsesCacheNotDatabase()
    {
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));

        await _service.GetBoxesAsync();
        await _service.GetBoxesAsync();

        await _db.Received(1).GetAllAsync<CardBox>();
    }

    [Fact]
    public async Task GetBoxesAsync_SortsBySortOrderThenName()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 1, Name = "Zulu", SortOrder = 0 },
            new() { Id = 2, Name = "Archived", SortOrder = 999, IsSystem = true, SystemKey = CardBox.SystemKeyArchived },
            new() { Id = 3, Name = "Alpha", SortOrder = 0 },
            new() { Id = 4, Name = "System", SortOrder = 900, IsSystem = true, SystemKey = CardBox.SystemKeySystem }
        };
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(boxes));

        var result = await _service.GetBoxesAsync();

        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal("Zulu", result[1].Name);
        Assert.Equal("System", result[2].Name);
        Assert.Equal("Archived", result[3].Name);
    }

    // ── GetSystemBoxAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetSystemBoxAsync_ReturnsSystemBoxByKey()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 1, Name = "Family" },
            new() { Id = 2, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem },
            new() { Id = 3, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived }
        };
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(boxes));

        var result = await _service.GetSystemBoxAsync(CardBox.SystemKeyArchived);

        Assert.NotNull(result);
        Assert.Equal("Archived", result!.Name);
        Assert.Equal(3, result.Id);
    }

    [Fact]
    public async Task GetSystemBoxAsync_ReturnsNullWhenNotFound()
    {
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));

        var result = await _service.GetSystemBoxAsync(CardBox.SystemKeyArchived);

        Assert.Null(result);
    }

    // ── SaveBoxAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveBoxAsync_NewBox_InsertsIntoDatabase()
    {
        var box = new CardBox { Id = 0, Name = "Family" };
        _db.InsertAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));

        await _service.SaveBoxAsync(box);

        await _db.Received(1).InsertAsync(Arg.Is<CardBox>(b => b.Name == "Family"));
    }

    [Fact]
    public async Task SaveBoxAsync_ExistingBox_UpdatesInDatabase()
    {
        var box = new CardBox { Id = 5, Name = "Renamed" };
        _db.UpdateAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));

        await _service.SaveBoxAsync(box);

        await _db.Received(1).UpdateAsync(Arg.Is<CardBox>(b => b.Id == 5));
    }

    [Fact]
    public async Task SaveBoxAsync_InvalidatesCache()
    {
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));
        await _service.GetBoxesAsync(); // populate cache

        var box = new CardBox { Id = 0, Name = "New" };
        _db.InsertAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));
        await _service.SaveBoxAsync(box);

        await _service.GetBoxesAsync(); // should re-query
        await _db.Received(2).GetAllAsync<CardBox>();
    }

    // ── DeleteBoxAsync (unassign) ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteBoxAsync_Unassign_UnassignsCardsAndDeletesBox()
    {
        var box = new CardBox { Id = 5, Name = "Family" };
        _db.GetByIdAsync<CardBox>(5).Returns(Task.FromResult(box));
        _db.DeleteAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));

        await _service.DeleteBoxAsync(5, deleteCards: false);

        await _db.Received(1).UnassignBoxFromCardsAsync(5);
        _cardService.Received(1).InvalidateCache();
        await _db.Received(1).DeleteAsync(Arg.Is<CardBox>(b => b.Id == 5));
    }

    // ── DeleteBoxAsync (cascade) ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteBoxAsync_CascadeDelete_DeletesCardsAndPrayers()
    {
        var box = new CardBox { Id = 5, Name = "Family" };
        _db.GetByIdAsync<CardBox>(5).Returns(Task.FromResult(box));

        var card1 = new PrayerCard { Id = 10, Title = "Card 1", BoxId = 5 };
        var card2 = new PrayerCard { Id = 11, Title = "Card 2", BoxId = 5 };
        _db.GetCardsByBoxIdAsync(5).Returns(Task.FromResult(new List<PrayerCard> { card1, card2 }));

        var prayer1 = new Prayer { Id = 100, PrayerCardId = 10 };
        var prayer2 = new Prayer { Id = 101, PrayerCardId = 11 };
        _prayerService.GetPrayersByCardAsync(10).Returns(Task.FromResult<IReadOnlyList<Prayer>>(new List<Prayer> { prayer1 }));
        _prayerService.GetPrayersByCardAsync(11).Returns(Task.FromResult<IReadOnlyList<Prayer>>(new List<Prayer> { prayer2 }));

        _db.DeleteAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));

        await _service.DeleteBoxAsync(5, deleteCards: true);

        // Cascade path passes publishMessage:false so per-entity messages don't fan out
        // alongside the trailing BulkChangedMessage.
        await _prayerService.Received(1).DeletePrayerAsync(prayer1, publishMessage: false);
        await _prayerService.Received(1).DeletePrayerAsync(prayer2, publishMessage: false);
        await _cardService.Received(1).DeleteCardAsync(card1, publishMessage: false);
        await _cardService.Received(1).DeleteCardAsync(card2, publishMessage: false);
        await _db.Received(1).DeleteAsync(Arg.Is<CardBox>(b => b.Id == 5));
    }

    // ── DeleteBoxAsync (system box protection) ────────────────────────────────

    [Fact]
    public async Task DeleteBoxAsync_SystemBox_DoesNotDelete()
    {
        var box = new CardBox { Id = 2, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem };
        _db.GetByIdAsync<CardBox>(2).Returns(Task.FromResult(box));

        await _service.DeleteBoxAsync(2, deleteCards: false);

        await _db.DidNotReceive().DeleteAsync(Arg.Any<CardBox>());
        await _db.DidNotReceive().UnassignBoxFromCardsAsync(Arg.Any<int>());
    }

    // ── SaveBoxAsync (system guard) ─────────────────────────────────────────

    [Fact]
    public async Task SaveBoxAsync_SystemBox_DoesNotSave()
    {
        var box = new CardBox { Id = 2, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem };

        var result = await _service.SaveBoxAsync(box);

        Assert.Same(box, result);
        await _db.DidNotReceive().UpdateAsync(Arg.Any<CardBox>());
        await _db.DidNotReceive().InsertAsync(Arg.Any<CardBox>());
    }

    // ── SeedSystemBoxesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SeedSystemBoxesAsync_BothExist_DoesNotInsert()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 1, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem },
            new() { Id = 2, Name = "Archived", IsSystem = true, SystemKey = CardBox.SystemKeyArchived }
        };
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(boxes));

        await _service.SeedSystemBoxesAsync();

        await _db.DidNotReceive().InsertAsync(Arg.Any<CardBox>());
    }

    [Fact]
    public async Task SeedSystemBoxesAsync_MissingArchived_CreatesIt()
    {
        var boxes = new List<CardBox>
        {
            new() { Id = 1, Name = "System", IsSystem = true, SystemKey = CardBox.SystemKeySystem }
        };
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(boxes));
        _db.InsertAsync(Arg.Any<CardBox>()).Returns(Task.FromResult(1));

        await _service.SeedSystemBoxesAsync();

        await _db.Received(1).InsertAsync(Arg.Is<CardBox>(b => b.SystemKey == CardBox.SystemKeyArchived));
    }

    // ── InvalidateCache ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateCache_ForcesNewQueryOnNextCall()
    {
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));
        await _service.GetBoxesAsync(); // populate cache

        _service.InvalidateCache();

        await _service.GetBoxesAsync(); // should re-query
        await _db.Received(2).GetAllAsync<CardBox>();
    }

    // ── Messenger publishes ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveBoxAsync_New_PublishesCreated()
    {
        var box = new CardBox { Name = "Friends" };

        await _service.SaveBoxAsync(box);

        Assert.Single(_boxMessages);
        Assert.Equal(ChangeKind.Created, _boxMessages[0].Kind);
    }

    [Fact]
    public async Task SaveBoxAsync_Existing_PublishesUpdated()
    {
        var box = new CardBox { Id = 4, Name = "Friends" };

        await _service.SaveBoxAsync(box);

        Assert.Single(_boxMessages);
        Assert.Equal(4, _boxMessages[0].BoxId);
        Assert.Equal(ChangeKind.Updated, _boxMessages[0].Kind);
    }

    [Fact]
    public async Task SaveBoxAsync_SystemBox_PublishesNothing()
    {
        var box = new CardBox { Id = 1, Name = "System", IsSystem = true };

        await _service.SaveBoxAsync(box);

        Assert.Empty(_boxMessages);
    }

    [Fact]
    public async Task DeleteBoxAsync_PublishesBulkOnly()
    {
        // Box delete is a bulk operation (cards and prayers may also change). Per the
        // BulkChangedMessage contract, granular CardBoxChangedMessage is NOT also sent.
        var box = new CardBox { Id = 4, Name = "Friends" };
        _db.GetByIdAsync<CardBox>(4).Returns(Task.FromResult<CardBox?>(box));
        _db.GetCardsByBoxIdAsync(4).Returns(new List<PrayerCard>());

        await _service.DeleteBoxAsync(4, deleteCards: false);

        Assert.Single(_bulkMessages);
        Assert.Empty(_boxMessages);
    }

    // ── HasConfidentialCardsAsync (#257 — background privacy screen gate) ─────
    // The platform snapshot-blanking code (Android FLAG_SECURE / iOS resign-active
    // overlay) only activates when this predicate is true, so it must correctly
    // fold in the box-cascade rule from PrayerCard.GetEffectiveProtectionMode.

    [Fact]
    public async Task HasConfidentialCardsAsync_NoCards_ReturnsFalse()
    {
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(new List<PrayerCard>()));
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));

        var result = await _service.HasConfidentialCardsAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task HasConfidentialCardsAsync_CardWithOwnProtection_ReturnsTrue()
    {
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, Title = "Private", BoxId = 0, ProtectionMode = CardProtectionMode.Hidden }
        };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(cards));
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox>()));

        var result = await _service.HasConfidentialCardsAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task HasConfidentialCardsAsync_CardProtectedViaBoxCascade_ReturnsTrue()
    {
        var box = new CardBox { Id = 5, Name = "Family", ProtectAllCards = true, CardProtectionMode = CardProtectionMode.LockedVisible };
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, Title = "Deferred to box", BoxId = 5, ProtectionMode = CardProtectionMode.None }
        };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(cards));
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox> { box }));

        var result = await _service.HasConfidentialCardsAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task HasConfidentialCardsAsync_NoCardsOrBoxesProtected_ReturnsFalse()
    {
        var box = new CardBox { Id = 5, Name = "Family", ProtectAllCards = false };
        var cards = new List<PrayerCard>
        {
            new() { Id = 1, Title = "Open", BoxId = 5, ProtectionMode = CardProtectionMode.None }
        };
        _cardService.GetCardsAsync().Returns(Task.FromResult<IReadOnlyList<PrayerCard>>(cards));
        _db.GetAllAsync<CardBox>().Returns(Task.FromResult(new List<CardBox> { box }));

        var result = await _service.HasConfidentialCardsAsync();

        Assert.False(result);
    }
}
