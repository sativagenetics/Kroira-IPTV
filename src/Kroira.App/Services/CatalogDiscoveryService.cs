#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public interface ICatalogDiscoveryService
    {
        CatalogDiscoveryProjection BuildProjection(
            CatalogDiscoveryDomain domain,
            IReadOnlyList<CatalogDiscoveryRecord> records,
            CatalogDiscoverySelection? selection,
            DateTime nowUtc);

        CatalogDiscoveryHealthBucket ResolveHealthBucket(SourceHealthState? state);
        string ResolveLanguageKey(string? value);
        string ResolveLanguageLabel(string? value);
        IReadOnlyList<CatalogDiscoveryTag> ExtractTags(string? value);
        string ResolveSourceTypeKey(SourceType sourceType);
        string ResolveSourceTypeLabel(SourceType sourceType);
    }

    public sealed class CatalogDiscoveryService : ICatalogDiscoveryService
    {
        private const string AllKey = "all";
        private static readonly TimeSpan RecentSyncWindow = TimeSpan.FromDays(7);
        private static readonly TimeSpan RecentInteractionWindow = TimeSpan.FromDays(10);

        public CatalogDiscoveryProjection BuildProjection(
            CatalogDiscoveryDomain domain,
            IReadOnlyList<CatalogDiscoveryRecord> records,
            CatalogDiscoverySelection? selection,
            DateTime nowUtc)
        {
            var scopedRecords = records
                .Where(record => record.Domain == domain && !string.IsNullOrWhiteSpace(record.Key))
                .ToList();
            var workingSelection = NormalizeSelection(selection);

            for (var pass = 0; pass < 4; pass++)
            {
                var signalOptions = BuildSignalOptions(domain, scopedRecords, workingSelection, nowUtc);
                var sourceTypeOptions = BuildSourceTypeOptions(scopedRecords, workingSelection, nowUtc);
                var languageOptions = BuildLanguageOptions(scopedRecords, workingSelection, nowUtc);
                var tagOptions = BuildTagOptions(domain, scopedRecords, workingSelection, nowUtc);
                var changed = false;

                var signalKey = workingSelection.SignalKey;
                var sourceTypeKey = workingSelection.SourceTypeKey;
                var languageKey = workingSelection.LanguageKey;
                var tagKey = workingSelection.TagKey;

                changed |= EnsureValidSelection(signalOptions, ref signalKey);
                changed |= EnsureValidSelection(sourceTypeOptions, ref sourceTypeKey);
                changed |= EnsureValidSelection(languageOptions, ref languageKey);
                changed |= EnsureValidSelection(tagOptions, ref tagKey);

                workingSelection.SignalKey = signalKey;
                workingSelection.SourceTypeKey = sourceTypeKey;
                workingSelection.LanguageKey = languageKey;
                workingSelection.TagKey = tagKey;

                if (changed)
                {
                    continue;
                }

                var matchingRecords = scopedRecords
                    .Where(record => MatchesAll(record, domain, workingSelection, nowUtc))
                    .ToList();
                var matchingKeys = matchingRecords
                    .Select(record => record.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return new CatalogDiscoveryProjection
                {
                    EffectiveSelection = new CatalogDiscoverySelection
                    {
                        SignalKey = workingSelection.SignalKey,
                        SourceTypeKey = workingSelection.SourceTypeKey,
                        LanguageKey = workingSelection.LanguageKey,
                        TagKey = workingSelection.TagKey
                    },
                    SignalOptions = signalOptions,
                    SourceTypeOptions = sourceTypeOptions,
                    LanguageOptions = languageOptions,
                    TagOptions = tagOptions,
                    MatchingKeys = matchingKeys,
                    HasActiveFacetFilters = HasActiveFacetFilters(workingSelection),
                    MatchingCount = matchingRecords.Count,
                    ProviderCount = matchingRecords
                        .SelectMany(record => record.SourceProfileIds)
                        .Where(id => id > 0)
                        .Distinct()
                        .Count(),
                    SummaryText = BuildSummaryText(domain, workingSelection, signalOptions, sourceTypeOptions, languageOptions, tagOptions)
                };
            }

            return new CatalogDiscoveryProjection
            {
                EffectiveSelection = new CatalogDiscoverySelection(),
                SignalOptions = BuildSignalOptions(domain, scopedRecords, new MutableSelection(), nowUtc),
                SourceTypeOptions = BuildSourceTypeOptions(scopedRecords, new MutableSelection(), nowUtc),
                LanguageOptions = BuildLanguageOptions(scopedRecords, new MutableSelection(), nowUtc),
                TagOptions = BuildTagOptions(domain, scopedRecords, new MutableSelection(), nowUtc),
                MatchingKeys = scopedRecords.Select(record => record.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
                MatchingCount = scopedRecords.Count,
                ProviderCount = scopedRecords.SelectMany(record => record.SourceProfileIds).Where(id => id > 0).Distinct().Count(),
                SummaryText = BuildDefaultSummaryText(domain)
            };
        }

        public CatalogDiscoveryHealthBucket ResolveHealthBucket(SourceHealthState? state)
        {
            return state switch
            {
                SourceHealthState.Healthy or SourceHealthState.Good => CatalogDiscoveryHealthBucket.Healthy,
                SourceHealthState.Weak or SourceHealthState.Incomplete or SourceHealthState.Outdated => CatalogDiscoveryHealthBucket.Attention,
                SourceHealthState.Problematic => CatalogDiscoveryHealthBucket.Degraded,
                _ => CatalogDiscoveryHealthBucket.Unknown
            };
        }

        public string ResolveLanguageKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().ToLowerInvariant();
        }

        public string ResolveLanguageLabel(string? value)
        {
            var key = ResolveLanguageKey(value);
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                return CultureInfo.GetCultureInfo(key).EnglishName;
            }
            catch
            {
                return key.Length <= 3
                    ? key.ToUpperInvariant()
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key);
            }
        }

        public IReadOnlyList<CatalogDiscoveryTag> ExtractTags(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<CatalogDiscoveryTag>();
            }

            var tags = value
                .Split([',', ';', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(raw => raw.Trim())
                .Where(raw => !string.IsNullOrWhiteSpace(raw))
                .Select(raw => new CatalogDiscoveryTag
                {
                    Key = NormalizeFacetKey(raw),
                    Label = NormalizeFacetLabel(raw)
                })
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Key) && !string.IsNullOrWhiteSpace(tag.Label))
                .GroupBy(tag => tag.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(6)
                .ToList();

            return tags;
        }

        public string ResolveSourceTypeKey(SourceType sourceType)
        {
            return sourceType.ToString().ToLowerInvariant();
        }

        public string ResolveSourceTypeLabel(SourceType sourceType)
        {
            return sourceType switch
            {
                SourceType.M3U => "M3U",
                SourceType.Xtream => "Xtream",
                SourceType.Stalker => "Stalker",
                _ => sourceType.ToString()
            };
        }

        private static IReadOnlyList<CatalogDiscoveryFacetOption> BuildSignalOptions(
            CatalogDiscoveryDomain domain,
            IReadOnlyList<CatalogDiscoveryRecord> records,
            MutableSelection selection,
            DateTime nowUtc)
        {
            var definitions = GetSignalDefinitions(domain);
            var options = new List<CatalogDiscoveryFacetOption>
            {
                new()
                {
                    Key = AllKey,
                    Label = GetAllSignalLabel(domain),
                    ItemCount = records.Count(record => MatchesSourceType(record, selection.SourceTypeKey) &&
                                                        MatchesLanguage(record, selection.LanguageKey) &&
                                                        MatchesTag(record, selection.TagKey))
                }
            };

            foreach (var definition in definitions)
            {
                var count = records.Count(record => definition.Matches(record, nowUtc) &&
                                                    MatchesSourceType(record, selection.SourceTypeKey) &&
                                                    MatchesLanguage(record, selection.LanguageKey) &&
                                                    MatchesTag(record, selection.TagKey));
                if (count <= 0)
                {
                    continue;
                }

                options.Add(new CatalogDiscoveryFacetOption
                {
                    Key = definition.Key,
                    Label = definition.Label,
                    ItemCount = count
                });
            }

            return options;
        }

        private IReadOnlyList<CatalogDiscoveryFacetOption> BuildSourceTypeOptions(
            IReadOnlyList<CatalogDiscoveryRecord> records,
            MutableSelection selection,
            DateTime nowUtc)
        {
            var options = new List<CatalogDiscoveryFacetOption>
            {
                new()
                {
                    Key = AllKey,
                    Label = "All source types",
                    ItemCount = records.Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                                        MatchesLanguage(record, selection.LanguageKey) &&
                                                        MatchesTag(record, selection.TagKey))
                }
            };

            foreach (var group in records
                         .SelectMany(record => record.SourceTypes.Distinct().Select(type => new { Type = type, Record = record }))
                         .GroupBy(item => item.Type)
                         .OrderBy(group => ResolveSourceTypeLabel(group.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                var count = group
                    .Select(item => item.Record)
                    .Distinct()
                    .Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                     MatchesLanguage(record, selection.LanguageKey) &&
                                     MatchesTag(record, selection.TagKey));
                if (count <= 0)
                {
                    continue;
                }

                options.Add(new CatalogDiscoveryFacetOption
                {
                    Key = ResolveSourceTypeKey(group.Key),
                    Label = ResolveSourceTypeLabel(group.Key),
                    ItemCount = count
                });
            }

            return options;
        }

        private static IReadOnlyList<CatalogDiscoveryFacetOption> BuildLanguageOptions(
            IReadOnlyList<CatalogDiscoveryRecord> records,
            MutableSelection selection,
            DateTime nowUtc)
        {
            var options = new List<CatalogDiscoveryFacetOption>
            {
                new()
                {
                    Key = AllKey,
                    Label = "All languages",
                    ItemCount = records.Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                                        MatchesSourceType(record, selection.SourceTypeKey) &&
                                                        MatchesTag(record, selection.TagKey))
                }
            };

            foreach (var group in records
                         .Where(record => !string.IsNullOrWhiteSpace(record.LanguageKey) && !string.IsNullOrWhiteSpace(record.LanguageLabel))
                         .GroupBy(record => record.LanguageKey, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.First().LanguageLabel, StringComparer.CurrentCultureIgnoreCase))
            {
                var label = group.First().LanguageLabel;
                var count = group.Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                                  MatchesSourceType(record, selection.SourceTypeKey) &&
                                                  MatchesTag(record, selection.TagKey));
                if (count <= 0)
                {
                    continue;
                }

                options.Add(new CatalogDiscoveryFacetOption
                {
                    Key = group.Key,
                    Label = label,
                    ItemCount = count
                });
            }

            return options;
        }

        private static IReadOnlyList<CatalogDiscoveryFacetOption> BuildTagOptions(
            CatalogDiscoveryDomain domain,
            IReadOnlyList<CatalogDiscoveryRecord> records,
            MutableSelection selection,
            DateTime nowUtc)
        {
            var options = new List<CatalogDiscoveryFacetOption>
            {
                new()
                {
                    Key = AllKey,
                    Label = domain == CatalogDiscoveryDomain.Live ? "All shelves" : "All genres",
                    ItemCount = records.Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                                        MatchesSourceType(record, selection.SourceTypeKey) &&
                                                        MatchesLanguage(record, selection.LanguageKey))
                }
            };

            foreach (var group in records
                         .SelectMany(record => record.Tags.DistinctBy(tag => tag.Key).Select(tag => new { Tag = tag, Record = record }))
                         .GroupBy(item => item.Tag.Key, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.First().Tag.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var label = group.First().Tag.Label;
                var count = group
                    .Select(item => item.Record)
                    .Distinct()
                    .Count(record => MatchesSignal(record, selection.SignalKey, nowUtc) &&
                                     MatchesSourceType(record, selection.SourceTypeKey) &&
                                     MatchesLanguage(record, selection.LanguageKey));
                if (count <= 0)
                {
                    continue;
                }

                options.Add(new CatalogDiscoveryFacetOption
                {
                    Key = group.Key,
                    Label = label,
                    ItemCount = count
                });
            }

            return options;
        }

        private static bool EnsureValidSelection(IReadOnlyList<CatalogDiscoveryFacetOption> options, ref string key)
        {
            var normalizedKey = NormalizeSelectionKey(key);
            if (string.Equals(normalizedKey, AllKey, StringComparison.OrdinalIgnoreCase) ||
                options.Any(option => string.Equals(option.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)))
            {
                key = normalizedKey;
                return false;
            }

            key = AllKey;
            return true;
        }

        private static bool HasActiveFacetFilters(MutableSelection selection)
        {
            return !string.Equals(selection.SignalKey, AllKey, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(selection.SourceTypeKey, AllKey, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(selection.LanguageKey, AllKey, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(selection.TagKey, AllKey, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSummaryText(
            CatalogDiscoveryDomain domain,
            MutableSelection selection,
            IReadOnlyList<CatalogDiscoveryFacetOption> signalOptions,
            IReadOnlyList<CatalogDiscoveryFacetOption> sourceTypeOptions,
            IReadOnlyList<CatalogDiscoveryFacetOption> languageOptions,
            IReadOnlyList<CatalogDiscoveryFacetOption> tagOptions)
        {
            var activeLabels = new List<string>();
            TryAddSelectionLabel(activeLabels, selection.SignalKey, signalOptions);
            TryAddSelectionLabel(activeLabels, selection.SourceTypeKey, sourceTypeOptions);
            TryAddSelectionLabel(activeLabels, selection.LanguageKey, languageOptions);
            TryAddSelectionLabel(activeLabels, selection.TagKey, tagOptions);

            if (activeLabels.Count == 0)
            {
                return BuildDefaultSummaryText(domain);
            }

            return $"Focused by {string.Join(" / ", activeLabels)}.";
        }

        private static string BuildDefaultSummaryText(CatalogDiscoveryDomain domain)
        {
            return domain switch
            {
                CatalogDiscoveryDomain.Live => "Guide, catchup, source type, and health stay quiet until sync evidence proves them.",
                CatalogDiscoveryDomain.Series => "Source type, language, genre, episode-ready, and health appear only when the catalog proves them.",
                _ => "Source type, language, genre, artwork, and health appear only when the catalog proves them."
            };
        }

        private static void TryAddSelectionLabel(
            ICollection<string> labels,
            string selectedKey,
            IReadOnlyList<CatalogDiscoveryFacetOption> options)
        {
            if (string.Equals(selectedKey, AllKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var label = options.FirstOrDefault(option => string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))?.Label;
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.Add(label);
            }
        }

        private static bool MatchesAll(
            CatalogDiscoveryRecord record,
            CatalogDiscoveryDomain domain,
            MutableSelection selection,
            DateTime nowUtc)
        {
            return MatchesSignal(record, selection.SignalKey, nowUtc) &&
                   MatchesSourceType(record, selection.SourceTypeKey) &&
                   MatchesLanguage(record, selection.LanguageKey) &&
                   MatchesTag(record, selection.TagKey);
        }

        private static bool MatchesSignal(CatalogDiscoveryRecord record, string signalKey, DateTime nowUtc)
        {
            if (string.Equals(signalKey, AllKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var definition in GetSignalDefinitions(record.Domain))
            {
                if (string.Equals(definition.Key, signalKey, StringComparison.OrdinalIgnoreCase))
                {
                    return definition.Matches(record, nowUtc);
                }
            }

            return false;
        }

        private static bool MatchesSourceType(CatalogDiscoveryRecord record, string sourceTypeKey)
        {
            if (string.Equals(sourceTypeKey, AllKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return record.SourceTypes.Any(type =>
                string.Equals(type.ToString(), sourceTypeKey, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesLanguage(CatalogDiscoveryRecord record, string languageKey)
        {
            if (string.Equals(languageKey, AllKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(record.LanguageKey, languageKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesTag(CatalogDiscoveryRecord record, string tagKey)
        {
            if (string.Equals(tagKey, AllKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return record.Tags.Any(tag => string.Equals(tag.Key, tagKey, StringComparison.OrdinalIgnoreCase));
        }

        private static MutableSelection NormalizeSelection(CatalogDiscoverySelection? selection)
        {
            return new MutableSelection
            {
                SignalKey = NormalizeSelectionKey(selection?.SignalKey),
                SourceTypeKey = NormalizeSelectionKey(selection?.SourceTypeKey),
                LanguageKey = NormalizeSelectionKey(selection?.LanguageKey),
                TagKey = NormalizeSelectionKey(selection?.TagKey)
            };
        }

        private static string NormalizeSelectionKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? AllKey
                : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeFacetKey(string value)
        {
            return ContentClassifier.NormalizeLabel(value).Trim().ToLowerInvariant();
        }

        private static string NormalizeFacetLabel(string value)
        {
            var normalized = ContentClassifier.NormalizeLabel(value).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var letters = normalized.Where(char.IsLetter).ToList();
            if (letters.Count > 0 && letters.All(char.IsUpper))
            {
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
            }

            return normalized;
        }

        private static IReadOnlyList<SignalDefinition> GetSignalDefinitions(CatalogDiscoveryDomain domain)
        {
            return domain switch
            {
                CatalogDiscoveryDomain.Live =>
                [
                    new("favorites", "Favorites", (record, _) => record.IsFavorite),
                    new("catchup_ready", "Catchup ready", (record, _) => record.HasCatchup),
                    new("guide_linked", "Guide linked", (record, _) => record.HasGuide),
                    new("healthy_sources", "Healthy sources", (record, _) => record.HealthBucket == CatalogDiscoveryHealthBucket.Healthy),
                    new("needs_attention", "Needs attention", (record, _) => record.HealthBucket is CatalogDiscoveryHealthBucket.Attention or CatalogDiscoveryHealthBucket.Degraded),
                    new("recently_watched", "Recently watched", (record, nowUtc) =>
                        record.LastInteractionUtc.HasValue &&
                        nowUtc - record.LastInteractionUtc.Value <= RecentInteractionWindow)
                ],
                CatalogDiscoveryDomain.Series =>
                [
                    new("favorites", "Favorites", (record, _) => record.IsFavorite),
                    new("artwork_ready", "Artwork ready", (record, _) => record.HasArtwork),
                    new("episode_ready", "Episode ready", (record, _) => record.HasPlayableChildren),
                    new("healthy_sources", "Healthy sources", (record, _) => record.HealthBucket == CatalogDiscoveryHealthBucket.Healthy),
                    new("needs_attention", "Needs attention", (record, _) => record.HealthBucket is CatalogDiscoveryHealthBucket.Attention or CatalogDiscoveryHealthBucket.Degraded),
                    new("recently_synced", "Recently synced", (record, nowUtc) =>
                        record.LastSyncUtc.HasValue &&
                        nowUtc - record.LastSyncUtc.Value <= RecentSyncWindow)
                ],
                _ =>
                [
                    new("favorites", "Favorites", (record, _) => record.IsFavorite),
                    new("artwork_ready", "Artwork ready", (record, _) => record.HasArtwork),
                    new("healthy_sources", "Healthy sources", (record, _) => record.HealthBucket == CatalogDiscoveryHealthBucket.Healthy),
                    new("needs_attention", "Needs attention", (record, _) => record.HealthBucket is CatalogDiscoveryHealthBucket.Attention or CatalogDiscoveryHealthBucket.Degraded),
                    new("recently_synced", "Recently synced", (record, nowUtc) =>
                        record.LastSyncUtc.HasValue &&
                        nowUtc - record.LastSyncUtc.Value <= RecentSyncWindow)
                ]
            };
        }

        private static string GetAllSignalLabel(CatalogDiscoveryDomain domain)
        {
            return domain switch
            {
                CatalogDiscoveryDomain.Live => "All live",
                CatalogDiscoveryDomain.Series => "All series",
                _ => "All movies"
            };
        }

        private sealed class MutableSelection
        {
            public string SignalKey { get; set; } = AllKey;
            public string SourceTypeKey { get; set; } = AllKey;
            public string LanguageKey { get; set; } = AllKey;
            public string TagKey { get; set; } = AllKey;
        }

        private sealed record SignalDefinition(
            string Key,
            string Label,
            Func<CatalogDiscoveryRecord, DateTime, bool> Matches);
    }
}
