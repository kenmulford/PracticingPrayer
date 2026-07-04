using System;
using System.Collections.Generic;
using SQLite;
using System.Linq;
using PrayerApp.Services;

namespace PrayerApp.Models
{
    [Table("PrayerCard")]
    public class PrayerCard
    {
        private static IDBService? _dbService;
        private string _title = String.Empty;

        [PrimaryKey, AutoIncrement]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Title"), MaxLength(100)]
        public string Title
        {
            get => _title;
            set => _title = value ?? String.Empty;
        }

        [Column("CanNotify")]
        public bool CanNotify { get; set; } = false;

        [Column("PrayerFrequency")]
        public PrayerFrequency PrayerFrequency { get; set; } = PrayerFrequency.Weekly;

        [Column("IsAnswered")]
        public bool IsAnswered { get; set; } = false;

        [Column("IsFavorite")]
        public bool IsFavorite { get; set; } = false;

        [Column("IsSystem")]
        public bool IsSystem { get; set; }

        [Column("IsImported")]
        public bool IsImported { get; set; } = false;

        [Column("ProtectionMode")]
        public CardProtectionMode ProtectionMode { get; set; } = CardProtectionMode.None;

        /// <summary>FK to CardBox.Id. 0 = Unboxed (no box assigned).</summary>
        [Column("BoxId")]
        public int BoxId { get; set; }

        /// <summary>
        /// The card's <see cref="BoxId"/> at the moment it was archived, so unarchiving can
        /// restore it to its original collection. Null when not archived (or pre-migration),
        /// in which case unarchive falls back to Loose Cards (0).
        /// </summary>
        [Column("PreArchiveBoxId")]
        public int? PreArchiveBoxId { get; set; }

        // Well-known SystemKey values for system cards.
        public const string SystemKeyQuickAdd = "quick_add";
        public const string SystemKeySharedWithMe = "shared_with_me";

        // Well-known titles for system cards (shared by CardService and DBService migrations).
        public const string TitleQuickAdd = "Quick Add";
        public const string TitleSharedWithMe = "Shared with me";

        /// <summary>Stable key for system cards: "quick_add" or "shared_with_me". Null for user cards. Used for icon mapping.</summary>
        [Column("SystemKey"), MaxLength(20)]
        public string? SystemKey { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        #region Static Methods
        public static void SetDBService(IDBService dbService)
        {
            _dbService = dbService;
        }

        /// <summary>
        /// Computes the effective protection mode for a card at read time. Not persisted —
        /// derived from the card's own <see cref="ProtectionMode"/> and, when the card defers
        /// (<see cref="CardProtectionMode.None"/>), the owning box's cascade settings.
        /// A card with an explicit mode always wins over the box. A card with no box
        /// (<paramref name="box"/> is null) falls back to the card's own mode.
        /// </summary>
        public static CardProtectionMode GetEffectiveProtectionMode(PrayerCard card, CardBox? box)
        {
            if (card.ProtectionMode != CardProtectionMode.None)
                return card.ProtectionMode;

            if (box != null && box.ProtectAllCards)
                return box.CardProtectionMode;

            return CardProtectionMode.None;
        }
        #endregion

        #region Actions

        public async Task SaveAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call PrayerCard.SetDBService at app startup.");
            if (Id == 0)
            {
                await _dbService.InsertAsync(this);
            }
            else
            {
                UpdatedAt = DateTime.Now;
                await _dbService.UpdateAsync(this);
            }
        }

        public async Task DeleteAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call PrayerCard.SetDBService at app startup.");

            await _dbService.DeleteAsync(this);
        }

        public static async Task<PrayerCard> LoadAsync(int _id)
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call PrayerCard.SetDBService at app startup.");

            return await _dbService.GetByIdAsync<PrayerCard>(_id);
        }

        public static async Task<List<PrayerCard>> LoadAllAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call PrayerCard.SetDBService at app startup.");

            return await _dbService.GetAllAsync<PrayerCard>();
        }

        #endregion
    }
}
