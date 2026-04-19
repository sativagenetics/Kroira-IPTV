#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public sealed partial class BackupPackageService
    {
        private static readonly Regex BackupKeyRegex = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static async Task<CatalogLocatorSnapshot> LoadCatalogLocatorSnapshotAsync(AppDbContext db)
        {
            var channels = await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new ChannelLocatorCandidate(
                        channel.Id,
                        category.SourceProfileId,
                        channel.Name,
                        channel.StreamUrl,
                        channel.EpgChannelId))
                .OrderBy(item => item.Id)
                .ToDictionaryAsync(item => item.Id);

            var movies = await db.Movies
                .AsNoTracking()
                .OrderBy(item => item.Id)
                .Select(item => new MovieLocatorCandidate(
                    item.Id,
                    item.SourceProfileId,
                    item.Title,
                    item.ExternalId,
                    item.StreamUrl,
                    item.CanonicalTitleKey,
                    item.DedupFingerprint))
                .ToDictionaryAsync(item => item.Id);

            var series = await db.Series
                .AsNoTracking()
                .OrderBy(item => item.Id)
                .Select(item => new SeriesLocatorCandidate(
                    item.Id,
                    item.SourceProfileId,
                    item.Title,
                    item.ExternalId,
                    item.CanonicalTitleKey,
                    item.DedupFingerprint))
                .ToDictionaryAsync(item => item.Id);

            var episodes = await db.Episodes
                .AsNoTracking()
                .Join(
                    db.Seasons.AsNoTracking(),
                    episode => episode.SeasonId,
                    season => season.Id,
                    (episode, season) => new { episode, season })
                .Join(
                    db.Series.AsNoTracking(),
                    pair => pair.season.SeriesId,
                    seriesItem => seriesItem.Id,
                    (pair, seriesItem) => new EpisodeLocatorCandidate(
                        pair.episode.Id,
                        seriesItem.SourceProfileId,
                        pair.episode.Title,
                        pair.episode.ExternalId,
                        pair.episode.StreamUrl,
                        pair.season.SeasonNumber,
                        pair.episode.EpisodeNumber,
                        seriesItem.Title,
                        seriesItem.ExternalId,
                        seriesItem.CanonicalTitleKey,
                        seriesItem.DedupFingerprint))
                .OrderBy(item => item.Id)
                .ToDictionaryAsync(item => item.Id);

            return new CatalogLocatorSnapshot(channels, movies, series, episodes);
        }

        private static BackupContentLocator BuildChannelLocator(ChannelLocatorCandidate candidate)
        {
            return new BackupContentLocator
            {
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                StreamUrl = candidate.StreamUrl,
                EpgChannelId = candidate.EpgChannelId
            };
        }

        private static BackupContentLocator BuildMovieLocator(MovieLocatorCandidate candidate)
        {
            return new BackupContentLocator
            {
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                ExternalId = candidate.ExternalId,
                StreamUrl = candidate.StreamUrl,
                CanonicalTitleKey = candidate.CanonicalTitleKey,
                DedupFingerprint = candidate.DedupFingerprint
            };
        }

        private static BackupContentLocator BuildSeriesLocator(SeriesLocatorCandidate candidate)
        {
            return new BackupContentLocator
            {
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                ExternalId = candidate.ExternalId,
                CanonicalTitleKey = candidate.CanonicalTitleKey,
                DedupFingerprint = candidate.DedupFingerprint
            };
        }

        private static BackupContentLocator BuildEpisodeLocator(EpisodeLocatorCandidate candidate)
        {
            return new BackupContentLocator
            {
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                ExternalId = candidate.ExternalId,
                StreamUrl = candidate.StreamUrl,
                SeriesTitle = candidate.SeriesTitle,
                SeriesExternalId = candidate.SeriesExternalId,
                SeriesCanonicalTitleKey = candidate.SeriesCanonicalTitleKey,
                SeriesDedupFingerprint = candidate.SeriesDedupFingerprint,
                SeasonNumber = candidate.SeasonNumber,
                EpisodeNumber = candidate.EpisodeNumber
            };
        }

        private static int ResolveChannelId(CatalogLocatorSnapshot snapshot, BackupContentLocator locator)
        {
            var candidates = FilterBySource(snapshot.Channels.Values, locator.SourceProfileId);

            var streamUrl = NormalizeUrl(locator.StreamUrl);
            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                var match = candidates.FirstOrDefault(item => NormalizeUrl(item.StreamUrl) == streamUrl);
                if (match != null) return match.Id;
            }

            var epgChannelId = NormalizeValue(locator.EpgChannelId);
            if (!string.IsNullOrWhiteSpace(epgChannelId))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.EpgChannelId) == epgChannelId);
                if (match != null) return match.Id;
            }

            var titleKey = NormalizeTextKey(locator.Title);
            if (!string.IsNullOrWhiteSpace(titleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.Title) == titleKey);
                if (match != null) return match.Id;
            }

            return 0;
        }

        private static int ResolveMovieId(CatalogLocatorSnapshot snapshot, BackupContentLocator locator)
        {
            var candidates = FilterBySource(snapshot.Movies.Values, locator.SourceProfileId);

            var externalId = NormalizeValue(locator.ExternalId);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.ExternalId) == externalId);
                if (match != null) return match.Id;
            }

            var streamUrl = NormalizeUrl(locator.StreamUrl);
            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                var match = candidates.FirstOrDefault(item => NormalizeUrl(item.StreamUrl) == streamUrl);
                if (match != null) return match.Id;
            }

            var fingerprint = NormalizeValue(locator.DedupFingerprint);
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.DedupFingerprint) == fingerprint);
                if (match != null) return match.Id;
            }

            var canonicalTitleKey = NormalizeTextKey(locator.CanonicalTitleKey);
            if (!string.IsNullOrWhiteSpace(canonicalTitleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.CanonicalTitleKey) == canonicalTitleKey);
                if (match != null) return match.Id;
            }

            var titleKey = NormalizeTextKey(locator.Title);
            if (!string.IsNullOrWhiteSpace(titleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.Title) == titleKey);
                if (match != null) return match.Id;
            }

            return 0;
        }

        private static int ResolveSeriesId(CatalogLocatorSnapshot snapshot, BackupContentLocator locator)
        {
            var candidates = FilterBySource(snapshot.Series.Values, locator.SourceProfileId);

            var externalId = NormalizeValue(locator.ExternalId);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.ExternalId) == externalId);
                if (match != null) return match.Id;
            }

            var fingerprint = NormalizeValue(locator.DedupFingerprint);
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.DedupFingerprint) == fingerprint);
                if (match != null) return match.Id;
            }

            var canonicalTitleKey = NormalizeTextKey(locator.CanonicalTitleKey);
            if (!string.IsNullOrWhiteSpace(canonicalTitleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.CanonicalTitleKey) == canonicalTitleKey);
                if (match != null) return match.Id;
            }

            var titleKey = NormalizeTextKey(locator.Title);
            if (!string.IsNullOrWhiteSpace(titleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.Title) == titleKey);
                if (match != null) return match.Id;
            }

            return 0;
        }

        private static int ResolveEpisodeId(CatalogLocatorSnapshot snapshot, BackupContentLocator locator)
        {
            var candidates = FilterBySource(snapshot.Episodes.Values, locator.SourceProfileId);

            var externalId = NormalizeValue(locator.ExternalId);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var match = candidates.FirstOrDefault(item => NormalizeValue(item.ExternalId) == externalId);
                if (match != null) return match.Id;
            }

            var streamUrl = NormalizeUrl(locator.StreamUrl);
            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                var match = candidates.FirstOrDefault(item => NormalizeUrl(item.StreamUrl) == streamUrl);
                if (match != null) return match.Id;
            }

            if (locator.SeasonNumber > 0 && locator.EpisodeNumber > 0)
            {
                var seasonEpisodeMatches = candidates
                    .Where(item => item.SeasonNumber == locator.SeasonNumber && item.EpisodeNumber == locator.EpisodeNumber)
                    .ToList();

                var seriesExternalId = NormalizeValue(locator.SeriesExternalId);
                if (!string.IsNullOrWhiteSpace(seriesExternalId))
                {
                    var match = seasonEpisodeMatches.FirstOrDefault(item => NormalizeValue(item.SeriesExternalId) == seriesExternalId);
                    if (match != null) return match.Id;
                }

                var seriesFingerprint = NormalizeValue(locator.SeriesDedupFingerprint);
                if (!string.IsNullOrWhiteSpace(seriesFingerprint))
                {
                    var match = seasonEpisodeMatches.FirstOrDefault(item => NormalizeValue(item.SeriesDedupFingerprint) == seriesFingerprint);
                    if (match != null) return match.Id;
                }

                var seriesCanonicalTitle = NormalizeTextKey(locator.SeriesCanonicalTitleKey);
                if (!string.IsNullOrWhiteSpace(seriesCanonicalTitle))
                {
                    var match = seasonEpisodeMatches.FirstOrDefault(item => NormalizeTextKey(item.SeriesCanonicalTitleKey) == seriesCanonicalTitle);
                    if (match != null) return match.Id;
                }

                var seriesTitleKey = NormalizeTextKey(locator.SeriesTitle);
                if (!string.IsNullOrWhiteSpace(seriesTitleKey))
                {
                    var match = seasonEpisodeMatches.FirstOrDefault(item => NormalizeTextKey(item.SeriesTitle) == seriesTitleKey);
                    if (match != null) return match.Id;
                }
            }

            var titleKey = NormalizeTextKey(locator.Title);
            if (!string.IsNullOrWhiteSpace(titleKey))
            {
                var match = candidates.FirstOrDefault(item => NormalizeTextKey(item.Title) == titleKey);
                if (match != null) return match.Id;
            }

            return 0;
        }

        private static IEnumerable<T> FilterBySource<T>(IEnumerable<T> values, int sourceProfileId)
            where T : IHasSourceProfileId
        {
            return sourceProfileId > 0
                ? values.Where(item => item.SourceProfileId == sourceProfileId)
                : values;
        }

        private static string NormalizeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var queryIndex = trimmed.IndexOf('?');
            if (queryIndex >= 0)
            {
                trimmed = trimmed[..queryIndex];
            }

            return trimmed.TrimEnd('/').ToLowerInvariant();
        }

        private static string NormalizeValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeTextKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var tokens = BackupKeyRegex
                .Matches(RemoveDiacritics(value).ToLowerInvariant())
                .Select(match => match.Value)
                .Where(token => token.Length > 0);

            return string.Join(" ", tokens);
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private interface IHasSourceProfileId
        {
            int SourceProfileId { get; }
        }

        private sealed record CatalogLocatorSnapshot(
            IReadOnlyDictionary<int, ChannelLocatorCandidate> Channels,
            IReadOnlyDictionary<int, MovieLocatorCandidate> Movies,
            IReadOnlyDictionary<int, SeriesLocatorCandidate> Series,
            IReadOnlyDictionary<int, EpisodeLocatorCandidate> Episodes);

        private sealed record ChannelLocatorCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string StreamUrl,
            string EpgChannelId) : IHasSourceProfileId;

        private sealed record MovieLocatorCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string ExternalId,
            string StreamUrl,
            string CanonicalTitleKey,
            string DedupFingerprint) : IHasSourceProfileId;

        private sealed record SeriesLocatorCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string ExternalId,
            string CanonicalTitleKey,
            string DedupFingerprint) : IHasSourceProfileId;

        private sealed record EpisodeLocatorCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string ExternalId,
            string StreamUrl,
            int SeasonNumber,
            int EpisodeNumber,
            string SeriesTitle,
            string SeriesExternalId,
            string SeriesCanonicalTitleKey,
            string SeriesDedupFingerprint) : IHasSourceProfileId;
    }
}
