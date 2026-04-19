#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public sealed class ProfileStateChangedEventArgs : EventArgs
    {
        public ProfileStateChangedEventArgs(int profileId, string profileName)
        {
            ProfileId = profileId;
            ProfileName = profileName;
        }

        public int ProfileId { get; }
        public string ProfileName { get; }
    }

    public sealed class ProfileAccessSnapshot
    {
        private static readonly string[] UnsafeKidsMarkers =
        {
            "adult", "xxx", "18+", "18 plus", "18plus", "erotic", "porn", "teaser", "preview", "clip"
        };

        public int ProfileId { get; init; }
        public string ProfileName { get; init; } = string.Empty;
        public bool IsKidsSafeMode { get; init; }
        public bool HideLockedContent { get; init; }
        public bool IsUnlocked { get; init; }
        public IReadOnlySet<int> LockedSourceIds { get; init; } = new HashSet<int>();
        public IReadOnlySet<string> LockedCategoryKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasLockedContent => LockedSourceIds.Count > 0 || LockedCategoryKeys.Count > 0;
        public bool ShouldHideLockedContent => HideLockedContent && !IsUnlocked;

        public bool IsSourceAllowed(int sourceProfileId)
        {
            return !ShouldHideLockedContent || !LockedSourceIds.Contains(sourceProfileId);
        }

        public bool IsCategoryAllowed(string domain, string categoryName)
        {
            if (!ShouldHideLockedContent)
            {
                return true;
            }

            return !LockedCategoryKeys.Contains(ProfileStateService.MakeCategoryLockKey(domain, categoryName));
        }

        public bool IsMovieAllowed(Movie movie)
        {
            if (!IsSourceAllowed(movie.SourceProfileId) || !IsCategoryAllowed(ProfileDomains.Movies, movie.CategoryName))
            {
                return false;
            }

            return !IsKidsSafeMode || IsKidsSafeMedia(movie.Title, movie.CategoryName, movie.ContentKind);
        }

        public bool IsSeriesAllowed(Series series)
        {
            if (!IsSourceAllowed(series.SourceProfileId) || !IsCategoryAllowed(ProfileDomains.Series, series.CategoryName))
            {
                return false;
            }

            return !IsKidsSafeMode || IsKidsSafeMedia(series.Title, series.CategoryName, series.ContentKind);
        }

        public bool IsLiveChannelAllowed(Channel channel, ChannelCategory category)
        {
            if (!IsSourceAllowed(category.SourceProfileId) || !IsCategoryAllowed(ProfileDomains.Live, category.Name))
            {
                return false;
            }

            return !IsKidsSafeMode || IsKidsSafeLive(channel.Name, category.Name);
        }

        private static bool IsKidsSafeMedia(string title, string categoryName, string contentKind)
        {
            if (!string.Equals(contentKind, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !ContainsUnsafeKidsMarker(title) && !ContainsUnsafeKidsMarker(categoryName);
        }

        private static bool IsKidsSafeLive(string title, string categoryName)
        {
            return !ContainsUnsafeKidsMarker(title) && !ContainsUnsafeKidsMarker(categoryName);
        }

        private static bool ContainsUnsafeKidsMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var lower = value.Trim().ToLowerInvariant();
            return UnsafeKidsMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
        }
    }

    public static class ProfileDomains
    {
        public const string Live = "Live";
        public const string Movies = "Movies";
        public const string Series = "Series";
    }

    public interface IProfileStateService
    {
        event EventHandler<ProfileStateChangedEventArgs>? ActiveProfileChanged;
        event EventHandler<ProfileStateChangedEventArgs>? ProfileConfigurationChanged;

        Task<IReadOnlyList<AppProfile>> GetProfilesAsync(AppDbContext db);
        Task<AppProfile> GetActiveProfileAsync(AppDbContext db);
        Task<int> GetActiveProfileIdAsync(AppDbContext db);
        Task<AppProfile> CreateProfileAsync(AppDbContext db, string name, bool isKidsProfile);
        Task RenameProfileAsync(AppDbContext db, int profileId, string name);
        Task SwitchProfileAsync(AppDbContext db, int profileId);
        Task<ParentalControlSetting> GetParentalControlsAsync(AppDbContext db, int profileId);
        Task SetKidsSafeModeAsync(AppDbContext db, int profileId, bool isEnabled);
        Task SetHideLockedContentAsync(AppDbContext db, int profileId, bool hideLockedContent);
        Task SetPinAsync(AppDbContext db, int profileId, string pin);
        Task ClearPinAsync(AppDbContext db, int profileId);
        Task<bool> UnlockLockedContentAsync(AppDbContext db, int profileId, string pin);
        void RelockProfile(int profileId);
        bool IsProfileUnlocked(int profileId);
        Task SetLockedSourceIdsAsync(AppDbContext db, int profileId, IEnumerable<int> sourceIds);
        Task SetLockedCategoryKeysAsync(AppDbContext db, int profileId, IEnumerable<string> categoryKeys);
        Task<ProfileAccessSnapshot> GetAccessSnapshotAsync(AppDbContext db);
    }

    public sealed class ProfileStateService : IProfileStateService
    {
        private const string ActiveProfileSettingKey = "Profiles.ActiveProfileId";
        private readonly HashSet<int> _unlockedProfiles = new();
        private readonly object _stateLock = new();

        public event EventHandler<ProfileStateChangedEventArgs>? ActiveProfileChanged;
        public event EventHandler<ProfileStateChangedEventArgs>? ProfileConfigurationChanged;

        public async Task<IReadOnlyList<AppProfile>> GetProfilesAsync(AppDbContext db)
        {
            await EnsureDefaultProfileExistsAsync(db);
            return await db.AppProfiles
                .AsNoTracking()
                .OrderBy(profile => profile.CreatedAtUtc)
                .ThenBy(profile => profile.Name)
                .ToListAsync();
        }

        public async Task<AppProfile> GetActiveProfileAsync(AppDbContext db)
        {
            await EnsureDefaultProfileExistsAsync(db);
            var activeId = await GetActiveProfileIdAsync(db);
            var profile = await db.AppProfiles.FirstOrDefaultAsync(item => item.Id == activeId);
            if (profile != null)
            {
                return profile;
            }

            profile = await db.AppProfiles.OrderBy(item => item.Id).FirstAsync();
            await SaveActiveProfileIdAsync(db, profile.Id);
            return profile;
        }

        public async Task<int> GetActiveProfileIdAsync(AppDbContext db)
        {
            await EnsureDefaultProfileExistsAsync(db);
            var rawValue = await db.AppSettings
                .Where(setting => setting.Key == ActiveProfileSettingKey)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (int.TryParse(rawValue, out var activeProfileId) &&
                await db.AppProfiles.AnyAsync(profile => profile.Id == activeProfileId))
            {
                return activeProfileId;
            }

            var fallbackId = await db.AppProfiles.OrderBy(profile => profile.Id).Select(profile => profile.Id).FirstAsync();
            await SaveActiveProfileIdAsync(db, fallbackId);
            return fallbackId;
        }

        public async Task<AppProfile> CreateProfileAsync(AppDbContext db, string name, bool isKidsProfile)
        {
            await EnsureDefaultProfileExistsAsync(db);
            var normalizedName = NormalizeProfileName(name, isKidsProfile ? "Kids" : "Profile");
            var profile = new AppProfile
            {
                Name = normalizedName,
                IsKidsProfile = isKidsProfile,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.AppProfiles.Add(profile);
            await db.SaveChangesAsync();
            await EnsureParentalControlsRowAsync(db, profile.Id);
            if (isKidsProfile)
            {
                await SetKidsSafeModeAsync(db, profile.Id, true);
            }

            OnProfileConfigurationChanged(profile);
            return profile;
        }

        public async Task RenameProfileAsync(AppDbContext db, int profileId, string name)
        {
            var profile = await db.AppProfiles.FirstOrDefaultAsync(item => item.Id == profileId);
            if (profile == null)
            {
                return;
            }

            profile.Name = NormalizeProfileName(name, profile.IsKidsProfile ? "Kids" : "Profile");
            await db.SaveChangesAsync();
            OnProfileConfigurationChanged(profile);
        }

        public async Task SwitchProfileAsync(AppDbContext db, int profileId)
        {
            var profile = await db.AppProfiles.FirstOrDefaultAsync(item => item.Id == profileId);
            if (profile == null)
            {
                return;
            }

            await SaveActiveProfileIdAsync(db, profileId);
            RelockProfile(profileId);
            ActiveProfileChanged?.Invoke(this, new ProfileStateChangedEventArgs(profile.Id, profile.Name));
        }

        public async Task<ParentalControlSetting> GetParentalControlsAsync(AppDbContext db, int profileId)
        {
            return await EnsureParentalControlsRowAsync(db, profileId);
        }

        public async Task SetKidsSafeModeAsync(AppDbContext db, int profileId, bool isEnabled)
        {
            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.IsKidsSafeMode = isEnabled;
            await db.SaveChangesAsync();
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task SetHideLockedContentAsync(AppDbContext db, int profileId, bool hideLockedContent)
        {
            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.HideLockedContent = hideLockedContent;
            await db.SaveChangesAsync();
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task SetPinAsync(AppDbContext db, int profileId, string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                return;
            }

            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.PinHash = HashPin(pin);
            await db.SaveChangesAsync();
            RelockProfile(profileId);
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task ClearPinAsync(AppDbContext db, int profileId)
        {
            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.PinHash = string.Empty;
            await db.SaveChangesAsync();
            RelockProfile(profileId);
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task<bool> UnlockLockedContentAsync(AppDbContext db, int profileId, string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                return false;
            }

            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            if (string.IsNullOrWhiteSpace(controls.PinHash))
            {
                return false;
            }

            if (!string.Equals(controls.PinHash, HashPin(pin), StringComparison.Ordinal))
            {
                return false;
            }

            lock (_stateLock)
            {
                _unlockedProfiles.Add(profileId);
            }

            await NotifyProfileConfigurationChangedAsync(db, profileId);
            return true;
        }

        public void RelockProfile(int profileId)
        {
            lock (_stateLock)
            {
                _unlockedProfiles.Remove(profileId);
            }
        }

        public bool IsProfileUnlocked(int profileId)
        {
            lock (_stateLock)
            {
                return _unlockedProfiles.Contains(profileId);
            }
        }

        public async Task SetLockedSourceIdsAsync(AppDbContext db, int profileId, IEnumerable<int> sourceIds)
        {
            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.LockedSourceIdsJson = SerializeInts(sourceIds);
            await db.SaveChangesAsync();
            RelockProfile(profileId);
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task SetLockedCategoryKeysAsync(AppDbContext db, int profileId, IEnumerable<string> categoryKeys)
        {
            var controls = await EnsureParentalControlsRowAsync(db, profileId);
            controls.LockedCategoryIdsJson = SerializeStrings(categoryKeys);
            await db.SaveChangesAsync();
            RelockProfile(profileId);
            await NotifyProfileConfigurationChangedAsync(db, profileId);
        }

        public async Task<ProfileAccessSnapshot> GetAccessSnapshotAsync(AppDbContext db)
        {
            var profile = await GetActiveProfileAsync(db);
            var controls = await EnsureParentalControlsRowAsync(db, profile.Id);

            return new ProfileAccessSnapshot
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                IsKidsSafeMode = profile.IsKidsProfile || controls.IsKidsSafeMode,
                HideLockedContent = controls.HideLockedContent,
                IsUnlocked = IsProfileUnlocked(profile.Id),
                LockedSourceIds = DeserializeInts(controls.LockedSourceIdsJson),
                LockedCategoryKeys = DeserializeStrings(controls.LockedCategoryIdsJson)
            };
        }

        public static string MakeCategoryLockKey(string domain, string categoryName)
        {
            var normalizedDomain = NormalizeCategoryPart(domain);
            var normalizedCategory = NormalizeCategoryPart(categoryName);
            return $"{normalizedDomain}:{normalizedCategory}";
        }

        private async Task EnsureDefaultProfileExistsAsync(AppDbContext db)
        {
            if (await db.AppProfiles.AnyAsync())
            {
                return;
            }

            db.AppProfiles.Add(new AppProfile
            {
                Id = 1,
                Name = "Primary",
                IsKidsProfile = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await EnsureParentalControlsRowAsync(db, 1);
            await SaveActiveProfileIdAsync(db, 1);
        }

        private async Task<ParentalControlSetting> EnsureParentalControlsRowAsync(AppDbContext db, int profileId)
        {
            var controls = await db.ParentalControlSettings.FirstOrDefaultAsync(setting => setting.ProfileId == profileId);
            if (controls != null)
            {
                return controls;
            }

            controls = new ParentalControlSetting
            {
                ProfileId = profileId,
                HideLockedContent = true
            };
            db.ParentalControlSettings.Add(controls);
            await db.SaveChangesAsync();
            return controls;
        }

        private async Task SaveActiveProfileIdAsync(AppDbContext db, int profileId)
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == ActiveProfileSettingKey);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = ActiveProfileSettingKey,
                    Value = profileId.ToString()
                });
            }
            else
            {
                setting.Value = profileId.ToString();
            }

            await db.SaveChangesAsync();
        }

        private async Task NotifyProfileConfigurationChangedAsync(AppDbContext db, int profileId)
        {
            var profile = await db.AppProfiles.FirstOrDefaultAsync(item => item.Id == profileId);
            if (profile != null)
            {
                OnProfileConfigurationChanged(profile);
            }
        }

        private void OnProfileConfigurationChanged(AppProfile profile)
        {
            ProfileConfigurationChanged?.Invoke(this, new ProfileStateChangedEventArgs(profile.Id, profile.Name));
        }

        private static string NormalizeProfileName(string name, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            return normalized.Length > 60 ? normalized[..60].Trim() : normalized;
        }

        private static string HashPin(string pin)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin.Trim()));
            return Convert.ToHexString(bytes);
        }

        private static string SerializeInts(IEnumerable<int> values)
        {
            return JsonSerializer.Serialize(values.Where(value => value > 0).Distinct().OrderBy(value => value));
        }

        private static string SerializeStrings(IEnumerable<string> values)
        {
            return JsonSerializer.Serialize(values
                .Select(NormalizeCategoryPart)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static HashSet<int> DeserializeInts(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<HashSet<int>>(json) ?? new HashSet<int>();
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        private static HashSet<string> DeserializeStrings(string json)
        {
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return values
                    .Select(NormalizeCategoryPart)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeCategoryPart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : ContentClassifier.NormalizeLabel(value).Trim().ToLowerInvariant();
        }
    }
}
