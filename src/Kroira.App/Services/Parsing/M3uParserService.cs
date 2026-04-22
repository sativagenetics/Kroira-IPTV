using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IM3uParserService
    {
        Task ParseAndImportM3uAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true);
    }

    public class M3uParserService : IM3uParserService
    {
        private const string DefaultGroupName = "Uncategorized";
        private readonly ICatalogNormalizationService _catalogNormalizationService;
        private readonly IChannelCatchupService _channelCatchupService;
        private readonly ISourceEnrichmentService _sourceEnrichmentService;
        private readonly ISourceHealthService _sourceHealthService;
        private readonly ISourceRoutingService _sourceRoutingService;

        public M3uParserService(
            ICatalogNormalizationService catalogNormalizationService,
            IChannelCatchupService channelCatchupService,
            ISourceEnrichmentService sourceEnrichmentService,
            ISourceHealthService sourceHealthService,
            ISourceRoutingService sourceRoutingService)
        {
            _catalogNormalizationService = catalogNormalizationService;
            _channelCatchupService = channelCatchupService;
            _sourceEnrichmentService = sourceEnrichmentService;
            _sourceHealthService = sourceHealthService;
            _sourceRoutingService = sourceRoutingService;
        }

        public async Task ParseAndImportM3uAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
            {
                throw new Exception("Source URL or Path is empty.");
            }

            try
            {
                var importMode = cred.M3uImportMode;
                var content = await ReadPlaylistAsync(cred.Url, cred);
                var headerMetadata = M3uMetadataParser.ParseHeaderMetadata(content, cred.Url);
                cred.DetectedEpgUrl = headerMetadata.XmltvUrls.FirstOrDefault() ?? string.Empty;
                var diagnostics = new M3uImportDiagnostics
                {
                    XmltvUrlFound = headerMetadata.XmltvUrls.Count > 0,
                    XmltvUrlValue = string.Join(" | ", headerMetadata.XmltvUrls)
                };

                LogHeaderDiagnostics(sourceProfileId, headerMetadata);

                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var parsedEntries = new List<M3uEntry>();

                var currentCategoryContext = string.Empty;
                PendingExtinf pending = null;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        var extinf = M3uMetadataParser.ParseExtinf(line);
                        var explicitGroup = NormalizeGroupName(M3uMetadataParser.GetFirstAttributeValue(
                            extinf.Attributes,
                            "group-title"));

                        if (!string.IsNullOrWhiteSpace(explicitGroup) &&
                            !string.Equals(explicitGroup, DefaultGroupName, StringComparison.OrdinalIgnoreCase))
                        {
                            currentCategoryContext = explicitGroup;
                        }

                        pending = new PendingExtinf
                        {
                            Name = ResolveEntryName(extinf),
                            GroupName = !string.IsNullOrWhiteSpace(explicitGroup) &&
                                        !string.Equals(explicitGroup, DefaultGroupName, StringComparison.OrdinalIgnoreCase)
                                ? explicitGroup
                                : NormalizeGroupName(currentCategoryContext),
                            LogoUrl = M3uMetadataParser.GetFirstAttributeValue(extinf.Attributes, "tvg-logo"),
                            TvgType = M3uMetadataParser.GetFirstAttributeValue(extinf.Attributes, "tvg-type"),
                            TvgId = M3uMetadataParser.GetFirstAttributeValue(extinf.Attributes, "tvg-id"),
                            TvgName = M3uMetadataParser.GetFirstAttributeValue(extinf.Attributes, "tvg-name"),
                            Attributes = new Dictionary<string, string>(extinf.Attributes, StringComparer.OrdinalIgnoreCase),
                            HadExplicitGroupTitle = !string.IsNullOrWhiteSpace(explicitGroup) &&
                                                    !string.Equals(explicitGroup, DefaultGroupName, StringComparison.OrdinalIgnoreCase)
                        };

                        continue;
                    }

                    if (pending != null && !line.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (ContentClassifier.TryExtractM3uPseudoCategoryHeader(pending.Name, out var pseudoCategory))
                        {
                            currentCategoryContext = NormalizeGroupName(pseudoCategory);
                            diagnostics.PseudoItemRejectedCount++;
                            pending = null;
                            continue;
                        }

                        parsedEntries.Add(new M3uEntry
                        {
                            GroupName = NormalizeGroupName(pending.GroupName),
                            Name = pending.Name,
                            Url = line,
                            LogoUrl = pending.LogoUrl,
                            TvgType = pending.TvgType,
                            TvgId = pending.TvgId,
                            TvgName = pending.TvgName,
                            Attributes = pending.Attributes,
                            HadExplicitGroupTitle = pending.HadExplicitGroupTitle
                        });

                        if (pending.HadExplicitGroupTitle)
                        {
                            diagnostics.WithGroupTitleCount++;
                        }

                        if (!string.IsNullOrWhiteSpace(pending.LogoUrl))
                        {
                            diagnostics.WithTvgLogoCount++;
                        }

                        if (!string.IsNullOrWhiteSpace(pending.TvgId))
                        {
                            diagnostics.WithTvgIdCount++;
                        }

                        pending = null;
                    }
                }

                var categoryLabels = ContentClassifier.BuildCategoryLabelSet(parsedEntries.Select(e => e.GroupName));
                var explicitCategoryLabels = ContentClassifier.BuildCategoryLabelSet(
                    parsedEntries
                        .Where(entry => entry.HadExplicitGroupTitle)
                        .Select(entry => entry.GroupName));
                var normalizedExplicitCategoryLabels = explicitCategoryLabels
                    .Select(label => label.Trim().ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                acquisitionSession?.RegisterRawItems(parsedEntries.Count);

                var categoriesDict = new Dictionary<string, ChannelCategory>(StringComparer.OrdinalIgnoreCase);
                var movieList = new List<Movie>();
                var episodeEntries = new List<EpisodeEntry>();
                var totalChannels = 0;

                foreach (var entry in parsedEntries)
                {
                    if (entry.HadExplicitGroupTitle && IsVodGroupUnsafe(entry.GroupName))
                    {
                        acquisitionSession?.RecordSuppressed(
                            SourceAcquisitionItemKind.LiveChannel,
                            "acquire.m3u.unsafe_group",
                            "Explicit provider group was suppressed by the M3U safety profile.",
                            entry.Name,
                            entry.GroupName,
                            entry.Name,
                            entry.GroupName);
                        continue;
                    }

                    if (ContentClassifier.IsGarbageCategoryName(entry.GroupName))
                    {
                        acquisitionSession?.RecordSuppressed(
                            SourceAcquisitionItemKind.LiveChannel,
                            "acquire.m3u.garbage_category",
                            "Provider category label was classified as garbage and ignored.",
                            entry.Name,
                            entry.GroupName,
                            entry.Name,
                            entry.GroupName);
                        continue;
                    }

                    if (ContentClassifier.IsGarbageTitle(entry.Name))
                    {
                        acquisitionSession?.RecordSuppressed(
                            SourceAcquisitionItemKind.LiveChannel,
                            "acquire.m3u.garbage_title",
                            "Provider title was classified as garbage and ignored.",
                            entry.Name,
                            entry.GroupName,
                            entry.Name,
                            entry.GroupName);
                        continue;
                    }

                    if (ContentClassifier.IsProviderCategoryRow(entry.Name, categoryLabels))
                    {
                        acquisitionSession?.RecordSuppressed(
                            SourceAcquisitionItemKind.LiveChannel,
                            "acquire.m3u.provider_bucket",
                            "Provider bucket/header row was suppressed before catalog import.",
                            entry.Name,
                            entry.GroupName,
                            entry.Name,
                            entry.GroupName);
                        continue;
                    }

                    var entryType = ContentClassifier.ClassifyM3uEntry(
                        entry.Name,
                        entry.Url,
                        entry.GroupName,
                        entry.TvgType);

                    switch (entryType)
                    {
                        case ContentClassifier.M3uEntryType.Live:
                            if (!categoriesDict.TryGetValue(entry.GroupName, out var category))
                            {
                                category = new ChannelCategory
                                {
                                    SourceProfileId = sourceProfileId,
                                    Name = entry.GroupName,
                                    OrderIndex = categoriesDict.Count,
                                    Channels = new List<Channel>()
                                };
                                categoriesDict[entry.GroupName] = category;
                            }

                            var channel = new Channel
                            {
                                Name = entry.Name,
                                StreamUrl = entry.Url,
                                LogoUrl = entry.LogoUrl,
                                ProviderLogoUrl = entry.LogoUrl,
                                EpgChannelId = !string.IsNullOrWhiteSpace(entry.TvgId)
                                    ? entry.TvgId
                                    : entry.TvgName,
                                ProviderEpgChannelId = !string.IsNullOrWhiteSpace(entry.TvgId)
                                    ? entry.TvgId
                                    : entry.TvgName
                            };
                            _channelCatchupService.ApplyM3uCatchup(channel, entry.Attributes);
                            category.Channels!.Add(channel);
                            totalChannels++;
                            break;

                        case ContentClassifier.M3uEntryType.Movie:
                            if (importMode == M3uImportMode.LiveOnly)
                            {
                                break;
                            }

                            if (!ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name))
                            {
                                movieList.Add(BuildMovie(sourceProfileId, entry));
                            }
                            break;

                        case ContentClassifier.M3uEntryType.Episode:
                            if (importMode != M3uImportMode.LiveMoviesAndSeries)
                            {
                                if (importMode == M3uImportMode.LiveAndMovies &&
                                    !ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name))
                                {
                                    movieList.Add(BuildMovie(sourceProfileId, entry));
                                }

                                break;
                            }

                            var parsed = ContentClassifier.TryParseEpisodeInfo(
                                entry.Name,
                                out var seriesTitle,
                                out var seasonNum,
                                out var episodeNum,
                                out var epTitle);

                            var cleanedSeriesTitle = ContentClassifier.CleanSeriesBaseTitle(seriesTitle);

                            if (!parsed ||
                                string.IsNullOrWhiteSpace(cleanedSeriesTitle) ||
                                cleanedSeriesTitle.Length < 2 ||
                                IsTitleAmbiguouslyCategory(
                                    cleanedSeriesTitle,
                                    entry.HadExplicitGroupTitle ? entry.GroupName : string.Empty,
                                    normalizedExplicitCategoryLabels) ||
                                ContentClassifier.IsM3uBucketOrAdultLabel(cleanedSeriesTitle))
                            {
                                acquisitionSession?.RecordDemotion(
                                    "acquire.m3u.episode_demoted",
                                    "Episode metadata could not produce a stable series grouping, so the row was kept as a movie.",
                                    entry.Name,
                                    entry.GroupName,
                                    entry.Name,
                                    entry.GroupName);

                                if (!ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name))
                                {
                                    movieList.Add(BuildMovie(sourceProfileId, entry));
                                }

                                break;
                            }

                            episodeEntries.Add(new EpisodeEntry
                            {
                                GroupName = entry.GroupName,
                                Name = entry.Name,
                                Url = entry.Url,
                                LogoUrl = entry.LogoUrl,
                                TvgType = entry.TvgType,
                                SeriesTitle = cleanedSeriesTitle,
                                GroupingKey = ContentClassifier.ComputeSeriesGroupingKey(cleanedSeriesTitle),
                                SeasonNumber = seasonNum > 0 ? seasonNum : 1,
                                EpisodeNumber = episodeNum,
                                EpisodeTitle = epTitle,
                                HasStrongMarker = ContentClassifier.HasStrongEpisodeMarker(entry.Name),
                                HadExplicitGroupTitle = entry.HadExplicitGroupTitle
                            });
                            break;
                    }
                }

                var (seriesList, demotedToMovies) = BuildSeriesList(
                    sourceProfileId,
                    episodeEntries,
                    normalizedExplicitCategoryLabels,
                    _catalogNormalizationService,
                    acquisitionSession);
                movieList.AddRange(demotedToMovies);

                var totalMovies = movieList.Count;
                var totalSeries = seriesList.Count;
                var totalEpisodes = seriesList.Sum(series => series.Seasons?.Sum(season => season.Episodes?.Count ?? 0) ?? 0);
                var uncategorizedCount = GetUncategorizedCount(categoriesDict, movieList, seriesList);

                diagnostics.LiveCount = totalChannels;
                diagnostics.MovieCount = totalMovies;
                diagnostics.SeriesCount = totalSeries;
                diagnostics.UncategorizedCount = uncategorizedCount;
                LogImportDiagnostics(sourceProfileId, diagnostics);

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var existingCats = await db.ChannelCategories
                        .Where(category => category.SourceProfileId == sourceProfileId)
                        .ToListAsync();
                    var catIds = existingCats.Select(category => category.Id).ToList();
                    var existingChannels = await db.Channels
                        .Where(channel => catIds.Contains(channel.ChannelCategoryId))
                        .ToListAsync();
                    db.Channels.RemoveRange(existingChannels);
                    db.ChannelCategories.RemoveRange(existingCats);

                    var existingMovies = await db.Movies
                        .Where(movie => movie.SourceProfileId == sourceProfileId)
                        .ToListAsync();
                    db.Movies.RemoveRange(existingMovies);

                    var existingSeries = await db.Series
                        .Include(series => series.Seasons!)
                        .ThenInclude(season => season.Episodes!)
                        .Where(series => series.SourceProfileId == sourceProfileId)
                        .ToListAsync();
                    db.Series.RemoveRange(existingSeries);

                    await db.SaveChangesAsync();

                    db.ChannelCategories.AddRange(categoriesDict.Values);
                    db.Movies.AddRange(movieList);
                    db.Series.AddRange(seriesList);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(state => state.SourceProfileId == sourceProfileId);
                    if (syncState != null)
                    {
                        syncState.LastAttempt = DateTime.UtcNow;
                        syncState.HttpStatusCode = 200;
                        syncState.ErrorLog = BuildImportSummary(
                            importMode,
                            totalChannels,
                            totalMovies,
                            totalEpisodes,
                            totalSeries,
                            diagnostics);
                    }

                    var profile = await db.SourceProfiles.FirstOrDefaultAsync(source => source.Id == sourceProfileId);
                    if (profile != null)
                    {
                        profile.LastSync = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync();
                    await _sourceEnrichmentService.PrepareLiveCatalogAsync(db, sourceProfileId, acquisitionSession);
                    await transaction.CommitAsync();

                    acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.LiveChannel, totalChannels);
                    acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Movie, totalMovies);
                    acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Series, totalSeries);
                    acquisitionSession?.RegisterAccepted(SourceAcquisitionItemKind.Episode, totalEpisodes);

                    await LogPersistedDiagnosticsAsync(sourceProfileId, db, totalChannels, totalMovies, totalSeries);
                    if (refreshHealth)
                    {
                        await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(state => state.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = ResolveFailureStatusCode(ex);
                    syncState.ErrorLog = $"M3U import failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                throw;
            }
        }

        private Movie BuildMovie(int sourceProfileId, M3uEntry entry)
        {
            var normalized = _catalogNormalizationService.NormalizeMovie(
                SourceType.M3U,
                entry.Name,
                entry.GroupName);

            var movie = new Movie
            {
                SourceProfileId = sourceProfileId,
                ExternalId = string.Empty,
                Title = normalized.Title,
                RawSourceTitle = normalized.RawTitle,
                StreamUrl = entry.Url,
                PosterUrl = entry.LogoUrl,
                CategoryName = normalized.CategoryName,
                RawSourceCategoryName = normalized.RawCategoryName,
                ContentKind = normalized.ContentKind
            };
            CatalogFingerprinting.Apply(movie);
            return movie;
        }

        private static bool IsVodGroupUnsafe(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return false;
            }

            if (ContentClassifier.IsGarbageCategoryName(groupName))
            {
                return true;
            }

            var lower = groupName.Trim().ToLowerInvariant();
            if (lower.Contains("xxx") || lower.Contains("adult") ||
                lower.Contains("18+") || lower.Contains("erotic") ||
                lower.Contains("softcore") || lower.Contains("hardcore"))
            {
                return true;
            }

            if (lower.Contains("reseller") || lower.Contains("iptv pack") ||
                lower.Contains("playlist") || lower.Contains("package") ||
                lower.Contains("trial") || lower.Contains("credits") ||
                lower.Contains("placeholder") || lower.Contains("stream pack") ||
                lower.Contains("channel pack") || lower.Contains("provider"))
            {
                return true;
            }

            return false;
        }

        private static bool IsTitleAmbiguouslyCategory(
            string cleanedSeriesTitle,
            string groupName,
            HashSet<string> normalizedCategoryLabels)
        {
            if (string.IsNullOrWhiteSpace(cleanedSeriesTitle))
            {
                return true;
            }

            var lower = cleanedSeriesTitle.Trim().ToLowerInvariant();
            if (lower.Length < 2)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(groupName) &&
                string.Equals(lower, groupName.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }

            return normalizedCategoryLabels.Contains(lower);
        }

        private static (List<Series> Series, List<Movie> DemotedMovies) BuildSeriesList(
            int sourceProfileId,
            List<EpisodeEntry> entries,
            HashSet<string> normalizedCategoryLabels,
            ICatalogNormalizationService catalogNormalizationService,
            SourceAcquisitionSession? acquisitionSession)
        {
            var seriesResult = new List<Series>();
            var demoted = new List<Movie>();

            var groups = entries
                .GroupBy(entry => entry.GroupingKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                var items = group.ToList();
                var key = group.Key ?? string.Empty;

                if (string.IsNullOrWhiteSpace(key) ||
                    key.Length < 2 ||
                    normalizedCategoryLabels.Contains(key) ||
                    ContentClassifier.IsM3uBucketOrAdultLabel(key))
                {
                    foreach (var episode in items)
                    {
                        acquisitionSession?.RecordDemotion(
                            "acquire.m3u.series_group_invalid",
                            "Episode grouping collapsed into a category-like or unsafe title, so the row was demoted to a movie.",
                            episode.Name,
                            episode.GroupName,
                            episode.SeriesTitle,
                            episode.GroupName);
                        demoted.Add(DemoteEpisodeToMovie(sourceProfileId, episode, catalogNormalizationService));
                    }

                    continue;
                }

                var hasStrong = items.Any(item => item.HasStrongMarker);
                var enoughItems = items.Count >= 2;

                var distinctCoords = items
                    .Select(item => (item.SeasonNumber, item.EpisodeNumber))
                    .Where(coord => coord.EpisodeNumber > 0)
                    .Distinct()
                    .Count();

                var confident = hasStrong && enoughItems && distinctCoords >= 2;
                if (!confident)
                {
                    foreach (var episode in items)
                    {
                        acquisitionSession?.RecordDemotion(
                            "acquire.m3u.series_group_low_confidence",
                            "Series grouping did not have enough stable episode evidence, so the row was demoted to a movie.",
                            episode.Name,
                            episode.GroupName,
                            episode.SeriesTitle,
                            episode.GroupName);
                        demoted.Add(DemoteEpisodeToMovie(sourceProfileId, episode, catalogNormalizationService));
                    }

                    continue;
                }

                var displayTitle = items
                    .GroupBy(item => item.SeriesTitle, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(bucket => bucket.Count())
                    .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;

                var rawCategoryName = items
                    .Where(item => item.HadExplicitGroupTitle)
                    .GroupBy(item => item.GroupName, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(bucket => bucket.Count())
                    .Select(bucket => bucket.Key)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(rawCategoryName))
                {
                    rawCategoryName = items
                        .GroupBy(item => item.GroupName, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(bucket => bucket.Count())
                        .First()
                        .Key;
                }

                var representativeRawTitle = items
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(bucket => bucket.Count())
                    .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;

                var effectiveCategoryName = ContentClassifier.ResolveSurfacedSeriesCategory(
                    rawCategoryName,
                    rawCategoryName,
                    displayTitle);

                var normalized = catalogNormalizationService.NormalizeSeries(
                    SourceType.M3U,
                    representativeRawTitle,
                    effectiveCategoryName);

                var normalizedDisplay = catalogNormalizationService.NormalizeSeries(
                    SourceType.M3U,
                    displayTitle,
                    effectiveCategoryName);

                var logo = items
                    .Select(item => item.LogoUrl)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

                var series = new Series
                {
                    SourceProfileId = sourceProfileId,
                    ExternalId = string.Empty,
                    Title = string.IsNullOrWhiteSpace(normalizedDisplay.Title) ? displayTitle : normalizedDisplay.Title,
                    RawSourceTitle = representativeRawTitle,
                    CategoryName = ContentClassifier.ResolveSurfacedSeriesCategory(
                        normalized.CategoryName,
                        rawCategoryName,
                        string.IsNullOrWhiteSpace(normalizedDisplay.Title) ? displayTitle : normalizedDisplay.Title),
                    RawSourceCategoryName = rawCategoryName,
                    ContentKind = normalized.ContentKind,
                    PosterUrl = logo,
                    Seasons = new List<Season>()
                };
                CatalogFingerprinting.Apply(series);

                var seasonGroups = items.GroupBy(item => item.SeasonNumber).OrderBy(bucket => bucket.Key);
                foreach (var seasonGroup in seasonGroups)
                {
                    var season = new Season
                    {
                        SeasonNumber = seasonGroup.Key,
                        Episodes = new List<Episode>()
                    };

                    var autoEpisodeNumber = 1;
                    var ordered = seasonGroup
                        .OrderBy(item => item.EpisodeNumber > 0 ? item.EpisodeNumber : int.MaxValue)
                        .ThenBy(item => item.Name);

                    foreach (var episodeEntry in ordered)
                    {
                        var episodeNumber = episodeEntry.EpisodeNumber > 0
                            ? episodeEntry.EpisodeNumber
                            : autoEpisodeNumber;
                        autoEpisodeNumber = episodeNumber + 1;

                        season.Episodes!.Add(new Episode
                        {
                            ExternalId = string.Empty,
                            Title = !string.IsNullOrWhiteSpace(episodeEntry.EpisodeTitle)
                                ? episodeEntry.EpisodeTitle
                                : episodeEntry.Name,
                            StreamUrl = episodeEntry.Url,
                            EpisodeNumber = episodeNumber
                        });
                    }

                    ((List<Season>)series.Seasons!).Add(season);
                }

                seriesResult.Add(series);
            }

            return (seriesResult, demoted);
        }

        private static Movie DemoteEpisodeToMovie(
            int sourceProfileId,
            EpisodeEntry episode,
            ICatalogNormalizationService catalogNormalizationService)
        {
            var normalized = catalogNormalizationService.NormalizeMovie(
                SourceType.M3U,
                episode.Name,
                episode.GroupName);

            var movie = new Movie
            {
                SourceProfileId = sourceProfileId,
                ExternalId = string.Empty,
                Title = normalized.Title,
                RawSourceTitle = normalized.RawTitle,
                StreamUrl = episode.Url,
                PosterUrl = episode.LogoUrl,
                CategoryName = normalized.CategoryName,
                RawSourceCategoryName = normalized.RawCategoryName,
                ContentKind = normalized.ContentKind
            };
            CatalogFingerprinting.Apply(movie);
            return movie;
        }

        private async Task<string> ReadPlaylistAsync(string location, SourceCredential credential)
        {
            if (location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = _sourceRoutingService.CreateHttpClient(credential, SourceNetworkPurpose.Import, TimeSpan.FromSeconds(60));
                return await client.GetStringAsync(location);
            }

            return await System.IO.File.ReadAllTextAsync(location);
        }

        private static string ResolveEntryName(M3uExtinfMetadata extinf)
        {
            var displayName = ContentClassifier.NormalizeLabel(extinf.DisplayName);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            var tvgName = ContentClassifier.NormalizeLabel(M3uMetadataParser.GetFirstAttributeValue(
                extinf.Attributes,
                "tvg-name"));

            return string.IsNullOrWhiteSpace(tvgName) ? "Unknown Channel" : tvgName;
        }

        private static string NormalizeGroupName(string groupName)
        {
            var normalized = ContentClassifier.NormalizeLabel(groupName ?? string.Empty);
            return string.IsNullOrWhiteSpace(normalized) ? DefaultGroupName : normalized;
        }

        private static int GetUncategorizedCount(
            Dictionary<string, ChannelCategory> categoriesDict,
            List<Movie> movieList,
            List<Series> seriesList)
        {
            var liveUncategorizedCount = categoriesDict.TryGetValue(DefaultGroupName, out var uncategorized)
                ? uncategorized.Channels?.Count ?? 0
                : 0;

            return liveUncategorizedCount
                + movieList.Count(movie => string.Equals(movie.CategoryName, DefaultGroupName, StringComparison.OrdinalIgnoreCase))
                + seriesList.Count(series => string.Equals(series.CategoryName, DefaultGroupName, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildImportSummary(
            M3uImportMode importMode,
            int totalChannels,
            int totalMovies,
            int totalEpisodes,
            int totalSeries,
            M3uImportDiagnostics diagnostics)
        {
            var summary = importMode == M3uImportMode.LiveOnly
                ? $"Parsed {totalChannels} channels (LiveOnly mode - VOD suppressed)."
                : $"Parsed {totalChannels} channels, {totalMovies} movies, {totalEpisodes} episodes across {totalSeries} series. Mode: {importMode}.";

            return $"{summary} Uncategorized={diagnostics.UncategorizedCount}, pseudo_rejected={diagnostics.PseudoItemRejectedCount}, xmltv_found={(diagnostics.XmltvUrlFound ? "yes" : "no")}.";
        }

        private static void LogHeaderDiagnostics(int sourceProfileId, M3uHeaderMetadata headerMetadata)
        {
            var formattedAttributes = headerMetadata.Attributes.Count == 0
                ? "none"
                : string.Join(
                    ";",
                    headerMetadata.Attributes
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => $"{pair.Key}=[{string.Join("|", pair.Value)}]"));

            ImportRuntimeLogger.Log(
                "M3U IMPORT",
                $"source_profile_id={sourceProfileId}; header_preview={FormatDiagnosticValue(headerMetadata.RawHeaderPreview)}; header_attributes={FormatDiagnosticValue(formattedAttributes)}; xmltv_url_found={(headerMetadata.XmltvUrls.Count > 0 ? "true" : "false")}; xmltv_url_value={FormatDiagnosticValue(string.Join(" | ", headerMetadata.XmltvUrls))}");
        }

        private static void LogImportDiagnostics(int sourceProfileId, M3uImportDiagnostics diagnostics)
        {
            ImportRuntimeLogger.Log(
                "M3U IMPORT",
                $"source_profile_id={sourceProfileId}; live_count={diagnostics.LiveCount}; movie_count={diagnostics.MovieCount}; series_count={diagnostics.SeriesCount}; with_group_title_count={diagnostics.WithGroupTitleCount}; with_tvg_logo_count={diagnostics.WithTvgLogoCount}; with_tvg_id_count={diagnostics.WithTvgIdCount}; uncategorized_count={diagnostics.UncategorizedCount}; pseudo_item_rejected_count={diagnostics.PseudoItemRejectedCount}; xmltv_url_found={(diagnostics.XmltvUrlFound ? "true" : "false")}; xmltv_url_value={FormatDiagnosticValue(diagnostics.XmltvUrlValue)}");
        }

        private static async Task LogPersistedDiagnosticsAsync(
            int sourceProfileId,
            AppDbContext db,
            int parserLiveCount,
            int parserMovieCount,
            int parserSeriesCount)
        {
            var persistedLiveCount = await db.ChannelCategories
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels,
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => channel.Id)
                .CountAsync();

            var persistedMovieCount = await db.Movies.CountAsync(movie => movie.SourceProfileId == sourceProfileId);
            var persistedSeriesCount = await db.Series.CountAsync(series => series.SourceProfileId == sourceProfileId);

            CatalogCountDiagnosticsLogger.Log(
                "m3u_import",
                "live",
                sourceProfileId.ToString(),
                parserLiveCount,
                persistedLiveCount,
                persistedLiveCount,
                persistedLiveCount);
            CatalogCountDiagnosticsLogger.Log(
                "m3u_import",
                "movie",
                sourceProfileId.ToString(),
                parserMovieCount,
                persistedMovieCount,
                persistedMovieCount,
                persistedMovieCount);
            CatalogCountDiagnosticsLogger.Log(
                "m3u_import",
                "series",
                sourceProfileId.ToString(),
                parserSeriesCount,
                persistedSeriesCount,
                persistedSeriesCount,
                persistedSeriesCount);
        }

        private static string FormatDiagnosticValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "'")}\"";
        }

        private static int ResolveFailureStatusCode(Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx when httpEx.StatusCode.HasValue => (int)httpEx.StatusCode.Value,
                TaskCanceledException => (int)HttpStatusCode.RequestTimeout,
                TimeoutException => (int)HttpStatusCode.RequestTimeout,
                FileNotFoundException => (int)HttpStatusCode.NotFound,
                UnauthorizedAccessException => (int)HttpStatusCode.Forbidden,
                _ => (int)HttpStatusCode.InternalServerError
            };
        }

        private sealed class M3uEntry
        {
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string TvgType { get; set; } = string.Empty;
            public string TvgId { get; set; } = string.Empty;
            public string TvgName { get; set; } = string.Empty;
            public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool HadExplicitGroupTitle { get; set; }
        }

        private sealed class EpisodeEntry
        {
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string TvgType { get; set; } = string.Empty;
            public string SeriesTitle { get; set; } = string.Empty;
            public string GroupingKey { get; set; } = string.Empty;
            public int SeasonNumber { get; set; } = 1;
            public int EpisodeNumber { get; set; }
            public string EpisodeTitle { get; set; } = string.Empty;
            public bool HasStrongMarker { get; set; }
            public bool HadExplicitGroupTitle { get; set; }
        }

        private sealed class PendingExtinf
        {
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string TvgType { get; set; } = string.Empty;
            public string TvgId { get; set; } = string.Empty;
            public string TvgName { get; set; } = string.Empty;
            public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool HadExplicitGroupTitle { get; set; }
        }

        private sealed class M3uImportDiagnostics
        {
            public int LiveCount { get; set; }
            public int MovieCount { get; set; }
            public int SeriesCount { get; set; }
            public int WithGroupTitleCount { get; set; }
            public int WithTvgLogoCount { get; set; }
            public int WithTvgIdCount { get; set; }
            public int UncategorizedCount { get; set; }
            public int PseudoItemRejectedCount { get; set; }
            public bool XmltvUrlFound { get; set; }
            public string XmltvUrlValue { get; set; } = string.Empty;
        }
    }
}
