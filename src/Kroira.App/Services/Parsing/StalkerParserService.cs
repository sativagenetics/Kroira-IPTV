#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IStalkerParserService
    {
        Task ParseAndImportStalkerAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true);

        Task ParseAndImportStalkerVodAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true);
    }

    public sealed class StalkerParserService : IStalkerParserService
    {
        private readonly ICatalogNormalizationService _catalogNormalizationService;
        private readonly ISourceEnrichmentService _sourceEnrichmentService;
        private readonly ISourceHealthService _sourceHealthService;
        private readonly IStalkerPortalClient _stalkerPortalClient;
        private readonly ISourceCredentialStore _credentialStore;

        public StalkerParserService(
            ICatalogNormalizationService catalogNormalizationService,
            ISourceEnrichmentService sourceEnrichmentService,
            ISourceHealthService sourceHealthService,
            IStalkerPortalClient stalkerPortalClient,
            ISourceCredentialStore? credentialStore = null)
        {
            _catalogNormalizationService = catalogNormalizationService;
            _sourceEnrichmentService = sourceEnrichmentService;
            _sourceHealthService = sourceHealthService;
            _stalkerPortalClient = stalkerPortalClient;
            _credentialStore = credentialStore ?? SourceCredentialStore.CreateDefault();
        }

        public async Task ParseAndImportStalkerAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true)
        {
            var profile = await db.SourceProfiles.FindAsync(sourceProfileId);
            if (profile == null)
            {
                throw new InvalidOperationException("Source not found.");
            }

            var credential = await _credentialStore.GetCredentialAsync(db, sourceProfileId);
            if (credential == null)
            {
                throw new InvalidOperationException("Stalker source credentials are missing.");
            }

            var portalCatalog = await _stalkerPortalClient.LoadCatalogAsync(credential);
            acquisitionSession?.RegisterRawItems(portalCatalog.LiveChannels.Count + portalCatalog.Movies.Count + portalCatalog.Series.Count);
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await ReplaceLiveCatalogAsync(db, sourceProfileId, portalCatalog, acquisitionSession);
                await ReplaceVodCatalogAsync(db, sourceProfileId, portalCatalog, acquisitionSession);
                await UpsertPortalSnapshotAsync(db, sourceProfileId, portalCatalog, null);
                await UpdateSyncStateAsync(
                    db,
                    sourceProfileId,
                    200,
                    $"Stalker sync imported {portalCatalog.LiveChannels.Count} live, {portalCatalog.Movies.Count} movies, and {portalCatalog.Series.Count} series.");
                profile.LastSync = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
            }
            catch (Exception ex)
            {
                var safeMessage = EpgDiagnosticFormatter.Redact(ex.Message);
                await transaction.RollbackAsync();
                await UpsertPortalSnapshotAsync(db, sourceProfileId, portalCatalog, safeMessage);
                await UpdateSyncStateAsync(db, sourceProfileId, 500, $"Stalker sync failed: {safeMessage}");
                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }

                throw;
            }
        }

        public async Task ParseAndImportStalkerVodAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true)
        {
            var profile = await db.SourceProfiles.FindAsync(sourceProfileId);
            if (profile == null)
            {
                throw new InvalidOperationException("Source not found.");
            }

            var credential = await _credentialStore.GetCredentialAsync(db, sourceProfileId);
            if (credential == null)
            {
                throw new InvalidOperationException("Stalker source credentials are missing.");
            }

            var portalCatalog = await _stalkerPortalClient.LoadCatalogAsync(credential);
            acquisitionSession?.RegisterRawItems(portalCatalog.Movies.Count + portalCatalog.Series.Count);
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await ReplaceVodCatalogAsync(db, sourceProfileId, portalCatalog, acquisitionSession);
                await UpsertPortalSnapshotAsync(db, sourceProfileId, portalCatalog, null);
                await UpdateSyncStateAsync(
                    db,
                    sourceProfileId,
                    200,
                    $"Stalker library sync imported {portalCatalog.Movies.Count} movies and {portalCatalog.Series.Count} series.");
                profile.LastSync = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
            }
            catch (Exception ex)
            {
                var safeMessage = EpgDiagnosticFormatter.Redact(ex.Message);
                await transaction.RollbackAsync();
                await UpsertPortalSnapshotAsync(db, sourceProfileId, portalCatalog, safeMessage);
                await UpdateSyncStateAsync(db, sourceProfileId, 500, $"Stalker library sync failed: {safeMessage}");
                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }

                throw;
            }
        }

        private async Task ReplaceLiveCatalogAsync(
            AppDbContext db,
            int sourceProfileId,
            StalkerPortalCatalog catalog,
            SourceAcquisitionSession? acquisitionSession)
        {
            var oldCategories = await db.ChannelCategories.Where(item => item.SourceProfileId == sourceProfileId).ToListAsync();
            var oldCategoryIds = oldCategories.Select(item => item.Id).ToList();
            var oldChannels = oldCategoryIds.Count == 0
                ? new List<Channel>()
                : await db.Channels.Where(item => oldCategoryIds.Contains(item.ChannelCategoryId)).ToListAsync();
            var oldChannelIds = oldChannels.Select(item => item.Id).ToList();
            if (oldChannelIds.Count > 0)
            {
                var oldPrograms = await db.EpgPrograms.Where(item => oldChannelIds.Contains(item.ChannelId)).ToListAsync();
                if (oldPrograms.Count > 0)
                {
                    db.EpgPrograms.RemoveRange(oldPrograms);
                }
            }

            if (oldChannels.Count > 0)
            {
                db.Channels.RemoveRange(oldChannels);
            }

            if (oldCategories.Count > 0)
            {
                db.ChannelCategories.RemoveRange(oldCategories);
            }

            await db.SaveChangesAsync();

            var categories = new Dictionary<string, ChannelCategory>(StringComparer.OrdinalIgnoreCase);
            var orderedCategories = catalog.LiveCategories
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select((item, index) => new ChannelCategory
                {
                    SourceProfileId = sourceProfileId,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? "Uncategorized" : item.Name.Trim(),
                    OrderIndex = index
                })
                .ToList();
            if (orderedCategories.Count > 0)
            {
                db.ChannelCategories.AddRange(orderedCategories);
                await db.SaveChangesAsync();
                for (var index = 0; index < orderedCategories.Count; index++)
                {
                    categories[catalog.LiveCategories[index].Id] = orderedCategories[index];
                }
            }

            if (!categories.ContainsKey(string.Empty))
            {
                var fallbackCategory = new ChannelCategory
                {
                    SourceProfileId = sourceProfileId,
                    Name = "Uncategorized",
                    OrderIndex = orderedCategories.Count
                };
                db.ChannelCategories.Add(fallbackCategory);
                await db.SaveChangesAsync();
                categories[string.Empty] = fallbackCategory;
            }

            var liveCategoryLabels = ContentClassifier.BuildCategoryLabelSet(categories.Values.Select(item => item.Name));
            var importedChannels = new List<Channel>();
            foreach (var liveChannel in catalog.LiveChannels)
            {
                var category = categories.TryGetValue(liveChannel.CategoryId ?? string.Empty, out var mappedCategory)
                    ? mappedCategory
                    : categories[string.Empty];
                if (!ContentClassifier.IsPlayableXtreamLiveChannel(liveChannel.Name, liveChannel.Command, liveCategoryLabels))
                {
                    acquisitionSession?.RecordSuppressed(
                        SourceAcquisitionItemKind.LiveChannel,
                        "acquire.stalker.live_filtered",
                        "Portal row failed playability or quality screening and was ignored.",
                        liveChannel.Name,
                        category.Name,
                        liveChannel.Name,
                        category.Name);
                    continue;
                }

                var locator = StalkerLocatorCodec.Encode(new StalkerStreamLocator(
                    sourceProfileId,
                    "live",
                    liveChannel.Id,
                    liveChannel.Command));
                importedChannels.Add(new Channel
                {
                    ChannelCategoryId = category.Id,
                    Name = liveChannel.Name.Trim(),
                    StreamUrl = locator,
                    LogoUrl = liveChannel.LogoUrl ?? string.Empty,
                    ProviderLogoUrl = liveChannel.LogoUrl ?? string.Empty,
                    EpgChannelId = string.IsNullOrWhiteSpace(liveChannel.EpgChannelId) ? liveChannel.Id : liveChannel.EpgChannelId,
                    ProviderEpgChannelId = string.IsNullOrWhiteSpace(liveChannel.EpgChannelId) ? liveChannel.Id : liveChannel.EpgChannelId,
                    CatchupSummary = "Catchup is not currently advertised by this Stalker source."
                });
            }

            if (importedChannels.Count > 0)
            {
                db.Channels.AddRange(importedChannels);
                await db.SaveChangesAsync();
                await _sourceEnrichmentService.PrepareLiveCatalogAsync(db, sourceProfileId, acquisitionSession);
                acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.LiveChannel, importedChannels.Count);
            }
        }

        private async Task ReplaceVodCatalogAsync(
            AppDbContext db,
            int sourceProfileId,
            StalkerPortalCatalog catalog,
            SourceAcquisitionSession? acquisitionSession)
        {
            var existingSeries = await db.Series
                .Include(item => item.Seasons!)
                .ThenInclude(item => item.Episodes!)
                .Where(item => item.SourceProfileId == sourceProfileId)
                .ToListAsync();
            foreach (var series in existingSeries)
            {
                foreach (var season in series.Seasons ?? Array.Empty<Season>())
                {
                    if (season.Episodes != null && season.Episodes.Count > 0)
                    {
                        db.Episodes.RemoveRange(season.Episodes);
                    }
                }

                if (series.Seasons != null && series.Seasons.Count > 0)
                {
                    db.Seasons.RemoveRange(series.Seasons);
                }
            }

            if (existingSeries.Count > 0)
            {
                db.Series.RemoveRange(existingSeries);
            }

            var existingMovies = await db.Movies.Where(item => item.SourceProfileId == sourceProfileId).ToListAsync();
            if (existingMovies.Count > 0)
            {
                db.Movies.RemoveRange(existingMovies);
            }

            await db.SaveChangesAsync();

            var movieCategoryMap = catalog.MovieCategories
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
            var seriesCategoryMap = catalog.SeriesCategories
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

            var movies = new List<Movie>();
            foreach (var item in catalog.Movies)
            {
                var categoryName = movieCategoryMap.GetValueOrDefault(item.CategoryId, "Uncategorized");
                if (ContentClassifier.IsGarbageTitle(item.Name) || ContentClassifier.IsGarbageCategoryName(categoryName))
                {
                    acquisitionSession?.RecordSuppressed(
                        SourceAcquisitionItemKind.Movie,
                        "acquire.stalker.movie_filtered",
                        "Movie row failed title or category quality screening and was ignored.",
                        item.Name,
                        categoryName,
                        item.Name,
                        categoryName);
                    continue;
                }

                var normalized = _catalogNormalizationService.NormalizeMovie(
                    SourceType.Stalker,
                    item.Name,
                    categoryName);
                var movie = new Movie
                {
                    SourceProfileId = sourceProfileId,
                    ExternalId = item.Id,
                    Title = normalized.Title,
                    RawSourceTitle = item.Name,
                    StreamUrl = StalkerLocatorCodec.Encode(new StalkerStreamLocator(sourceProfileId, "movie", item.Id, item.Command)),
                    PosterUrl = item.LogoUrl ?? string.Empty,
                    Overview = item.Description ?? string.Empty,
                    CategoryName = normalized.CategoryName,
                    RawSourceCategoryName = categoryName,
                    ContentKind = "Primary"
                };
                CatalogFingerprinting.Apply(movie);
                movies.Add(movie);
            }

            if (movies.Count > 0)
            {
                db.Movies.AddRange(movies);
                acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Movie, movies.Count);
            }

            var seriesRows = new List<Series>();
            var episodeCount = 0;
            foreach (var item in catalog.Series)
            {
                var categoryName = seriesCategoryMap.GetValueOrDefault(item.CategoryId, "Series");
                if (ContentClassifier.IsGarbageTitle(item.Name) || ContentClassifier.IsGarbageCategoryName(categoryName))
                {
                    acquisitionSession?.RecordSuppressed(
                        SourceAcquisitionItemKind.Series,
                        "acquire.stalker.series_filtered",
                        "Series row failed title or category quality screening and was ignored.",
                        item.Name,
                        categoryName,
                        item.Name,
                        categoryName);
                    continue;
                }

                var normalized = _catalogNormalizationService.NormalizeSeries(
                    SourceType.Stalker,
                    item.Name,
                    categoryName);
                var seasons = new List<Season>();
                foreach (var season in item.Seasons.OrderBy(season => season.SeasonNumber))
                {
                    var episodes = season.Episodes
                        .Where(episode => !string.IsNullOrWhiteSpace(episode.Id) && !string.IsNullOrWhiteSpace(episode.Command))
                        .OrderBy(episode => episode.EpisodeNumber)
                        .Select(episode => new Episode
                        {
                            ExternalId = episode.Id,
                            Title = episode.Title,
                            EpisodeNumber = episode.EpisodeNumber,
                            StreamUrl = StalkerLocatorCodec.Encode(
                                new StalkerStreamLocator(
                                    sourceProfileId,
                                    "episode",
                                    episode.Id,
                                    episode.Command,
                                    item.Id,
                                    season.SeasonNumber,
                                    episode.EpisodeNumber))
                        })
                        .ToList();
                    if (episodes.Count == 0)
                    {
                        continue;
                    }

                    episodeCount += episodes.Count;
                    seasons.Add(new Season
                    {
                        SeasonNumber = season.SeasonNumber,
                        Episodes = episodes
                    });
                }

                if (seasons.Count == 0)
                {
                    acquisitionSession?.RecordSuppressed(
                        SourceAcquisitionItemKind.Series,
                        "acquire.stalker.series_missing_episodes",
                        "Series row did not expose episode details and was ignored.",
                        item.Name,
                        categoryName,
                        normalized.Title,
                        normalized.CategoryName);
                    continue;
                }

                var series = new Series
                {
                    SourceProfileId = sourceProfileId,
                    ExternalId = item.Id,
                    Title = normalized.Title,
                    RawSourceTitle = item.Name,
                    PosterUrl = item.LogoUrl ?? string.Empty,
                    CategoryName = normalized.CategoryName,
                    RawSourceCategoryName = categoryName,
                    ContentKind = "Primary",
                    Seasons = seasons
                };
                CatalogFingerprinting.Apply(series);
                seriesRows.Add(series);
            }

            if (seriesRows.Count > 0)
            {
                db.Series.AddRange(seriesRows);
                acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Series, seriesRows.Count);
            }

            if (episodeCount > 0)
            {
                acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Episode, episodeCount);
            }

            await db.SaveChangesAsync();
        }

        private static async Task UpdateSyncStateAsync(
            AppDbContext db,
            int sourceProfileId,
            int httpStatusCode,
            string message)
        {
            var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (syncState == null)
            {
                syncState = new SourceSyncState
                {
                    SourceProfileId = sourceProfileId
                };
                db.SourceSyncStates.Add(syncState);
            }

            syncState.LastAttempt = DateTime.UtcNow;
            syncState.HttpStatusCode = httpStatusCode;
            syncState.ErrorLog = message;
            await db.SaveChangesAsync();
        }

        private static async Task UpsertPortalSnapshotAsync(
            AppDbContext db,
            int sourceProfileId,
            StalkerPortalCatalog catalog,
            string? error)
        {
            var snapshot = await db.StalkerPortalSnapshots.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (snapshot == null)
            {
                snapshot = new StalkerPortalSnapshot
                {
                    SourceProfileId = sourceProfileId
                };
                db.StalkerPortalSnapshots.Add(snapshot);
            }

            snapshot.PortalName = Trim(catalog.PortalName, 180);
            snapshot.PortalVersion = Trim(catalog.PortalVersion, 96);
            snapshot.ProfileName = Trim(catalog.ProfileName, 180);
            snapshot.ProfileId = Trim(catalog.ProfileId, 96);
            snapshot.MacAddress = Trim(catalog.MacAddress, 64);
            snapshot.DeviceId = Trim(catalog.DeviceId, 128);
            snapshot.SerialNumber = Trim(catalog.SerialNumber, 128);
            snapshot.Locale = Trim(catalog.Locale, 64);
            snapshot.Timezone = Trim(catalog.Timezone, 96);
            snapshot.DiscoveredApiUrl = Trim(catalog.DiscoveredApiUrl, 600);
            snapshot.SupportsLive = catalog.SupportsLive;
            snapshot.SupportsMovies = catalog.SupportsMovies;
            snapshot.SupportsSeries = catalog.SupportsSeries;
            snapshot.LiveCategoryCount = catalog.LiveCategories.Count;
            snapshot.MovieCategoryCount = catalog.MovieCategories.Count;
            snapshot.SeriesCategoryCount = catalog.SeriesCategories.Count;
            snapshot.LastHandshakeAtUtc = catalog.LastHandshakeAtUtc;
            snapshot.LastProfileSyncAtUtc = catalog.LastProfileSyncAtUtc;
            snapshot.LastSummary = Trim(
                $"Portal {catalog.PortalName} at {catalog.DiscoveredApiUrl} exposed {catalog.LiveChannels.Count} live, {catalog.Movies.Count} movies, and {catalog.Series.Count} series.",
                320);
            snapshot.LastError = Trim(error ?? string.Join(" ", catalog.Warnings), 320);
            await db.SaveChangesAsync();
        }

        private static string Trim(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
        }
    }
}
