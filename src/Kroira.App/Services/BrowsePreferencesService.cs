#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public sealed class BrowseChannelReference
    {
        public string LogicalKey { get; set; } = string.Empty;
        public int PreferredSourceProfileId { get; set; }
    }

    public sealed class BrowsePreferences
    {
        public string SortKey { get; set; } = string.Empty;
        public string SelectedCategoryKey { get; set; } = string.Empty;
        public int SelectedSourceId { get; set; }
        public bool IsAdvancedControlsExpanded { get; set; }
        public BrowseChannelReference? LastChannel { get; set; }
        public List<BrowseChannelReference> RecentChannels { get; set; } = new();
        public Dictionary<string, int> LiveChannelWatchCountsByKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int LastChannelId { get; set; }
        public bool HasExplicitLiveSortPreference { get; set; }
        public bool FavoritesOnly { get; set; }
        public bool HideSecondaryContent { get; set; }
        public bool GuideMatchedOnly { get; set; }
        public List<int> HiddenSourceIds { get; set; } = new();
        public List<int> RecentChannelIds { get; set; } = new();
        public Dictionary<int, int> LiveChannelWatchCounts { get; set; } = new();
        public List<string> HiddenCategoryKeys { get; set; } = new();
        public Dictionary<string, string> CategoryRemaps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public interface IBrowsePreferencesService
    {
        Task<BrowsePreferences> GetAsync(AppDbContext db, string domain, int profileId);
        Task SaveAsync(AppDbContext db, string domain, int profileId, BrowsePreferences preferences);
        Task RepairSourceReferencesAsync(AppDbContext db, int? sourceProfileId = null);
        string NormalizeCategoryKey(string? categoryName);
        string GetEffectiveCategoryName(BrowsePreferences preferences, string? categoryName);
        string GetEffectiveCategoryName(BrowsePreferences preferences, string? categoryName, string? defaultDisplayCategoryName);
        bool IsCategoryHidden(BrowsePreferences preferences, string? categoryName);
    }

    public sealed class BrowsePreferencesService : IBrowsePreferencesService
    {
        private const string KeyPrefix = "BrowsePrefs.";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public async Task<BrowsePreferences> GetAsync(AppDbContext db, string domain, int profileId)
        {
            var key = BuildKey(domain, profileId);
            var json = await db.AppSettings
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return new BrowsePreferences();
            }

            try
            {
                var preferences = JsonSerializer.Deserialize<BrowsePreferences>(json, JsonOptions) ?? new BrowsePreferences();
                preferences.SelectedCategoryKey = NormalizeCategoryKey(preferences.SelectedCategoryKey);
                preferences.HiddenSourceIds = preferences.HiddenSourceIds
                    .Where(id => id > 0)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                preferences.LastChannel = NormalizeChannelReference(preferences.LastChannel);
                preferences.LastChannelId = Math.Max(0, preferences.LastChannelId);
                preferences.RecentChannels = preferences.RecentChannels
                    .Select(NormalizeChannelReference)
                    .Where(reference => reference != null)
                    .Cast<BrowseChannelReference>()
                    .GroupBy(reference => reference.LogicalKey, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(12)
                    .ToList();
                preferences.RecentChannelIds = preferences.RecentChannelIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(12)
                    .ToList();
                preferences.LiveChannelWatchCountsByKey = preferences.LiveChannelWatchCountsByKey
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                    .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value), StringComparer.OrdinalIgnoreCase);
                preferences.LiveChannelWatchCounts = preferences.LiveChannelWatchCounts
                    .Where(pair => pair.Key > 0 && pair.Value > 0)
                    .GroupBy(pair => pair.Key)
                    .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value));
                preferences.HiddenCategoryKeys = preferences.HiddenCategoryKeys
                    .Select(NormalizeCategoryKey)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                preferences.CategoryRemaps = preferences.CategoryRemaps
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .Select(pair => new KeyValuePair<string, string>(NormalizeCategoryKey(pair.Key), pair.Value?.Trim() ?? string.Empty))
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
                return preferences;
            }
            catch
            {
                return new BrowsePreferences();
            }
        }

        public async Task SaveAsync(AppDbContext db, string domain, int profileId, BrowsePreferences preferences)
        {
            var key = BuildKey(domain, profileId);
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

        public async Task RepairSourceReferencesAsync(AppDbContext db, int? sourceProfileId = null)
        {
            var validSourceIds = (await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => profile.Id)
                .ToListAsync())
                .ToHashSet();
            var validChannelIds = (await db.Channels
                .AsNoTracking()
                .Select(channel => channel.Id)
                .ToListAsync())
                .ToHashSet();
            var settings = await db.AppSettings
                .Where(setting => setting.Key.StartsWith(KeyPrefix))
                .ToListAsync();

            var changed = false;
            foreach (var setting in settings)
            {
                if (string.IsNullOrWhiteSpace(setting.Value))
                {
                    continue;
                }

                BrowsePreferences preferences;
                try
                {
                    preferences = JsonSerializer.Deserialize<BrowsePreferences>(setting.Value, JsonOptions) ?? new BrowsePreferences();
                }
                catch
                {
                    continue;
                }

                if (!RepairSourceReferences(preferences, validSourceIds, validChannelIds, sourceProfileId))
                {
                    continue;
                }

                setting.Value = JsonSerializer.Serialize(Normalize(preferences), JsonOptions);
                changed = true;
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }

        public string NormalizeCategoryKey(string? categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName)
                ? "uncategorized"
                : ContentClassifier.NormalizeLabel(categoryName).Trim().ToLowerInvariant();
        }

        public string GetEffectiveCategoryName(BrowsePreferences preferences, string? categoryName)
        {
            return GetEffectiveCategoryName(preferences, categoryName, categoryName);
        }

        public string GetEffectiveCategoryName(BrowsePreferences preferences, string? categoryName, string? defaultDisplayCategoryName)
        {
            var normalizedKey = NormalizeCategoryKey(categoryName);
            if (preferences.CategoryRemaps.TryGetValue(normalizedKey, out var remapped) &&
                !string.IsNullOrWhiteSpace(remapped))
            {
                return remapped.Trim();
            }

            if (!string.IsNullOrWhiteSpace(defaultDisplayCategoryName))
            {
                return defaultDisplayCategoryName.Trim();
            }

            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        public bool IsCategoryHidden(BrowsePreferences preferences, string? categoryName)
        {
            var normalizedKey = NormalizeCategoryKey(categoryName);
            return preferences.HiddenCategoryKeys.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase);
        }

        private static BrowsePreferences Normalize(BrowsePreferences preferences)
        {
            var normalized = new BrowsePreferences
            {
                SortKey = preferences.SortKey?.Trim() ?? string.Empty,
                SelectedCategoryKey = string.IsNullOrWhiteSpace(preferences.SelectedCategoryKey)
                    ? string.Empty
                    : ContentClassifier.NormalizeLabel(preferences.SelectedCategoryKey).Trim().ToLowerInvariant(),
                SelectedSourceId = Math.Max(0, preferences.SelectedSourceId),
                IsAdvancedControlsExpanded = preferences.IsAdvancedControlsExpanded,
                LastChannel = NormalizeChannelReference(preferences.LastChannel),
                LastChannelId = Math.Max(0, preferences.LastChannelId),
                HasExplicitLiveSortPreference = preferences.HasExplicitLiveSortPreference,
                FavoritesOnly = preferences.FavoritesOnly,
                HideSecondaryContent = preferences.HideSecondaryContent,
                GuideMatchedOnly = preferences.GuideMatchedOnly
            };

            normalized.HiddenSourceIds = preferences.HiddenSourceIds
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            normalized.RecentChannels = preferences.RecentChannels
                .Select(NormalizeChannelReference)
                .Where(reference => reference != null)
                .Cast<BrowseChannelReference>()
                .GroupBy(reference => reference.LogicalKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(12)
                .ToList();

            normalized.RecentChannelIds = preferences.RecentChannelIds
                .Where(id => id > 0)
                .Distinct()
                .Take(12)
                .ToList();

            normalized.LiveChannelWatchCountsByKey = preferences.LiveChannelWatchCountsByKey
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value), StringComparer.OrdinalIgnoreCase);

            normalized.LiveChannelWatchCounts = preferences.LiveChannelWatchCounts
                .Where(pair => pair.Key > 0 && pair.Value > 0)
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value));

            normalized.HiddenCategoryKeys = preferences.HiddenCategoryKeys
                .Select(value => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            normalized.CategoryRemaps = preferences.CategoryRemaps
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new KeyValuePair<string, string>(
                    string.IsNullOrWhiteSpace(pair.Key) ? string.Empty : pair.Key.Trim().ToLowerInvariant(),
                    pair.Value?.Trim() ?? string.Empty))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

            return normalized;
        }

        private static bool RepairSourceReferences(
            BrowsePreferences preferences,
            IReadOnlySet<int> validSourceIds,
            IReadOnlySet<int> validChannelIds,
            int? sourceProfileId)
        {
            var changed = false;
            if (preferences.SelectedSourceId > 0 &&
                (!validSourceIds.Contains(preferences.SelectedSourceId) ||
                 sourceProfileId.HasValue && preferences.SelectedSourceId == sourceProfileId.Value))
            {
                preferences.SelectedSourceId = 0;
                changed = true;
            }

            var hiddenSourceIds = preferences.HiddenSourceIds
                .Where(id => id > 0 &&
                             validSourceIds.Contains(id) &&
                             (!sourceProfileId.HasValue || id != sourceProfileId.Value))
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            if (!preferences.HiddenSourceIds.SequenceEqual(hiddenSourceIds))
            {
                preferences.HiddenSourceIds = hiddenSourceIds;
                changed = true;
            }

            if (RepairChannelReference(preferences.LastChannel, validSourceIds, sourceProfileId))
            {
                changed = true;
            }

            foreach (var reference in preferences.RecentChannels)
            {
                if (RepairChannelReference(reference, validSourceIds, sourceProfileId))
                {
                    changed = true;
                }
            }

            if (preferences.LastChannelId > 0 && !validChannelIds.Contains(preferences.LastChannelId))
            {
                preferences.LastChannelId = 0;
                changed = true;
            }

            var recentChannelIds = preferences.RecentChannelIds
                .Where(id => id > 0 && validChannelIds.Contains(id))
                .Distinct()
                .Take(12)
                .ToList();
            if (!preferences.RecentChannelIds.SequenceEqual(recentChannelIds))
            {
                preferences.RecentChannelIds = recentChannelIds;
                changed = true;
            }

            var liveChannelWatchCounts = preferences.LiveChannelWatchCounts
                .Where(pair => pair.Key > 0 && pair.Value > 0 && validChannelIds.Contains(pair.Key))
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value));
            if (!DictionariesEqual(preferences.LiveChannelWatchCounts, liveChannelWatchCounts))
            {
                preferences.LiveChannelWatchCounts = liveChannelWatchCounts;
                changed = true;
            }

            return changed;
        }

        private static bool RepairChannelReference(
            BrowseChannelReference? reference,
            IReadOnlySet<int> validSourceIds,
            int? sourceProfileId)
        {
            if (reference == null || reference.PreferredSourceProfileId <= 0)
            {
                return false;
            }

            if (validSourceIds.Contains(reference.PreferredSourceProfileId) &&
                (!sourceProfileId.HasValue || reference.PreferredSourceProfileId != sourceProfileId.Value))
            {
                return false;
            }

            reference.PreferredSourceProfileId = 0;
            return true;
        }

        private static bool DictionariesEqual<TKey>(
            IReadOnlyDictionary<TKey, int> left,
            IReadOnlyDictionary<TKey, int> right)
            where TKey : notnull
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static string BuildKey(string domain, int profileId)
        {
            var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? "General" : domain.Trim();
            return $"{KeyPrefix}{normalizedDomain}.Profile.{Math.Max(0, profileId)}";
        }

        private static BrowseChannelReference? NormalizeChannelReference(BrowseChannelReference? reference)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.LogicalKey))
            {
                return null;
            }

            return new BrowseChannelReference
            {
                LogicalKey = reference.LogicalKey.Trim(),
                PreferredSourceProfileId = Math.Max(0, reference.PreferredSourceProfileId)
            };
        }
    }
}
