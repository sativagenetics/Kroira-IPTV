#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public sealed record MediaCountChain(int? ParserCount, int PersistedCount, int QueriedCount, int SurfacedCount);

    public sealed record SourceCatalogCountChain(
        int SourceProfileId,
        MediaCountChain Live,
        MediaCountChain Movies,
        MediaCountChain Series);

    public sealed record CatalogSurfaceCountSummary(
        MediaCountChain Live,
        MediaCountChain Movies,
        MediaCountChain Series,
        IReadOnlyDictionary<int, SourceCatalogCountChain> Sources);

    public interface ICatalogSurfaceCountService
    {
        Task<CatalogSurfaceCountSummary> BuildAsync(AppDbContext db, ProfileAccessSnapshot access);
    }

    public sealed class CatalogSurfaceCountService : ICatalogSurfaceCountService
    {
        private static readonly Regex ParserSummaryRegex = new(
            @"Parsed\s+(?<live>\d+)\s+channels(?:,\s+(?<movies>\d+)\s+movies,\s+\d+\s+episodes\s+across\s+(?<series>\d+)\s+series)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ICatalogDeduplicationService _catalogDeduplicationService;
        private readonly IBrowsePreferencesService _browsePreferencesService;

        public CatalogSurfaceCountService(
            ICatalogDeduplicationService catalogDeduplicationService,
            IBrowsePreferencesService browsePreferencesService)
        {
            _catalogDeduplicationService = catalogDeduplicationService;
            _browsePreferencesService = browsePreferencesService;
        }

        public async Task<CatalogSurfaceCountSummary> BuildAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            var sourceIds = await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => profile.Id)
                .ToListAsync();

            var parserCounts = await LoadParserCountsAsync(db, sourceIds);

            var livePreferences = await _browsePreferencesService.GetAsync(db, ProfileDomains.Live, access.ProfileId);
            var moviePreferences = await _browsePreferencesService.GetAsync(db, ProfileDomains.Movies, access.ProfileId);
            var seriesPreferences = await _browsePreferencesService.GetAsync(db, ProfileDomains.Series, access.ProfileId);

            var categories = await db.ChannelCategories
                .AsNoTracking()
                .Where(category => sourceIds.Contains(category.SourceProfileId))
                .ToListAsync();
            var categoryById = categories.ToDictionary(category => category.Id);
            var categoryIds = categories.Select(category => category.Id).ToList();
            var channels = categoryIds.Count == 0
                ? new List<Channel>()
                : await db.Channels
                    .AsNoTracking()
                    .Where(channel => categoryIds.Contains(channel.ChannelCategoryId))
                    .ToListAsync();

            var movieGroups = await _catalogDeduplicationService.LoadMovieGroupsAsync(db);
            var seriesGroups = await _catalogDeduplicationService.LoadSeriesGroupsAsync(db);

            var persistedLiveCounts = categories
                .Join(
                    channels,
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => category.SourceProfileId)
                .GroupBy(sourceProfileId => sourceProfileId)
                .ToDictionary(group => group.Key, group => group.Count());

            var persistedMovieCounts = movieGroups
                .SelectMany(group => group.Variants)
                .GroupBy(variant => variant.SourceProfile.Id)
                .ToDictionary(group => group.Key, group => group.Count());

            var persistedSeriesCounts = seriesGroups
                .SelectMany(group => group.Variants)
                .GroupBy(variant => variant.SourceProfile.Id)
                .ToDictionary(group => group.Key, group => group.Count());

            var overallLiveQueried = CountQueriedChannels(channels, categoryById, access);
            var overallLiveSurfaced = CountSurfacedChannels(channels, categoryById, access, livePreferences);
            var overallMovieQueried = movieGroups.Count(group => HasQueriedMovieVariant(group, access));
            var overallMovieSurfaced = movieGroups.Count(group => IsSurfacedMovieGroup(group, access, moviePreferences, sourceProfileId: null));
            var overallSeriesQueried = seriesGroups.Count(group => HasQueriedSeriesVariant(group, access));
            var overallSeriesSurfaced = seriesGroups.Count(group => IsSurfacedSeriesGroup(group, access, seriesPreferences, sourceProfileId: null));

            var sourceChains = new Dictionary<int, SourceCatalogCountChain>(sourceIds.Count);
            foreach (var sourceId in sourceIds)
            {
                parserCounts.TryGetValue(sourceId, out var parser);
                sourceChains[sourceId] = new SourceCatalogCountChain(
                    sourceId,
                    new MediaCountChain(
                        parser.LiveCount,
                        persistedLiveCounts.GetValueOrDefault(sourceId),
                        CountQueriedChannels(channels, categoryById, access, sourceId),
                        CountSurfacedChannels(channels, categoryById, access, livePreferences, sourceId)),
                    new MediaCountChain(
                        parser.MovieCount,
                        persistedMovieCounts.GetValueOrDefault(sourceId),
                        movieGroups.Count(group => HasQueriedMovieVariant(group, access, sourceId)),
                        movieGroups.Count(group => IsSurfacedMovieGroup(group, access, moviePreferences, sourceId))),
                    new MediaCountChain(
                        parser.SeriesCount,
                        persistedSeriesCounts.GetValueOrDefault(sourceId),
                        seriesGroups.Count(group => HasQueriedSeriesVariant(group, access, sourceId)),
                        seriesGroups.Count(group => IsSurfacedSeriesGroup(group, access, seriesPreferences, sourceId))));
            }

            return new CatalogSurfaceCountSummary(
                new MediaCountChain(
                    sourceIds.Sum(id => parserCounts.GetValueOrDefault(id).LiveCount ?? 0),
                    persistedLiveCounts.Values.Sum(),
                    overallLiveQueried,
                    overallLiveSurfaced),
                new MediaCountChain(
                    sourceIds.Sum(id => parserCounts.GetValueOrDefault(id).MovieCount ?? 0),
                    persistedMovieCounts.Values.Sum(),
                    overallMovieQueried,
                    overallMovieSurfaced),
                new MediaCountChain(
                    sourceIds.Sum(id => parserCounts.GetValueOrDefault(id).SeriesCount ?? 0),
                    persistedSeriesCounts.Values.Sum(),
                    overallSeriesQueried,
                    overallSeriesSurfaced),
                sourceChains);
        }

        private static int CountQueriedChannels(
            IEnumerable<Channel> channels,
            IReadOnlyDictionary<int, ChannelCategory> categoryById,
            ProfileAccessSnapshot access,
            int? sourceProfileId = null)
        {
            return channels.Count(channel =>
                categoryById.TryGetValue(channel.ChannelCategoryId, out var category) &&
                (sourceProfileId == null || category.SourceProfileId == sourceProfileId.Value) &&
                access.IsLiveChannelAllowed(channel, category));
        }

        private int CountSurfacedChannels(
            IEnumerable<Channel> channels,
            IReadOnlyDictionary<int, ChannelCategory> categoryById,
            ProfileAccessSnapshot access,
            BrowsePreferences preferences,
            int? sourceProfileId = null)
        {
            return channels.Count(channel =>
                categoryById.TryGetValue(channel.ChannelCategoryId, out var category) &&
                (sourceProfileId == null || category.SourceProfileId == sourceProfileId.Value) &&
                access.IsLiveChannelAllowed(channel, category) &&
                !_browsePreferencesService.IsCategoryHidden(preferences, category.Name));
        }

        private static bool HasQueriedMovieVariant(
            CatalogMovieGroup group,
            ProfileAccessSnapshot access,
            int? sourceProfileId = null)
        {
            return group.Variants.Any(variant =>
                (sourceProfileId == null || variant.SourceProfile.Id == sourceProfileId.Value) &&
                access.IsMovieAllowed(variant.Movie));
        }

        private bool IsSurfacedMovieGroup(
            CatalogMovieGroup group,
            ProfileAccessSnapshot access,
            BrowsePreferences preferences,
            int? sourceProfileId)
        {
            var variants = group.Variants
                .Where(variant => sourceProfileId == null || variant.SourceProfile.Id == sourceProfileId.Value)
                .Where(variant => access.IsMovieAllowed(variant.Movie))
                .ToList();

            if (preferences.HideSecondaryContent)
            {
                variants = variants
                    .Where(variant => string.Equals(variant.Movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (variants.Count == 0)
            {
                return false;
            }

            var preferredMovie = variants.FirstOrDefault(variant => variant.Movie.Id == group.PreferredMovie.Id)?.Movie ?? variants[0].Movie;
            return !_browsePreferencesService.IsCategoryHidden(preferences, preferredMovie.CategoryName);
        }

        private static bool HasQueriedSeriesVariant(
            CatalogSeriesGroup group,
            ProfileAccessSnapshot access,
            int? sourceProfileId = null)
        {
            return group.Variants.Any(variant =>
                (sourceProfileId == null || variant.SourceProfile.Id == sourceProfileId.Value) &&
                access.IsSeriesAllowed(variant.Series));
        }

        private bool IsSurfacedSeriesGroup(
            CatalogSeriesGroup group,
            ProfileAccessSnapshot access,
            BrowsePreferences preferences,
            int? sourceProfileId)
        {
            var variants = group.Variants
                .Where(variant => sourceProfileId == null || variant.SourceProfile.Id == sourceProfileId.Value)
                .Where(variant => access.IsSeriesAllowed(variant.Series))
                .ToList();

            if (preferences.HideSecondaryContent)
            {
                variants = variants
                    .Where(variant => string.Equals(variant.Series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (variants.Count == 0)
            {
                return false;
            }

            var preferredSeries = variants.FirstOrDefault(variant => variant.Series.Id == group.PreferredSeries.Id)?.Series ?? variants[0].Series;
            var surfacedCategoryName = ContentClassifier.ResolveSurfacedSeriesCategory(
                preferredSeries.CategoryName,
                preferredSeries.RawSourceCategoryName,
                preferredSeries.Title);

            return !_browsePreferencesService.IsCategoryHidden(preferences, surfacedCategoryName);
        }

        private static async Task<Dictionary<int, ParsedImportCounts>> LoadParserCountsAsync(AppDbContext db, IReadOnlyCollection<int> sourceIds)
        {
            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => sourceIds.Contains(state.SourceProfileId))
                .ToListAsync();

            var result = new Dictionary<int, ParsedImportCounts>(sourceIds.Count);
            foreach (var sourceId in sourceIds)
            {
                var syncState = syncStates.FirstOrDefault(state => state.SourceProfileId == sourceId);
                result[sourceId] = ParseParserCounts(syncState?.ErrorLog);
            }

            return result;
        }

        private static ParsedImportCounts ParseParserCounts(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return ParsedImportCounts.Empty;
            }

            var match = ParserSummaryRegex.Match(summary);
            if (!match.Success)
            {
                return ParsedImportCounts.Empty;
            }

            return new ParsedImportCounts(
                ParseCount(match, "live"),
                ParseCount(match, "movies"),
                ParseCount(match, "series"));
        }

        private static int? ParseCount(Match match, string groupName)
        {
            return match.Groups[groupName].Success && int.TryParse(match.Groups[groupName].Value, out var count)
                ? count
                : null;
        }

        private readonly record struct ParsedImportCounts(int? LiveCount, int? MovieCount, int? SeriesCount)
        {
            public static ParsedImportCounts Empty => new(null, null, null);
        }
    }
}
