#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ICatalogDeduplicationService
    {
        Task<IReadOnlyList<CatalogMovieGroup>> LoadMovieGroupsAsync(AppDbContext db);
        Task<IReadOnlyList<CatalogSeriesGroup>> LoadSeriesGroupsAsync(AppDbContext db);
    }

    public sealed class CatalogMovieVariant
    {
        public required Movie Movie { get; init; }
        public required SourceProfile SourceProfile { get; init; }
        public string DisplayName => $"{SourceProfile.Name} ({SourceProfile.Type})";
    }

    public sealed class CatalogSeriesVariant
    {
        public required Series Series { get; init; }
        public required SourceProfile SourceProfile { get; init; }
        public string DisplayName => $"{SourceProfile.Name} ({SourceProfile.Type})";
        public int EpisodeCount =>
            Series.Seasons?.Sum(season => season.Episodes?.Count ?? 0) ?? 0;
    }

    public sealed class CatalogMovieGroup
    {
        public required string GroupKey { get; init; }
        public required Movie PreferredMovie { get; init; }
        public required IReadOnlyList<CatalogMovieVariant> Variants { get; init; }
        public string SourceSummary { get; init; } = string.Empty;
    }

    public sealed class CatalogSeriesGroup
    {
        public required string GroupKey { get; init; }
        public required Series PreferredSeries { get; init; }
        public required IReadOnlyList<CatalogSeriesVariant> Variants { get; init; }
        public string SourceSummary { get; init; } = string.Empty;
    }

    public sealed class CatalogDeduplicationService : ICatalogDeduplicationService
    {
        private readonly object _cacheLock = new();
        private CatalogCacheRevision? _movieCacheRevision;
        private IReadOnlyList<CatalogMovieGroup>? _movieGroupsCache;
        private CatalogCacheRevision? _seriesCacheRevision;
        private IReadOnlyList<CatalogSeriesGroup>? _seriesGroupsCache;

        private sealed record CatalogCacheRevision(
            int ItemCount,
            int MaxItemId,
            long ItemMetadataTicks,
            int SourceCount,
            int MaxSourceId,
            long SourceLastSyncTicks);

        public async Task<IReadOnlyList<CatalogMovieGroup>> LoadMovieGroupsAsync(AppDbContext db)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var phaseStopwatch = Stopwatch.StartNew();
            var revision = await BuildMovieRevisionAsync(db);
            Log("movies", $"revision ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            lock (_cacheLock)
            {
                if (_movieGroupsCache != null && Equals(_movieCacheRevision, revision))
                {
                    Log("movies", $"cache_hit groups={_movieGroupsCache.Count} total_ms={totalStopwatch.ElapsedMilliseconds}");
                    return _movieGroupsCache;
                }
            }

            phaseStopwatch.Restart();
            var movies = await db.Movies.AsNoTracking().ToListAsync();
            Log("movies", $"db_movies count={movies.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            phaseStopwatch.Restart();
            var sourceIds = movies.Select(movie => movie.SourceProfileId).Distinct().ToList();
            var sourceProfiles = await db.SourceProfiles
                .Where(profile => sourceIds.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id);
            Log("movies", $"db_sources count={sourceProfiles.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");

            phaseStopwatch.Restart();
            var groups = await Task.Run(() => BuildMovieGroups(movies, sourceProfiles));
            Log("movies", $"group_build groups={groups.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            lock (_cacheLock)
            {
                _movieCacheRevision = revision;
                _movieGroupsCache = groups;
            }

            return groups;
        }

        public async Task<IReadOnlyList<CatalogSeriesGroup>> LoadSeriesGroupsAsync(AppDbContext db)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var phaseStopwatch = Stopwatch.StartNew();
            var revision = await BuildSeriesRevisionAsync(db);
            Log("series", $"revision ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            lock (_cacheLock)
            {
                if (_seriesGroupsCache != null && Equals(_seriesCacheRevision, revision))
                {
                    Log("series", $"cache_hit groups={_seriesGroupsCache.Count} total_ms={totalStopwatch.ElapsedMilliseconds}");
                    return _seriesGroupsCache;
                }
            }

            phaseStopwatch.Restart();
            var series = await db.Series
                .AsNoTracking()
                .ToListAsync();
            Log("series", $"db_series count={series.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            phaseStopwatch.Restart();
            var sourceIds = series.Select(show => show.SourceProfileId).Distinct().ToList();
            var sourceProfiles = await db.SourceProfiles
                .Where(profile => sourceIds.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id);
            Log("series", $"db_sources count={sourceProfiles.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");

            phaseStopwatch.Restart();
            var groups = await Task.Run(() => BuildSeriesGroups(series, sourceProfiles));
            Log("series", $"group_build groups={groups.Count} ms={phaseStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
            lock (_cacheLock)
            {
                _seriesCacheRevision = revision;
                _seriesGroupsCache = groups;
            }

            return groups;
        }

        private static void Log(string media, string message)
        {
            BrowseRuntimeLogger.Log("CATALOG DEDUP", $"{media} {message}");
        }

        private static async Task<CatalogCacheRevision> BuildMovieRevisionAsync(AppDbContext db)
        {
            var movieState = await db.Movies
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    MaxId = group.Max(movie => (int?)movie.Id) ?? 0,
                    MetadataUpdatedAt = group.Max(movie => movie.MetadataUpdatedAt)
                })
                .SingleOrDefaultAsync();

            var sourceState = await db.SourceProfiles
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    MaxId = group.Max(profile => (int?)profile.Id) ?? 0,
                    LastSync = group.Max(profile => profile.LastSync)
                })
                .SingleOrDefaultAsync();

            return new CatalogCacheRevision(
                movieState?.Count ?? 0,
                movieState?.MaxId ?? 0,
                movieState?.MetadataUpdatedAt?.Ticks ?? 0,
                sourceState?.Count ?? 0,
                sourceState?.MaxId ?? 0,
                sourceState?.LastSync?.Ticks ?? 0);
        }

        private static async Task<CatalogCacheRevision> BuildSeriesRevisionAsync(AppDbContext db)
        {
            var seriesState = await db.Series
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    MaxId = group.Max(series => (int?)series.Id) ?? 0,
                    MetadataUpdatedAt = group.Max(series => series.MetadataUpdatedAt)
                })
                .SingleOrDefaultAsync();

            var sourceState = await db.SourceProfiles
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    MaxId = group.Max(profile => (int?)profile.Id) ?? 0,
                    LastSync = group.Max(profile => profile.LastSync)
                })
                .SingleOrDefaultAsync();

            return new CatalogCacheRevision(
                seriesState?.Count ?? 0,
                seriesState?.MaxId ?? 0,
                seriesState?.MetadataUpdatedAt?.Ticks ?? 0,
                sourceState?.Count ?? 0,
                sourceState?.MaxId ?? 0,
                sourceState?.LastSync?.Ticks ?? 0);
        }

        private static IReadOnlyList<CatalogMovieGroup> BuildMovieGroups(
            IReadOnlyList<Movie> movies,
            IReadOnlyDictionary<int, SourceProfile> sourceProfiles)
        {
            var groups = new List<CatalogMovieGroup>();
            foreach (var bucket in movies.GroupBy(movie => GetMovieBucketKey(movie)))
            {
                var bucketItems = bucket.ToList();
                if (bucketItems.Count <= 1 || !CanMergeMovies(bucketItems))
                {
                    groups.AddRange(bucketItems.Select(movie => BuildMovieGroup(new[] { movie }, sourceProfiles)));
                    continue;
                }

                groups.Add(BuildMovieGroup(bucketItems, sourceProfiles));
            }

            return groups;
        }

        private static IReadOnlyList<CatalogSeriesGroup> BuildSeriesGroups(
            IReadOnlyList<Series> series,
            IReadOnlyDictionary<int, SourceProfile> sourceProfiles)
        {
            var groups = new List<CatalogSeriesGroup>();
            foreach (var bucket in series.GroupBy(show => GetSeriesBucketKey(show)))
            {
                var bucketItems = bucket.ToList();
                if (bucketItems.Count <= 1 || !CanMergeSeries(bucketItems))
                {
                    groups.AddRange(bucketItems.Select(show => BuildSeriesGroup(new[] { show }, sourceProfiles)));
                    continue;
                }

                groups.Add(BuildSeriesGroup(bucketItems, sourceProfiles));
            }

            return groups;
        }

        private static string GetMovieBucketKey(Movie movie)
        {
            var result = CatalogFingerprinting.ComputeMovie(movie);
            return string.IsNullOrWhiteSpace(result.DedupFingerprint)
                ? $"movie:row:{movie.Id}"
                : result.DedupFingerprint;
        }

        private static string GetSeriesBucketKey(Series series)
        {
            var result = CatalogFingerprinting.ComputeSeries(series);
            return string.IsNullOrWhiteSpace(result.DedupFingerprint)
                ? $"series:row:{series.Id}"
                : result.DedupFingerprint;
        }

        private static bool CanMergeMovies(IReadOnlyList<Movie> movies)
        {
            var firstFingerprint = CatalogFingerprinting.ComputeMovie(movies[0]);
            if (firstFingerprint.DedupFingerprint.StartsWith("movie:source:", StringComparison.Ordinal))
            {
                return movies
                    .All(movie => movie.SourceProfileId == movies[0].SourceProfileId);
            }

            if (!firstFingerprint.IsStrong)
            {
                return false;
            }

            return HasConsistentMovieEvidence(movies, firstFingerprint);
        }

        private static bool CanMergeSeries(IReadOnlyList<Series> series)
        {
            var firstFingerprint = CatalogFingerprinting.ComputeSeries(series[0]);
            if (firstFingerprint.DedupFingerprint.StartsWith("series:source:", StringComparison.Ordinal))
            {
                return series
                    .All(show => show.SourceProfileId == series[0].SourceProfileId);
            }

            if (!firstFingerprint.IsStrong)
            {
                return false;
            }

            return HasConsistentSeriesEvidence(series, firstFingerprint);
        }

        private static bool HasConsistentMovieEvidence(IReadOnlyList<Movie> movies, CatalogFingerprintResult firstFingerprint)
        {
            var titleKeys = movies
                .Select(movie => CatalogFingerprinting.ComputeMovie(movie).CanonicalTitleKey)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (titleKeys.Count > 1)
            {
                return false;
            }

            var knownTmdbIds = movies.Where(movie => !string.IsNullOrWhiteSpace(movie.TmdbId)).Select(movie => movie.TmdbId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var knownImdbIds = movies.Where(movie => !string.IsNullOrWhiteSpace(movie.ImdbId)).Select(movie => movie.ImdbId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (knownTmdbIds.Count > 1 || knownImdbIds.Count > 1)
            {
                return false;
            }

            if (firstFingerprint.DedupFingerprint.StartsWith("movie:titleyear:", StringComparison.Ordinal))
            {
                var years = movies.Where(movie => movie.ReleaseDate.HasValue).Select(movie => movie.ReleaseDate!.Value.Year).Distinct().ToList();
                if (years.Count > 1)
                {
                    return false;
                }
            }

            var languages = movies
                .Where(movie => !string.IsNullOrWhiteSpace(movie.OriginalLanguage))
                .Select(movie => movie.OriginalLanguage.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return languages.Count <= 1;
        }

        private static bool HasConsistentSeriesEvidence(IReadOnlyList<Series> series, CatalogFingerprintResult firstFingerprint)
        {
            var titleKeys = series
                .Select(show => CatalogFingerprinting.ComputeSeries(show).CanonicalTitleKey)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (titleKeys.Count > 1)
            {
                return false;
            }

            var knownTmdbIds = series.Where(show => !string.IsNullOrWhiteSpace(show.TmdbId)).Select(show => show.TmdbId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var knownImdbIds = series.Where(show => !string.IsNullOrWhiteSpace(show.ImdbId)).Select(show => show.ImdbId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (knownTmdbIds.Count > 1 || knownImdbIds.Count > 1)
            {
                return false;
            }

            if (firstFingerprint.DedupFingerprint.StartsWith("series:titleyear:", StringComparison.Ordinal))
            {
                var years = series.Where(show => show.FirstAirDate.HasValue).Select(show => show.FirstAirDate!.Value.Year).Distinct().ToList();
                if (years.Count > 1)
                {
                    return false;
                }
            }

            var languages = series
                .Where(show => !string.IsNullOrWhiteSpace(show.OriginalLanguage))
                .Select(show => show.OriginalLanguage.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return languages.Count <= 1;
        }

        private static CatalogMovieGroup BuildMovieGroup(IEnumerable<Movie> movies, IReadOnlyDictionary<int, SourceProfile> sourceProfiles)
        {
            var variants = movies
                .Select(movie => new CatalogMovieVariant
                {
                    Movie = movie,
                    SourceProfile = sourceProfiles[movie.SourceProfileId]
                })
                .OrderByDescending(variant => GetMovieQualityScore(variant.Movie))
                .ThenBy(variant => variant.SourceProfile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var preferred = variants.First().Movie;
            return new CatalogMovieGroup
            {
                GroupKey = GetMovieBucketKey(preferred),
                PreferredMovie = preferred,
                Variants = variants,
                SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
            };
        }

        private static CatalogSeriesGroup BuildSeriesGroup(IEnumerable<Series> series, IReadOnlyDictionary<int, SourceProfile> sourceProfiles)
        {
            var variants = series
                .Select(show => new CatalogSeriesVariant
                {
                    Series = show,
                    SourceProfile = sourceProfiles[show.SourceProfileId]
                })
                .OrderByDescending(variant => GetSeriesQualityScore(variant.Series))
                .ThenByDescending(variant => variant.EpisodeCount)
                .ThenBy(variant => variant.SourceProfile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var preferred = variants.First().Series;
            return new CatalogSeriesGroup
            {
                GroupKey = GetSeriesBucketKey(preferred),
                PreferredSeries = preferred,
                Variants = variants,
                SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
            };
        }

        private static int GetMovieQualityScore(Movie movie)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(movie.TmdbId)) score += 40;
            if (!string.IsNullOrWhiteSpace(movie.ImdbId)) score += 30;
            if (movie.ReleaseDate.HasValue) score += 18;
            if (!string.IsNullOrWhiteSpace(movie.Overview)) score += 12;
            if (!string.IsNullOrWhiteSpace(movie.BackdropUrl) || !string.IsNullOrWhiteSpace(movie.TmdbBackdropPath)) score += 10;
            if (!string.IsNullOrWhiteSpace(movie.PosterUrl) || !string.IsNullOrWhiteSpace(movie.TmdbPosterPath)) score += 8;
            if (movie.VoteAverage > 0) score += 6;
            if (!string.IsNullOrWhiteSpace(movie.StreamUrl)) score += 20;
            return score;
        }

        private static int GetSeriesQualityScore(Series series)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(series.TmdbId)) score += 40;
            if (!string.IsNullOrWhiteSpace(series.ImdbId)) score += 30;
            if (series.FirstAirDate.HasValue) score += 18;
            if (!string.IsNullOrWhiteSpace(series.Overview)) score += 12;
            if (!string.IsNullOrWhiteSpace(series.BackdropUrl) || !string.IsNullOrWhiteSpace(series.TmdbBackdropPath)) score += 10;
            if (!string.IsNullOrWhiteSpace(series.PosterUrl) || !string.IsNullOrWhiteSpace(series.TmdbPosterPath)) score += 8;
            if (series.VoteAverage > 0) score += 6;
            score += Math.Min(series.Seasons?.Sum(season => season.Episodes?.Count ?? 0) ?? 0, 20);
            return score;
        }

        private static string BuildSourceSummary(IEnumerable<string> sourceNames)
        {
            var distinct = sourceNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                return string.Empty;
            }

            if (distinct.Count == 1)
            {
                return distinct[0];
            }

            if (distinct.Count == 2)
            {
                return $"{distinct[0]} + {distinct[1]}";
            }

            return $"{distinct[0]} +{distinct.Count - 1} more";
        }
    }
}
