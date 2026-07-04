using System;
using SQLite;
using PrayerApp.Services;

namespace PrayerApp.Models
{
    [Table("CardBox")]
    public class CardBox
    {
        private static IDBService? _dbService;
        private string _name = string.Empty;

        [PrimaryKey, AutoIncrement]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Name"), MaxLength(50), Unique]
        public string Name
        {
            get => _name;
            set => _name = value ?? string.Empty;
        }

        /// <summary>True for system-managed boxes (System, Archived) — protected from user rename/delete.</summary>
        [Column("IsSystem")]
        public bool IsSystem { get; set; }

        // Well-known SystemKey values — use these constants instead of raw strings.
        public const string SystemKeySystem = "system";
        public const string SystemKeyArchived = "archived";

        /// <summary>Stable key for system boxes: "system" or "archived". Null for user-created boxes.</summary>
        [Column("SystemKey"), MaxLength(20)]
        public string? SystemKey { get; set; }

        /// <summary>Controls display order. Lower values appear first. User boxes use 0 (sorted by Name).</summary>
        [Column("SortOrder")]
        public int SortOrder { get; set; }

        /// <summary>When true, cascades <see cref="CardProtectionMode"/> to every card in this box that has no explicit protection mode of its own.</summary>
        [Column("ProtectAllCards")]
        public bool ProtectAllCards { get; set; }

        /// <summary>The protection mode cascaded to cards in this box when <see cref="ProtectAllCards"/> is true.</summary>
        [Column("CardProtectionMode")]
        public CardProtectionMode CardProtectionMode { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        #region Static Methods
        public static void SetDBService(IDBService dbService)
        {
            _dbService = dbService;
        }
        #endregion

        #region Actions

        public async Task SaveAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call CardBox.SetDBService at app startup.");
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
                throw new InvalidOperationException("DBService not set. Call CardBox.SetDBService at app startup.");

            await _dbService.DeleteAsync(this);
        }

        public static async Task<CardBox> LoadAsync(int id)
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call CardBox.SetDBService at app startup.");

            return await _dbService.GetByIdAsync<CardBox>(id);
        }

        public static async Task<List<CardBox>> LoadAllAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call CardBox.SetDBService at app startup.");

            return await _dbService.GetAllAsync<CardBox>();
        }

        #endregion
    }
}
