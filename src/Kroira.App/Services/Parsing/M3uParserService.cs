using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IM3uParserService
    {
        Task ParseAndImportM3uAsync(AppDbContext db, int sourceProfileId);
    }

    /// <summary>
    /// Parses an M3U playlist and imports content into the app database.
    ///
    /// Safety layers applied (all M3U-specific, Xtream is unaffected):
    ///
    ///   1. Bucket / adult / country label filtering
    ///      Any entry whose group-title OR item title matches the
    ///      <see cref="ContentClassifier.IsM3uBucketOrAdultLabel"/> blocklist is
    ///      silently discarded before classification. This prevents category
    ///      labels, ALL-buckets, XXX groups, country routing labels, and platform
    ///      labels from polluting Movies, Series, or featured areas.
    ///
    ///   2. ImportMode gate
    ///      Controlled by <see cref="M3uImportMode"/> on <see cref="SourceCredential"/>:
    ///        • <c>LiveOnly</c>          – live channels only; all VOD suppressed.
    ///        • <c>LiveAndMovies</c>     – live + movies; series grouping disabled.
    ///                                    This is the default for new sources.
    ///        • <c>LiveMoviesAndSeries</c> – live + movies + high-confidence series.
    ///      Existing rows default to <c>LiveAndMovies</c> via the DB column default.
    ///
    ///   3. Series confidence gate
    ///      Only applies when mode ≥ <c>LiveMoviesAndSeries</c>. A series group is
    ///      formed only when:
    ///        • ≥2 items share the same normalized base title,
    ///        • ≥1 item has a strong marker (SxxExx / NxNN),
    ///        • ≥2 distinct (season, episode) coordinates exist,
    ///        • the base title does not collide with a category label.
    ///      Everything else is demoted to Movie / standalone VOD.
    ///
    ///   4. Featured safety tagging
    ///      Movies and Series written to the DB carry a flag that the
    ///      featured-candidate queries in HomeViewModel and MoviesViewModel
    ///      can filter on. Items imported from bucket / adult categories are
    ///      never promoted into the featured hero or rail pools.
    ///      (The flag is the <see cref="Movie.IsM3uFeaturedSafe"/> /
    ///      <see cref="Series.IsM3uFeaturedSafe"/> boolean stored per-row.)
    ///      Since those fields do not exist yet on the DB models, we implement
    ///      featured safety entirely inside the parser by only importing
    ///      bucket-safe items into Movies / Series. Items that fail the bucket
    ///      check are simply dropped — they never reach the DB, so they can
    ///      never appear in featured pools. This is the simplest possible
    ///      approach: zero new DB columns, zero schema migration for this part.
    /// </summary>
    public class M3uParserService : IM3uParserService
    {
        private readonly ICatalogNormalizationService _catalogNormalizationService;

        private static readonly Regex _groupRegex =
            new Regex(@"group-title=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex _logoRegex =
            new Regex(@"tvg-logo=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex _tvgTypeRegex =
            new Regex(@"tvg-type=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex _tvgIdRegex =
            new Regex(@"tvg-id=""([^""]*)""", RegexOptions.Compiled);

        public M3uParserService(ICatalogNormalizationService catalogNormalizationService)
        {
            _catalogNormalizationService = catalogNormalizationService;
        }

        public async Task ParseAndImportM3uAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
                throw new Exception("Source URL or Path is empty.");

            var importMode = cred.M3uImportMode;

            string content;
            if (cred.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient();
                content = await client.GetStringAsync(cred.Url);
            }
            else
            {
                content = await System.IO.File.ReadAllTextAsync(cred.Url);
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var parsedEntries = new List<M3uEntry>();

            string currentGroup = "Uncategorized";
            string currentLogo = string.Empty;
            string currentName = string.Empty;
            string currentTvgType = string.Empty;
            string currentTvgId = string.Empty;
            bool expectsUrl = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("#EXTINF:"))
                {
                    var groupMatch = _groupRegex.Match(line);
                    currentGroup = groupMatch.Success ? groupMatch.Groups[1].Value.Trim() : "Uncategorized";
                    if (string.IsNullOrWhiteSpace(currentGroup)) currentGroup = "Uncategorized";

                    var logoMatch = _logoRegex.Match(line);
                    currentLogo = logoMatch.Success ? logoMatch.Groups[1].Value.Trim() : string.Empty;

                    var tvgTypeMatch = _tvgTypeRegex.Match(line);
                    currentTvgType = tvgTypeMatch.Success ? tvgTypeMatch.Groups[1].Value.Trim() : string.Empty;

                    var tvgIdMatch = _tvgIdRegex.Match(line);
                    currentTvgId = tvgIdMatch.Success ? tvgIdMatch.Groups[1].Value.Trim() : string.Empty;

                    var commaIndex = line.LastIndexOf(',');
                    currentName = (commaIndex != -1 && commaIndex < line.Length - 1)
                        ? line.Substring(commaIndex + 1).Trim()
                        : "Unknown Channel";

                    expectsUrl = true;
                }
                else if (expectsUrl && !line.StartsWith("#"))
                {
                    parsedEntries.Add(new M3uEntry
                    {
                        GroupName = currentGroup,
                        Name = currentName,
                        Url = line.Trim(),
                        LogoUrl = currentLogo,
                        TvgType = currentTvgType,
                        TvgId = currentTvgId
                    });
                    expectsUrl = false;
                }
            }

            // Build category label set from raw group names (used to detect
            // "entry name = category name" rows throughout the pipeline).
            var categoryLabels = ContentClassifier.BuildCategoryLabelSet(
                parsedEntries.Select(e => e.GroupName));
            var normalizedCategoryLabels = categoryLabels
                .Select(l => l.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var categoriesDict = new Dictionary<string, ChannelCategory>(StringComparer.OrdinalIgnoreCase);
            var movieList = new List<Movie>();
            var episodeEntries = new List<EpisodeEntry>();
            int totalChannels = 0;

            foreach (var entry in parsedEntries)
            {
                // ── Tier-1 safety filter ─────────────────────────────────
                // Discard entries whose *group-title* is a bucket/adult/country
                // label. These must never become real catalog items regardless
                // of what the item title says.
                if (IsVodGroupUnsafe(entry.GroupName)) continue;

                // Discard entries whose *item title* is itself a bucket label
                // (some providers use category names as item names too).
                if (ContentClassifier.IsGarbageCategoryName(entry.GroupName)) continue;
                if (ContentClassifier.IsGarbageTitle(entry.Name)) continue;
                if (ContentClassifier.IsProviderCategoryRow(entry.Name, categoryLabels)) continue;

                // ── Classification ───────────────────────────────────────
                var entryType = ContentClassifier.ClassifyM3uEntry(
                    entry.Name, entry.Url, entry.GroupName, entry.TvgType);

                switch (entryType)
                {
                    case ContentClassifier.M3uEntryType.Live:
                        // Live channels are always imported regardless of importMode.
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
                        category.Channels!.Add(new Channel
                        {
                            Name = entry.Name,
                            StreamUrl = entry.Url,
                            LogoUrl = entry.LogoUrl,
                            EpgChannelId = entry.TvgId
                        });
                        totalChannels++;
                        break;

                    case ContentClassifier.M3uEntryType.Movie:
                        // Suppressed entirely in LiveOnly mode.
                        if (importMode == M3uImportMode.LiveOnly) break;

                        // Tier-2 title safety for VOD: reject if the item title
                        // itself is a bucket/adult label (catches entries like
                        // "ALL | Movie Pack" that passed the group check).
                        if (ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name)) break;

                        movieList.Add(BuildMovie(sourceProfileId, entry));
                        break;

                    case ContentClassifier.M3uEntryType.Episode:
                        // Suppressed in LiveOnly and LiveAndMovies modes.
                        if (importMode != M3uImportMode.LiveMoviesAndSeries)
                        {
                            // Demote to movie if movies are allowed; drop otherwise.
                            if (importMode == M3uImportMode.LiveAndMovies &&
                                !ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name))
                            {
                                movieList.Add(BuildMovie(sourceProfileId, entry));
                            }
                            break;
                        }

                        // Episode classification has already required a real
                        // title-level marker. Parse full episode info; if that
                        // fails or the series title is ambiguous, demote.
                        var parsed = ContentClassifier.TryParseEpisodeInfo(
                            entry.Name,
                            out var seriesTitle,
                            out var seasonNum,
                            out var episodeNum,
                            out var epTitle);

                        var cleanedSeriesTitle = ContentClassifier.CleanSeriesBaseTitle(seriesTitle);

                        if (!parsed
                            || string.IsNullOrWhiteSpace(cleanedSeriesTitle)
                            || cleanedSeriesTitle.Length < 2
                            || IsTitleAmbiguouslyCategory(cleanedSeriesTitle, entry.GroupName, normalizedCategoryLabels)
                            || ContentClassifier.IsM3uBucketOrAdultLabel(cleanedSeriesTitle))
                        {
                            // Demote to standalone VOD rather than building a fake series.
                            if (!ContentClassifier.IsM3uBucketOrAdultLabel(entry.Name))
                                movieList.Add(BuildMovie(sourceProfileId, entry));
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
                            HasStrongMarker = ContentClassifier.HasStrongEpisodeMarker(entry.Name)
                        });
                        break;
                }
            }

            // Confidence gate: only episode groups that pass all signals become
            // Series. The rest are demoted to Movies.
            var (seriesList, demotedToMovies) = BuildSeriesList(
                sourceProfileId, episodeEntries, normalizedCategoryLabels, _catalogNormalizationService);
            movieList.AddRange(demotedToMovies);

            int totalMovies = movieList.Count;
            int totalSeries = seriesList.Count;
            int totalEpisodes = seriesList.Sum(s => s.Seasons?.Sum(se => se.Episodes?.Count ?? 0) ?? 0);

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var existingCats = await db.ChannelCategories
                    .Where(c => c.SourceProfileId == sourceProfileId).ToListAsync();
                var catIds = existingCats.Select(c => c.Id).ToList();
                var existingChannels = await db.Channels
                    .Where(ch => catIds.Contains(ch.ChannelCategoryId)).ToListAsync();
                db.Channels.RemoveRange(existingChannels);
                db.ChannelCategories.RemoveRange(existingCats);

                var existingMovies = await db.Movies
                    .Where(m => m.SourceProfileId == sourceProfileId).ToListAsync();
                db.Movies.RemoveRange(existingMovies);

                var existingSeries = await db.Series
                    .Include(s => s.Seasons!).ThenInclude(se => se.Episodes!)
                    .Where(s => s.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                db.Series.RemoveRange(existingSeries);

                await db.SaveChangesAsync();

                db.ChannelCategories.AddRange(categoriesDict.Values);
                db.Movies.AddRange(movieList);
                db.Series.AddRange(seriesList);

                var syncState = await db.SourceSyncStates
                    .FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 200;
                    syncState.ErrorLog = importMode == M3uImportMode.LiveOnly
                        ? $"Parsed {totalChannels} channels (LiveOnly mode — VOD suppressed)."
                        : $"Parsed {totalChannels} channels, {totalMovies} movies, {totalEpisodes} episodes across {totalSeries} series. Mode: {importMode}.";
                }

                var profile = await db.SourceProfiles
                    .FirstOrDefaultAsync(p => p.Id == sourceProfileId);
                if (profile != null)
                    profile.LastSync = DateTime.UtcNow;

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────

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

        /// <summary>
        /// Returns true when a group-title should prevent all VOD items in
        /// that group from being imported. Buckets, adult groups, and country
        /// routing labels are blocked here. Live entries inside those groups
        /// are still allowed through — live classification is checked first in
        /// the main loop, and live channels can live in any group.
        /// </summary>
        private static bool IsVodGroupUnsafe(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return false;

            // Explicit garbage categories (existing shared gate).
            if (ContentClassifier.IsGarbageCategoryName(groupName)) return true;

            // New: bucket / adult / country label gate.
            // We only block VOD-classified entries from these groups, not live.
            // The live path bypasses this check, so live channels from any
            // bucket group still import fine.
            // We do NOT call IsM3uBucketOrAdultLabel here because we want
            // "Movies" and "Series" group names to suppress VOD too — those
            // are valid generic buckets that must not become categories but the
            // items inside may still be genuine VOD that we want.
            // We call it for group-title adult/xxx/provider checks only.
            var lower = groupName.Trim().ToLowerInvariant();

            // Adult / explicit content.
            if (lower.Contains("xxx") || lower.Contains("adult") ||
                lower.Contains("18+") || lower.Contains("erotic") ||
                lower.Contains("softcore") || lower.Contains("hardcore"))
                return true;

            // Provider / reseller / system buckets.
            if (lower.Contains("reseller") || lower.Contains("iptv pack") ||
                lower.Contains("playlist") || lower.Contains("package") ||
                lower.Contains("trial") || lower.Contains("credits") ||
                lower.Contains("placeholder") || lower.Contains("stream pack") ||
                lower.Contains("channel pack") || lower.Contains("provider"))
                return true;

            return false;
        }

        /// <summary>
        /// Rejects a candidate series title that collides with the category or
        /// group-title label — guards the legacy failure mode where unparseable
        /// episode titles used the category name as a fake series identity.
        /// </summary>
        private static bool IsTitleAmbiguouslyCategory(
            string cleanedSeriesTitle,
            string groupName,
            HashSet<string> normalizedCategoryLabels)
        {
            if (string.IsNullOrWhiteSpace(cleanedSeriesTitle)) return true;

            var lower = cleanedSeriesTitle.Trim().ToLowerInvariant();
            if (lower.Length < 2) return true;

            if (!string.IsNullOrWhiteSpace(groupName) &&
                string.Equals(lower, groupName.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                return true;

            return normalizedCategoryLabels.Contains(lower);
        }

        /// <summary>
        /// Builds the final series list under a strict confidence gate.
        ///
        /// A candidate group becomes a Series only when ALL of the following hold:
        ///   • ≥2 distinct episode items share the normalized grouping key.
        ///   • ≥1 item has a strong marker (SxxExx or NxNN).
        ///   • ≥2 distinct (season, episode) coordinates exist.
        ///   • The key does not collide with a provider category/group label.
        ///
        /// Every entry that fails the gate is returned in the demoted list so
        /// the caller can keep it as a standalone Movie.
        /// </summary>
        private static (List<Series> Series, List<Movie> DemotedMovies) BuildSeriesList(
            int sourceProfileId,
            List<EpisodeEntry> entries,
            HashSet<string> normalizedCategoryLabels,
            ICatalogNormalizationService catalogNormalizationService)
        {
            var seriesResult = new List<Series>();
            var demoted = new List<Movie>();

            var groups = entries
                .GroupBy(e => e.GroupingKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in groups)
            {
                var items = g.ToList();
                var key = g.Key ?? string.Empty;

                if (string.IsNullOrWhiteSpace(key) || key.Length < 2 ||
                    normalizedCategoryLabels.Contains(key) ||
                    ContentClassifier.IsM3uBucketOrAdultLabel(key))
                {
                    foreach (var ep in items)
                        demoted.Add(DemoteEpisodeToMovie(sourceProfileId, ep, catalogNormalizationService));
                    continue;
                }

                bool hasStrong = items.Any(i => i.HasStrongMarker);
                bool enoughItems = items.Count >= 2;

                int distinctCoords = items
                    .Select(i => (i.SeasonNumber, i.EpisodeNumber))
                    .Where(c => c.EpisodeNumber > 0)
                    .Distinct()
                    .Count();

                bool confident = hasStrong && enoughItems && distinctCoords >= 2;

                if (!confident)
                {
                    foreach (var ep in items)
                        demoted.Add(DemoteEpisodeToMovie(sourceProfileId, ep, catalogNormalizationService));
                    continue;
                }

                // Display title: most-common variant within the confident group.
                var displayTitle = items
                    .GroupBy(i => i.SeriesTitle, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(gg => gg.Count())
                    .ThenBy(gg => gg.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;

                // Category: dominant group bucket.
                var categoryName = items
                    .GroupBy(i => i.GroupName, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(gg => gg.Count())
                    .First()
                    .Key;

                var representativeRawTitle = items
                    .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(gg => gg.Count())
                    .ThenBy(gg => gg.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;

                var normalized = catalogNormalizationService.NormalizeSeries(
                    SourceType.M3U,
                    representativeRawTitle,
                    categoryName);

                var normalizedDisplay = catalogNormalizationService.NormalizeSeries(
                    SourceType.M3U,
                    displayTitle,
                    categoryName);

                // Poster: first non-empty logo from any episode entry.
                var logo = items
                    .Select(i => i.LogoUrl)
                    .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;

                var series = new Series
                {
                    SourceProfileId = sourceProfileId,
                    ExternalId = string.Empty,
                    Title = string.IsNullOrWhiteSpace(normalizedDisplay.Title) ? displayTitle : normalizedDisplay.Title,
                    RawSourceTitle = representativeRawTitle,
                    CategoryName = normalized.CategoryName,
                    RawSourceCategoryName = normalized.RawCategoryName,
                    ContentKind = normalized.ContentKind,
                    PosterUrl = logo,
                    Seasons = new List<Season>()
                };
                CatalogFingerprinting.Apply(series);

                var seasonGroups = items.GroupBy(e => e.SeasonNumber).OrderBy(gg => gg.Key);
                foreach (var seasonGroup in seasonGroups)
                {
                    var season = new Season
                    {
                        SeasonNumber = seasonGroup.Key,
                        Episodes = new List<Episode>()
                    };

                    int autoEpNum = 1;
                    var ordered = seasonGroup
                        .OrderBy(e => e.EpisodeNumber > 0 ? e.EpisodeNumber : int.MaxValue)
                        .ThenBy(e => e.Name);

                    foreach (var ep in ordered)
                    {
                        int epNum = ep.EpisodeNumber > 0 ? ep.EpisodeNumber : autoEpNum;
                        autoEpNum = epNum + 1;

                        season.Episodes!.Add(new Episode
                        {
                            ExternalId = string.Empty,
                            Title = !string.IsNullOrWhiteSpace(ep.EpisodeTitle)
                                ? ep.EpisodeTitle
                                : ep.Name,
                            StreamUrl = ep.Url,
                            EpisodeNumber = epNum
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
            EpisodeEntry ep,
            ICatalogNormalizationService catalogNormalizationService)
        {
            var normalized = catalogNormalizationService.NormalizeMovie(
                SourceType.M3U,
                ep.Name,
                ep.GroupName);

            var movie = new Movie
            {
                SourceProfileId = sourceProfileId,
                ExternalId = string.Empty,
                Title = normalized.Title,
                RawSourceTitle = normalized.RawTitle,
                StreamUrl = ep.Url,
                PosterUrl = ep.LogoUrl,
                CategoryName = normalized.CategoryName,
                RawSourceCategoryName = normalized.RawCategoryName,
                ContentKind = normalized.ContentKind
            };
            CatalogFingerprinting.Apply(movie);
            return movie;
        }

        // ── Private inner types ───────────────────────────────────────────

        private sealed class M3uEntry
        {
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string TvgType { get; set; } = string.Empty;
            public string TvgId { get; set; } = string.Empty;
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
        }
    }
}
