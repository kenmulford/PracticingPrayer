using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using PrayerApp.Messages;
using PrayerApp.Models;

namespace PrayerApp.Services;

public class BoxService : IBoxService
{
    private IReadOnlyList<CardBox>? _cache;
    private readonly IDBService _dbService;
    private readonly IPrayerService _prayerService;
    private readonly ICardService _cardService;
    private readonly IMessenger _messenger;

    public BoxService(IDBService dbService, IPrayerService prayerService, ICardService cardService, IMessenger messenger)
    {
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _prayerService = prayerService ?? throw new ArgumentNullException(nameof(prayerService));
        _cardService = cardService ?? throw new ArgumentNullException(nameof(cardService));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
    }

    public async Task<IReadOnlyList<CardBox>> GetBoxesAsync()
    {
        if (_cache is not null)
            return _cache;

        var list = await CardBox.LoadAllAsync();
        var sorted = list
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .ToList();

        _cache = new ReadOnlyCollection<CardBox>(sorted);
        return _cache;
    }

    public async Task<CardBox?> GetSystemBoxAsync(string systemKey)
    {
        var boxes = await GetBoxesAsync();
        return boxes.FirstOrDefault(b => b.SystemKey == systemKey);
    }

    public async Task<CardBox> SaveBoxAsync(CardBox box)
    {
        // System boxes cannot be renamed — guard against accidental mutation
        if (box.Id > 0 && box.IsSystem) return box;

        var isNew = box.Id == 0;
        await box.SaveAsync();
        _cache = null;
        _messenger.Send(new CardBoxChangedMessage(box.Id, isNew ? ChangeKind.Created : ChangeKind.Updated));
        return box;
    }

    /// <summary>
    /// Ensures System and Archived boxes exist at runtime.
    /// Called from MauiProgram.SeedAsync as a resilience fallback
    /// (primary creation is in DBService.EnsureCardBoxMigrationAsync).
    /// </summary>
    public async Task SeedSystemBoxesAsync()
    {
        var boxes = await CardBox.LoadAllAsync();

        if (!boxes.Any(b => b.SystemKey == CardBox.SystemKeySystem))
        {
            var systemBox = new CardBox
            {
                Name = "System",
                IsSystem = true,
                SystemKey = CardBox.SystemKeySystem,
                SortOrder = 900
            };
            await systemBox.SaveAsync();
        }

        if (!boxes.Any(b => b.SystemKey == CardBox.SystemKeyArchived))
        {
            var archivedBox = new CardBox
            {
                Name = "Archived",
                IsSystem = true,
                SystemKey = CardBox.SystemKeyArchived,
                SortOrder = 999
            };
            await archivedBox.SaveAsync();
        }

        _cache = null;
    }

    public async Task DeleteBoxAsync(int boxId, bool deleteCards)
    {
        var box = await CardBox.LoadAsync(boxId);
        if (box == null || box.IsSystem) return; // System boxes cannot be deleted

        if (deleteCards)
        {
            var cards = await _dbService.GetCardsByBoxIdAsync(boxId);
            foreach (var card in cards)
            {
                var prayers = await _prayerService.GetPrayersByCardAsync(card.Id);
                foreach (var prayer in prayers)
                {
                    await _prayerService.DeletePrayerAsync(prayer, publishMessage: false);
                }
                await _cardService.DeleteCardAsync(card, publishMessage: false);
            }
        }
        else
        {
            // Unassign: move all cards in this box to Unboxed (BoxId=0)
            await _dbService.UnassignBoxFromCardsAsync(boxId);
            _cardService.InvalidateCache();
        }

        await box.DeleteAsync();
        _cache = null;
        // Single summary signal — see BulkChangedMessage doc for why no granular send pairs with this.
        _messenger.Send(new BulkChangedMessage());
    }

    public async Task<bool> HasConfidentialCardsAsync()
    {
        var cards = await _cardService.GetCardsAsync();
        if (cards.Count == 0) return false;

        var boxes = await GetBoxesAsync();
        var boxesById = boxes.ToDictionary(b => b.Id);

        foreach (var card in cards)
        {
            boxesById.TryGetValue(card.BoxId, out var box);
            if (PrayerCard.GetEffectiveProtectionMode(card, box) != CardProtectionMode.None)
                return true;
        }

        return false;
    }

    public void InvalidateCache()
    {
        _cache = null;
    }
}
