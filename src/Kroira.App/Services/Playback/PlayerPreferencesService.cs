#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Playback
{
    public sealed class PlayerPreferencesSnapshot
    {
        public double Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public PlaybackAspectMode AspectMode { get; set; } = PlaybackAspectMode.Automatic;
        public double PlaybackSpeed { get; set; } = 1.0;
        public double AudioDelaySeconds { get; set; }
        public double SubtitleDelaySeconds { get; set; }
        public double SubtitleScale { get; set; } = 1.0;
        public int SubtitlePosition { get; set; } = 100;
        public bool SubtitlesEnabled { get; set; } = true;
        public bool Deinterlace { get; set; }
        public bool AlwaysOnTop { get; set; }
    }

    public interface IPlayerPreferencesService
    {
        Task<PlayerPreferencesSnapshot> LoadAsync(AppDbContext db, int profileId);
        Task SaveAsync(AppDbContext db, int profileId, PlayerPreferencesSnapshot preferences);
    }

    public sealed class PlayerPreferencesService : IPlayerPreferencesService
    {
        private const string KeyPrefix = "PlayerPreferences.Profile.";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public async Task<PlayerPreferencesSnapshot> LoadAsync(AppDbContext db, int profileId)
        {
            var key = BuildKey(profileId);
            var json = await db.AppSettings
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return new PlayerPreferencesSnapshot();
            }

            try
            {
                return Normalize(JsonSerializer.Deserialize<PlayerPreferencesSnapshot>(json, JsonOptions));
            }
            catch
            {
                return new PlayerPreferencesSnapshot();
            }
        }

        public async Task SaveAsync(AppDbContext db, int profileId, PlayerPreferencesSnapshot preferences)
        {
            var key = BuildKey(profileId);
            var normalized = Normalize(preferences);
            var json = JsonSerializer.Serialize(normalized, JsonOptions);

            var setting = await db.AppSettings.FirstOrDefaultAsync(existing => existing.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = json
                });
            }
            else
            {
                setting.Value = json;
            }

            await db.SaveChangesAsync();
        }

        private static string BuildKey(int profileId)
        {
            return KeyPrefix + Math.Max(0, profileId);
        }

        private static PlayerPreferencesSnapshot Normalize(PlayerPreferencesSnapshot? snapshot)
        {
            var normalized = snapshot ?? new PlayerPreferencesSnapshot();
            normalized.Volume = Math.Clamp(normalized.Volume, 0, 100);
            normalized.PlaybackSpeed = Math.Clamp(normalized.PlaybackSpeed, 0.25, 3.0);
            normalized.AudioDelaySeconds = Math.Clamp(normalized.AudioDelaySeconds, -3.0, 3.0);
            normalized.SubtitleDelaySeconds = Math.Clamp(normalized.SubtitleDelaySeconds, -10.0, 10.0);
            normalized.SubtitleScale = Math.Clamp(normalized.SubtitleScale, 0.5, 2.5);
            normalized.SubtitlePosition = Math.Clamp(normalized.SubtitlePosition, 0, 100);
            return normalized;
        }
    }
}
