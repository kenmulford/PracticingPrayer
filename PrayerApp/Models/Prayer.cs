using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;
using PrayerApp.Services;

namespace PrayerApp.Models
{
    [Table("PrayerRequest")]
    public class Prayer
    {
        public const int TitleMaxLength = 100;

        private static IDBService? _dbService;
        private string _title = string.Empty;

        [PrimaryKey, AutoIncrement]
        [Column("Id")]
        public int Id { get; set; }

        [Column("PrayerCardId"), Indexed]
        public int PrayerCardId { get; set; }

        [Column("Title"), MaxLength(TitleMaxLength)]
        public string Title {
            get => _title;
            set => _title = value ?? string.Empty;
        }

        [Column("Details"), MaxLength(1000)]
        public string? Details { get; set;}

        [Column("CanNotify")]
        public bool CanNotify { get; set; } = false;

        [Column("PrayerFrequency")]
        public PrayerFrequency PrayerFrequency { get; set; } = PrayerFrequency.Weekly;

        [Column("NotifyHour")]
        public int NotifyHour { get; set; } = 9;

        [Column("NotifyMinute")]
        public int NotifyMinute { get; set; } = 0;

        [Column("NotifyDayOfWeek")]
        public int NotifyDayOfWeek { get; set; } = -1;  // 0=Sun..6=Sat, -1=use creation day

        [Column("NotifyDayOfMonth")]
        public int NotifyDayOfMonth { get; set; } = -1;  // 1-31, -1=use creation day

        [Column("NotifyDate")]
        public DateTime? NotifyDate { get; set; }  // OneTime/Yearly reminder day (date only); null = legacy CreatedAt path

        [Column("IsImported")]
        public bool IsImported { get; set; } = false;

        [Column("IsAnswered")]
        public bool IsAnswered { get; set; } = false;

        [Column("AnsweredAt")]
        public DateTime? AnsweredAt { get; set; }

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
                throw new InvalidOperationException("DBService not set. Call Prayer.SetDBService at app startup.");
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
                throw new InvalidOperationException("DBService not set. Call Prayer.SetDBService at app startup.");

            await _dbService.DeleteAsync(this);
        }

        public static async Task<Prayer> LoadAsync(int _id)
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call Prayer.SetDBService at app startup.");

            return await _dbService.GetByIdAsync<Prayer>(_id);
        }

        public static async Task<List<Prayer>> LoadAllAsync()
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call Prayer.SetDBService at app startup.");

            return await _dbService.GetAllAsync<Prayer>();
        }

        public static async Task<List<Prayer>> LoadByCardIdAsync(int prayerCardId)
        {
            if (_dbService == null)
                throw new InvalidOperationException("DBService not set. Call Prayer.SetDBService at app startup.");

            return await _dbService.GetPrayersByCardIdAsync(prayerCardId);
        }

        #endregion
    }
}
