#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceHealthService
    {
        Task RefreshSourceHealthAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool forceProbe = false);
    }

    public sealed class SourceHealthService : ISourceHealthService
    {
        private readonly ISourceProbeService _sourceProbeService;
        private readonly ISourceRoutingService _sourceRoutingService;
        private readonly IProviderStreamResolverService _providerStreamResolverService;

        private static readonly HashSet<string> ValidStreamSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            Uri.UriSchemeHttp,
            Uri.UriSchemeHttps,
            Uri.UriSchemeFile,
            "rtmp",
            "rtmps",
            "rtsp",
            "udp",
            "mms",
            "stalker"
        };

        private static readonly IReadOnlyDictionary<SourceHealthComponentType, int> ComponentWeights =
            new Dictionary<SourceHealthComponentType, int>
            {
                [SourceHealthComponentType.Catalog] = 30,
                [SourceHealthComponentType.Live] = 16,
                [SourceHealthComponentType.Vod] = 16,
                [SourceHealthComponentType.Epg] = 14,
                [SourceHealthComponentType.Logos] = 8,
                [SourceHealthComponentType.Freshness] = 16
            };

        public SourceHealthService(
            ISourceProbeService sourceProbeService,
            ISourceRoutingService sourceRoutingService,
            IProviderStreamResolverService providerStreamResolverService)
        {
            _sourceProbeService = sourceProbeService;
            _sourceRoutingService = sourceRoutingService;
            _providerStreamResolverService = providerStreamResolverService;
        }

        public async Task RefreshSourceHealthAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool forceProbe = false)
        {
            var profile = await db.SourceProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == sourceProfileId);
            if (profile == null)
            {
                return;
            }

            var syncState = await db.SourceSyncStates.AsNoTracking().FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            var credential = await db.SourceCredentials.AsNoTracking().FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            var epgLog = await db.EpgSyncLogs.AsNoTracking().FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);

            var liveChannels = await db.ChannelCategories
                .AsNoTracking()
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels.AsNoTracking(),
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => new LiveChannelRecord
                    {
                        Id = channel.Id,
                        Name = channel.Name,
                        StreamUrl = channel.StreamUrl,
                        LogoUrl = channel.LogoUrl,
                        ProviderLogoUrl = channel.ProviderLogoUrl,
                        EpgChannelId = channel.EpgChannelId,
                        ProviderEpgChannelId = channel.ProviderEpgChannelId,
                        EpgMatchSource = channel.EpgMatchSource,
                        LogoSource = channel.LogoSource,
                        CategoryName = category.Name
                    })
                .ToListAsync();

            var movies = await db.Movies
                .AsNoTracking()
                .Where(movie => movie.SourceProfileId == sourceProfileId)
                .Select(movie => new MovieRecord
                {
                    Id = movie.Id,
                    Title = movie.Title,
                    StreamUrl = movie.StreamUrl,
                    CategoryName = movie.CategoryName,
                    RawCategoryName = movie.RawSourceCategoryName,
                    DedupFingerprint = movie.DedupFingerprint
                })
                .ToListAsync();

            var series = await db.Series
                .AsNoTracking()
                .Where(item => item.SourceProfileId == sourceProfileId)
                .Select(item => new SeriesRecord
                {
                    Id = item.Id,
                    Title = item.Title,
                    CategoryName = item.CategoryName,
                    RawCategoryName = item.RawSourceCategoryName,
                    DedupFingerprint = item.DedupFingerprint
                })
                .ToListAsync();

            var episodes = await db.Episodes
                .AsNoTracking()
                .Join(
                    db.Seasons.AsNoTracking(),
                    episode => episode.SeasonId,
                    season => season.Id,
                    (episode, season) => new { episode, season })
                .Join(
                    db.Series
                        .AsNoTracking()
                        .Where(item => item.SourceProfileId == sourceProfileId),
                    item => item.season.SeriesId,
                    series => series.Id,
                    (item, series) => new EpisodeRecord
                    {
                        Id = item.episode.Id,
                        Title = item.episode.Title,
                        StreamUrl = item.episode.StreamUrl,
                        SeriesTitle = series.Title,
                        SeasonNumber = item.season.SeasonNumber,
                        EpisodeNumber = item.episode.EpisodeNumber
                    })
                .ToListAsync();

            var report = await db.SourceHealthReports
                .Include(item => item.Components)
                .Include(item => item.Probes)
                .Include(item => item.Issues)
                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);

            var guideMetrics = await BuildGuideMetricsAsync(db, liveChannels.Select(channel => channel.Id).ToList());
            var probes = await ResolveProbesAsync(db, profile, credential, liveChannels, movies, episodes, report, forceProbe);
            var evaluation = Evaluate(profile, syncState, credential, epgLog, liveChannels, movies, series, guideMetrics, probes);
            if (acquisitionSession != null)
            {
                acquisitionSession.SetValidationSummary(evaluation.ValidationSummary);

                foreach (var probe in evaluation.Probes.OrderBy(item => item.SortOrder))
                {
                    acquisitionSession.RecordValidationProbe(
                        probe.ProbeType,
                        probe.Status,
                        probe.SuccessCount,
                        probe.FailureCount,
                        probe.Summary);
                }

                foreach (var issue in evaluation.Issues
                             .OrderByDescending(item => item.Severity)
                             .ThenBy(item => item.SortOrder)
                             .Take(8))
                {
                    acquisitionSession.RecordValidationIssue(
                        issue.Severity,
                        issue.Code,
                        issue.Title,
                        issue.Message);
                }
            }

            if (report == null)
            {
                report = new SourceHealthReport { SourceProfileId = sourceProfileId };
                db.SourceHealthReports.Add(report);
            }
            else
            {
                if (report.Components.Count > 0)
                {
                    db.SourceHealthComponents.RemoveRange(report.Components);
                    report.Components.Clear();
                }

                if (report.Probes.Count > 0)
                {
                    db.SourceHealthProbes.RemoveRange(report.Probes);
                    report.Probes.Clear();
                }

                if (report.Issues.Count > 0)
                {
                    db.SourceHealthIssues.RemoveRange(report.Issues);
                    report.Issues.Clear();
                }
            }

            report.EvaluatedAtUtc = evaluation.EvaluatedAtUtc;
            report.LastSyncAttemptAtUtc = evaluation.LastSyncAttemptAtUtc;
            report.LastSuccessfulSyncAtUtc = evaluation.LastSuccessfulSyncAtUtc;
            report.HealthScore = evaluation.HealthScore;
            report.HealthState = evaluation.HealthState;
            report.StatusSummary = Trim(evaluation.StatusSummary, 280);
            report.ImportResultSummary = Trim(evaluation.ImportResultSummary, 280);
            report.ValidationSummary = Trim(evaluation.ValidationSummary, 280);
            report.TopIssueSummary = Trim(evaluation.TopIssueSummary, 360);
            report.TotalChannelCount = evaluation.TotalChannelCount;
            report.TotalMovieCount = evaluation.TotalMovieCount;
            report.TotalSeriesCount = evaluation.TotalSeriesCount;
            report.DuplicateCount = evaluation.DuplicateCount;
            report.InvalidStreamCount = evaluation.InvalidStreamCount;
            report.ChannelsWithEpgMatchCount = evaluation.ChannelsWithEpgMatchCount;
            report.ChannelsWithCurrentProgramCount = evaluation.ChannelsWithCurrentProgramCount;
            report.ChannelsWithNextProgramCount = evaluation.ChannelsWithNextProgramCount;
            report.ChannelsWithLogoCount = evaluation.ChannelsWithLogoCount;
            report.SuspiciousEntryCount = evaluation.SuspiciousEntryCount;
            report.WarningCount = evaluation.WarningCount;
            report.ErrorCount = evaluation.ErrorCount;

            foreach (var component in evaluation.Components.OrderBy(item => item.SortOrder))
            {
                report.Components.Add(new SourceHealthComponent
                {
                    ComponentType = component.ComponentType,
                    State = component.State,
                    Score = component.Score,
                    Summary = Trim(component.Summary, 220),
                    RelevantCount = component.RelevantCount,
                    HealthyCount = component.HealthyCount,
                    IssueCount = component.IssueCount,
                    SortOrder = component.SortOrder
                });
            }

            foreach (var probe in evaluation.Probes.OrderBy(item => item.SortOrder))
            {
                report.Probes.Add(new SourceHealthProbe
                {
                    ProbeType = probe.ProbeType,
                    Status = probe.Status,
                    ProbedAtUtc = probe.ProbedAtUtc,
                    CandidateCount = probe.CandidateCount,
                    SampleSize = probe.SampleSize,
                    SuccessCount = probe.SuccessCount,
                    FailureCount = probe.FailureCount,
                    TimeoutCount = probe.TimeoutCount,
                    HttpErrorCount = probe.HttpErrorCount,
                    TransportErrorCount = probe.TransportErrorCount,
                    Summary = Trim(probe.Summary, 220),
                    SortOrder = probe.SortOrder
                });
            }

            foreach (var issue in evaluation.Issues)
            {
                report.Issues.Add(new SourceHealthIssue
                {
                    Severity = issue.Severity,
                    Code = issue.Code,
                    Title = Trim(issue.Title, 120),
                    Message = Trim(issue.Message, 280),
                    AffectedCount = issue.AffectedCount,
                    SampleItems = Trim(issue.SampleItems, 280),
                    SortOrder = issue.SortOrder
                });
            }

            await db.SaveChangesAsync();
        }

        private static async Task<GuideMetrics> BuildGuideMetricsAsync(AppDbContext db, IReadOnlyCollection<int> channelIds)
        {
            if (channelIds.Count == 0)
            {
                return new GuideMetrics();
            }

            var nowUtc = DateTime.UtcNow;
            var query = db.EpgPrograms.AsNoTracking().Where(program => channelIds.Contains(program.ChannelId));

            return new GuideMetrics
            {
                MatchedChannelCount = await query.Select(program => program.ChannelId).Distinct().CountAsync(),
                CurrentCoverageCount = await query
                    .Where(program => program.StartTimeUtc <= nowUtc && program.EndTimeUtc > nowUtc)
                    .Select(program => program.ChannelId)
                    .Distinct()
                    .CountAsync(),
                NextCoverageCount = await query
                    .Where(program => program.StartTimeUtc > nowUtc && program.StartTimeUtc <= nowUtc.AddHours(24))
                    .Select(program => program.ChannelId)
                    .Distinct()
                    .CountAsync()
            };
        }

        private async Task<IReadOnlyList<SourceHealthProbeDraft>> ResolveProbesAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            IReadOnlyList<LiveChannelRecord> liveChannels,
            IReadOnlyList<MovieRecord> movies,
            IReadOnlyList<EpisodeRecord> episodes,
            SourceHealthReport? existingReport,
            bool forceProbe)
        {
            var currentLastSuccessfulSyncAtUtc = profile.LastSync.HasValue ? NormalizeUtc(profile.LastSync.Value) : (DateTime?)null;
            if (!forceProbe && CanReuseProbeEvidence(existingReport, currentLastSuccessfulSyncAtUtc))
            {
                return existingReport!.Probes
                    .OrderBy(item => item.SortOrder)
                    .Select(MapPersistedProbe)
                    .ToList();
            }

            var liveProbeTask = BuildLiveProbeAsync(db, profile, credential, liveChannels);
            var vodProbeTask = BuildVodProbeAsync(db, profile, credential, movies, episodes);
            await Task.WhenAll(liveProbeTask, vodProbeTask);

            return new List<SourceHealthProbeDraft>
            {
                liveProbeTask.Result,
                vodProbeTask.Result
            };
        }

        private static SourceHealthEvaluation Evaluate(
            SourceProfile profile,
            SourceSyncState? syncState,
            SourceCredential? credential,
            EpgSyncLog? epgLog,
            IReadOnlyList<LiveChannelRecord> liveChannels,
            IReadOnlyList<MovieRecord> movies,
            IReadOnlyList<SeriesRecord> series,
            GuideMetrics guideMetrics,
            IReadOnlyList<SourceHealthProbeDraft> probes)
        {
            var evaluatedAtUtc = DateTime.UtcNow;
            var hasSuccessfulSync = profile.LastSync.HasValue;
            var hasLatestImportFailure = syncState is { HttpStatusCode: >= 400 };
            var lastSuccessfulSyncAtUtc = hasSuccessfulSync ? NormalizeUtc(profile.LastSync!.Value) : (DateTime?)null;
            var guideMode = credential?.EpgMode ?? EpgActiveMode.Detected;

            var totalChannels = liveChannels.Count;
            var totalMovies = movies.Count;
            var totalSeries = series.Count;
            var totalVodItems = totalMovies + totalSeries;
            var totalCatalogItems = totalChannels + totalVodItems;
            var totalPlayableItems = totalChannels + totalMovies;

            var liveDuplicates = AnalyzeLiveDuplicates(liveChannels);
            var movieDuplicates = AnalyzeCatalogDuplicates(movies, movie => ChooseCatalogKey(movie.DedupFingerprint, movie.Title, movie.CategoryName), movie => movie.Title);
            var seriesDuplicates = AnalyzeCatalogDuplicates(series, item => ChooseCatalogKey(item.DedupFingerprint, item.Title, item.CategoryName), item => item.Title);

            var duplicateCount = liveDuplicates.Count + movieDuplicates.Count + seriesDuplicates.Count;
            var vodDuplicateCount = movieDuplicates.Count + seriesDuplicates.Count;
            var invalidStreams = AnalyzeInvalidStreams(liveChannels, movies);
            var suspiciousLive = AnalyzeSuspiciousLiveEntries(profile.Type, liveChannels);
            var suspiciousVod = AnalyzeSuspiciousVodEntries(profile.Type, movies, series);
            var suspiciousEntryCount = suspiciousLive.Count + suspiciousVod.Count;
            var logoCount = liveChannels.Count(channel => !string.IsNullOrWhiteSpace(channel.LogoUrl));
            var guideFallbackCount = liveChannels.Count(channel => channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.UserApproved or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy);
            var guideReuseCount = liveChannels.Count(channel => channel.EpgMatchSource == ChannelEpgMatchSource.Previous);
            var logoFallbackCount = liveChannels.Count(channel => channel.LogoSource is ChannelLogoSource.Previous or ChannelLogoSource.Xmltv);
            var providerLogoCount = liveChannels.Count(channel => channel.LogoSource == ChannelLogoSource.Provider);
            var hasGuideConfigured = HasGuideConfigured(profile.Type, credential, epgLog);
            var hasPersistedGuideData = guideMetrics.MatchedChannelCount > 0 || (epgLog?.ProgrammeCount ?? 0) > 0;
            var guideIsStale = epgLog?.Status == EpgStatus.Stale;
            var probeMap = probes.ToDictionary(item => item.ProbeType);

            var components = new List<SourceHealthComponentDraft>
            {
                EvaluateCatalogComponent(hasSuccessfulSync, hasLatestImportFailure, totalCatalogItems, totalPlayableItems, duplicateCount, invalidStreams.TotalCount, suspiciousEntryCount),
                EvaluateLiveComponent(totalChannels, liveDuplicates.Count, invalidStreams.LiveCount, suspiciousLive.Count, GetProbe(probeMap, SourceHealthProbeType.Live)),
                EvaluateVodComponent(profile.Type, credential, totalVodItems, totalMovies, totalSeries, vodDuplicateCount, invalidStreams.VodCount, suspiciousVod.Count, GetProbe(probeMap, SourceHealthProbeType.Vod)),
                EvaluateEpgComponent(profile.Type, totalChannels, hasSuccessfulSync, guideMode, hasGuideConfigured, guideMetrics, epgLog, hasPersistedGuideData, guideFallbackCount, guideReuseCount),
                EvaluateLogosComponent(totalChannels, logoCount, providerLogoCount, logoFallbackCount),
                EvaluateFreshnessComponent(hasSuccessfulSync, hasLatestImportFailure, lastSuccessfulSyncAtUtc, evaluatedAtUtc, guideIsStale)
            };

            var healthState = ComposeOverallHealthState(hasSuccessfulSync, hasLatestImportFailure, components);
            var healthScore = ComposeOverallHealthScore(hasSuccessfulSync, hasLatestImportFailure, healthState, components);
            var issues = BuildIssues(
                profile,
                syncState,
                epgLog,
                hasSuccessfulSync,
                hasLatestImportFailure,
                hasGuideConfigured,
                guideMode,
                totalChannels,
                totalCatalogItems,
                duplicateCount,
                invalidStreams,
                suspiciousEntryCount,
                suspiciousLive,
                suspiciousVod,
                logoCount,
                liveDuplicates,
                movieDuplicates,
                seriesDuplicates,
                guideMetrics,
                components,
                GetProbe(probeMap, SourceHealthProbeType.Live),
                GetProbe(probeMap, SourceHealthProbeType.Vod));

            return new SourceHealthEvaluation
            {
                EvaluatedAtUtc = evaluatedAtUtc,
                LastSyncAttemptAtUtc = syncState?.LastAttempt,
                LastSuccessfulSyncAtUtc = profile.LastSync,
                HealthScore = healthScore,
                HealthState = healthState,
                StatusSummary = BuildStatusSummary(healthState, totalChannels, totalMovies, totalSeries, components),
                ImportResultSummary = BuildImportResultSummary(profile, syncState, totalChannels, totalMovies, totalSeries),
                ValidationSummary = BuildValidationSummary(healthState, healthScore, components),
                TopIssueSummary = BuildTopIssueSummary(issues),
                TotalChannelCount = totalChannels,
                TotalMovieCount = totalMovies,
                TotalSeriesCount = totalSeries,
                DuplicateCount = duplicateCount,
                InvalidStreamCount = invalidStreams.TotalCount,
                ChannelsWithEpgMatchCount = guideMetrics.MatchedChannelCount,
                ChannelsWithCurrentProgramCount = guideMetrics.CurrentCoverageCount,
                ChannelsWithNextProgramCount = guideMetrics.NextCoverageCount,
                ChannelsWithLogoCount = logoCount,
                SuspiciousEntryCount = suspiciousEntryCount,
                WarningCount = issues.Count(item => item.Severity == SourceHealthIssueSeverity.Warning),
                ErrorCount = issues.Count(item => item.Severity == SourceHealthIssueSeverity.Error),
                Probes = probes.OrderBy(item => item.SortOrder).ToList(),
                Components = components.OrderBy(item => item.SortOrder).ToList(),
                Issues = issues.OrderByDescending(item => item.Severity).ThenBy(item => item.SortOrder).ThenByDescending(item => item.AffectedCount).ToList()
            };
        }

        private static SourceHealthComponentDraft EvaluateCatalogComponent(
            bool hasSuccessfulSync,
            bool hasLatestImportFailure,
            int totalCatalogItems,
            int totalPlayableItems,
            int duplicateCount,
            int invalidCount,
            int suspiciousCount)
        {
            if (!hasSuccessfulSync)
            {
                return CreateComponent(
                    SourceHealthComponentType.Catalog,
                    hasLatestImportFailure ? SourceHealthComponentState.Problematic : SourceHealthComponentState.NotSynced,
                    hasLatestImportFailure ? 18 : 0,
                    hasLatestImportFailure
                        ? "No usable catalog is stored yet after the failed sync."
                        : "Catalog health will appear after the first successful sync.",
                    Math.Max(totalCatalogItems, 0),
                    0,
                    Math.Max(totalCatalogItems, 0),
                    0);
            }

            if (totalCatalogItems == 0)
            {
                return CreateComponent(SourceHealthComponentType.Catalog, SourceHealthComponentState.Problematic, 10, "Catalog is empty after the latest sync.", 0, 0, 0, 0);
            }

            var invalidRatio = CalculateRatio(invalidCount, totalPlayableItems);
            var duplicateRatio = CalculateRatio(duplicateCount, totalCatalogItems);
            var suspiciousRatio = CalculateRatio(suspiciousCount, totalCatalogItems);

            var rawScore = 100;
            rawScore -= (int)Math.Round(Math.Min(34d, invalidRatio * 96d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(20d, duplicateRatio * 72d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(20d, suspiciousRatio * 80d), MidpointRounding.AwayFromZero);

            var state = invalidRatio >= 0.18d || suspiciousRatio >= 0.20d
                ? SourceHealthComponentState.Problematic
                : invalidRatio >= 0.08d || suspiciousRatio >= 0.10d
                    ? SourceHealthComponentState.Incomplete
                    : duplicateCount > 0 || invalidCount > 0 || suspiciousCount > 0
                        ? SourceHealthComponentState.Weak
                        : SourceHealthComponentState.Healthy;

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => $"Catalog looks coherent across {totalCatalogItems:N0} items.",
                SourceHealthComponentState.Weak => $"Catalog is usable, with {duplicateCount:N0} duplicates and {suspiciousCount:N0} suspicious entries.",
                SourceHealthComponentState.Incomplete => $"Catalog imported, but malformed or suspicious entries are too common across {totalCatalogItems:N0} items.",
                _ => $"{invalidCount:N0} invalid playable items and {suspiciousCount:N0} suspicious entries make the catalog unreliable."
            };

            return CreateComponent(
                SourceHealthComponentType.Catalog,
                state,
                AlignComponentScore(state, rawScore),
                summary,
                totalCatalogItems,
                Math.Max(0, totalCatalogItems - Math.Min(totalCatalogItems, invalidCount + suspiciousCount)),
                duplicateCount + invalidCount + suspiciousCount,
                0);
        }

        private static SourceHealthComponentDraft EvaluateLiveComponent(int totalChannels, int duplicateCount, int invalidCount, int suspiciousCount, SourceHealthProbeDraft? probe)
        {
            if (totalChannels <= 0)
            {
                return CreateComponent(SourceHealthComponentType.Live, SourceHealthComponentState.NotApplicable, 0, "No live catalog is present for this source.", 0, 0, 0, 1);
            }

            var invalidRatio = CalculateRatio(invalidCount, totalChannels);
            var duplicateRatio = CalculateRatio(duplicateCount, totalChannels);
            var suspiciousRatio = CalculateRatio(suspiciousCount, totalChannels);

            var rawScore = 100;
            rawScore -= (int)Math.Round(Math.Min(38d, invalidRatio * 100d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(18d, duplicateRatio * 70d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(22d, suspiciousRatio * 84d), MidpointRounding.AwayFromZero);

            var state = invalidRatio >= 0.20d || suspiciousRatio >= 0.18d
                ? SourceHealthComponentState.Problematic
                : invalidRatio >= 0.08d || suspiciousRatio >= 0.10d
                    ? SourceHealthComponentState.Incomplete
                    : duplicateCount > 0 || invalidCount > 0 || suspiciousCount > 0
                        ? SourceHealthComponentState.Weak
                        : SourceHealthComponentState.Healthy;

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => $"Live catalog is present across {totalChannels:N0} channels.",
                SourceHealthComponentState.Weak => $"Live catalog is present, but {duplicateCount + suspiciousCount + invalidCount:N0} channels still look noisy or duplicated.",
                SourceHealthComponentState.Incomplete => $"Live catalog imported, but stream or title quality is uneven across {totalChannels:N0} channels.",
                _ => $"Too many live channels are unusable or suspicious inside the {totalChannels:N0}-channel catalog."
            };

            var component = CreateComponent(
                SourceHealthComponentType.Live,
                state,
                AlignComponentScore(state, rawScore),
                summary,
                totalChannels,
                Math.Max(0, totalChannels - Math.Min(totalChannels, invalidCount + suspiciousCount)),
                duplicateCount + invalidCount + suspiciousCount,
                1);

            return ApplyProbeEvidence(component, probe);
        }

        private static SourceHealthComponentDraft EvaluateVodComponent(
            SourceType sourceType,
            SourceCredential? credential,
            int totalVodItems,
            int totalMovies,
            int totalSeries,
            int duplicateCount,
            int invalidCount,
            int suspiciousCount,
            SourceHealthProbeDraft? probe)
        {
            if (totalVodItems <= 0)
            {
                var notApplicableSummary = sourceType == SourceType.M3U && credential?.M3uImportMode == M3uImportMode.LiveOnly
                    ? "VOD import is intentionally disabled for this playlist."
                    : "No VOD catalog is present for this source.";

                return CreateComponent(SourceHealthComponentType.Vod, SourceHealthComponentState.NotApplicable, 0, notApplicableSummary, 0, 0, 0, 2);
            }

            var invalidRatio = CalculateRatio(invalidCount, totalMovies);
            var duplicateRatio = CalculateRatio(duplicateCount, totalVodItems);
            var suspiciousRatio = CalculateRatio(suspiciousCount, totalVodItems);

            var rawScore = 100;
            rawScore -= (int)Math.Round(Math.Min(28d, invalidRatio * 90d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(18d, duplicateRatio * 60d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(22d, suspiciousRatio * 75d), MidpointRounding.AwayFromZero);

            var state = invalidRatio >= 0.30d || suspiciousRatio >= 0.22d
                ? SourceHealthComponentState.Problematic
                : invalidRatio >= 0.10d || suspiciousRatio >= 0.10d
                    ? SourceHealthComponentState.Incomplete
                    : duplicateCount > 0 || invalidCount > 0 || suspiciousCount > 0
                        ? SourceHealthComponentState.Weak
                        : SourceHealthComponentState.Healthy;

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => $"VOD catalog is present across {totalMovies:N0} movies and {totalSeries:N0} series.",
                SourceHealthComponentState.Weak => $"VOD catalog is usable, but duplicate or suspicious entries remain across {totalVodItems:N0} items.",
                SourceHealthComponentState.Incomplete => $"VOD catalog imported, but completeness is uneven across {totalVodItems:N0} items.",
                _ => $"VOD data is unreliable across {totalVodItems:N0} items and needs review."
            };

            var component = CreateComponent(
                SourceHealthComponentType.Vod,
                state,
                AlignComponentScore(state, rawScore),
                summary,
                totalVodItems,
                Math.Max(0, totalVodItems - Math.Min(totalVodItems, invalidCount + suspiciousCount)),
                duplicateCount + invalidCount + suspiciousCount,
                2);

            return ApplyProbeEvidence(component, probe);
        }

        private static SourceHealthComponentDraft EvaluateEpgComponent(
            SourceType sourceType,
            int totalChannels,
            bool hasSuccessfulSync,
            EpgActiveMode guideMode,
            bool hasGuideConfigured,
            GuideMetrics guideMetrics,
            EpgSyncLog? epgLog,
            bool hasPersistedGuideData,
            int guideFallbackCount,
            int guideReuseCount)
        {
            if (totalChannels <= 0)
            {
                return CreateComponent(SourceHealthComponentType.Epg, SourceHealthComponentState.NotApplicable, 0, "Guide health only applies when live channels exist.", 0, 0, 0, 3);
            }

            if (guideMode == EpgActiveMode.None)
            {
                return CreateComponent(SourceHealthComponentType.Epg, SourceHealthComponentState.NotApplicable, 0, "Guide is disabled for this source.", totalChannels, 0, 0, 3);
            }

            if (!hasSuccessfulSync && !hasPersistedGuideData)
            {
                return CreateComponent(SourceHealthComponentType.Epg, SourceHealthComponentState.NotSynced, 0, "Guide validation will appear after the first successful live sync.", totalChannels, 0, totalChannels, 3);
            }

            var matchedRatio = CalculateRatio(guideMetrics.MatchedChannelCount, totalChannels);
            var currentRatio = CalculateRatio(guideMetrics.CurrentCoverageCount, totalChannels);
            var nextRatio = CalculateRatio(guideMetrics.NextCoverageCount, totalChannels);
            var status = epgLog?.Status ?? EpgStatus.Unknown;

            if (sourceType == SourceType.Stalker && !hasGuideConfigured && !hasPersistedGuideData)
            {
                return CreateComponent(
                    SourceHealthComponentType.Epg,
                    SourceHealthComponentState.NotApplicable,
                    0,
                    "Guide is optional for this Stalker source until you add a manual XMLTV feed.",
                    totalChannels,
                    0,
                    0,
                    3);
            }

            var rawScore = 100;
            if (!hasGuideConfigured)
            {
                rawScore -= 35;
            }

            rawScore -= (int)Math.Round(Math.Min(38d, Math.Max(0d, 0.95d - matchedRatio) * 40d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(12d, Math.Max(0d, 0.50d - currentRatio) * 24d), MidpointRounding.AwayFromZero);
            rawScore -= (int)Math.Round(Math.Min(10d, Math.Max(0d, 0.50d - nextRatio) * 20d), MidpointRounding.AwayFromZero);

            SourceHealthComponentState state;
            if (status == EpgStatus.FailedFetchOrParse && !hasPersistedGuideData)
            {
                state = SourceHealthComponentState.Problematic;
            }
            else if (status == EpgStatus.Stale)
            {
                state = SourceHealthComponentState.Outdated;
            }
            else if (!hasGuideConfigured || guideMetrics.MatchedChannelCount == 0 || matchedRatio < 0.55d ||
                     (guideMetrics.MatchedChannelCount > 0 && guideMetrics.CurrentCoverageCount == 0 && guideMetrics.NextCoverageCount == 0))
            {
                state = SourceHealthComponentState.Incomplete;
            }
            else if (matchedRatio < 0.85d || currentRatio < 0.45d || nextRatio < 0.45d)
            {
                state = SourceHealthComponentState.Weak;
            }
            else
            {
                state = SourceHealthComponentState.Healthy;
            }

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => BuildGuideSummary($"Guide matches {guideMetrics.MatchedChannelCount:N0} of {totalChannels:N0} live channels.", guideFallbackCount, guideReuseCount),
                SourceHealthComponentState.Weak => BuildGuideSummary($"Guide covers {guideMetrics.MatchedChannelCount:N0} of {totalChannels:N0} live channels, but coverage is still uneven.", guideFallbackCount, guideReuseCount),
                SourceHealthComponentState.Incomplete when !hasGuideConfigured => "No active guide source is available for live-channel matching.",
                SourceHealthComponentState.Incomplete => BuildGuideSummary($"Guide coverage is too thin across the {totalChannels:N0} live channels.", guideFallbackCount, guideReuseCount),
                SourceHealthComponentState.Outdated => "Guide data is stale after a failed refresh.",
                _ => "Guide refresh failed before any usable coverage was stored."
            };

            return CreateComponent(
                SourceHealthComponentType.Epg,
                state,
                AlignComponentScore(state, rawScore),
                summary,
                totalChannels,
                guideMetrics.MatchedChannelCount,
                Math.Max(0, totalChannels - guideMetrics.MatchedChannelCount),
                3);
        }

        private static SourceHealthComponentDraft EvaluateLogosComponent(int totalChannels, int logoCount, int providerLogoCount, int logoFallbackCount)
        {
            if (totalChannels <= 0)
            {
                return CreateComponent(SourceHealthComponentType.Logos, SourceHealthComponentState.NotApplicable, 0, "Logo coverage only applies when live channels exist.", 0, 0, 0, 4);
            }

            var logoRatio = CalculateRatio(logoCount, totalChannels);
            var rawScore = 100;
            rawScore -= (int)Math.Round(Math.Min(28d, Math.Max(0d, 0.90d - logoRatio) * 42d), MidpointRounding.AwayFromZero);

            var state = logoRatio >= 0.85d
                ? SourceHealthComponentState.Healthy
                : logoRatio >= 0.55d
                    ? SourceHealthComponentState.Weak
                    : SourceHealthComponentState.Incomplete;

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => BuildLogoSummary($"Logos cover {logoCount:N0} of {totalChannels:N0} live channels.", providerLogoCount, logoFallbackCount),
                SourceHealthComponentState.Weak => BuildLogoSummary($"Logos cover {logoCount:N0} of {totalChannels:N0} live channels, but branding is still thin.", providerLogoCount, logoFallbackCount),
                _ => BuildLogoSummary($"Many live channels are still missing logos ({logoCount:N0}/{totalChannels:N0}).", providerLogoCount, logoFallbackCount)
            };

            return CreateComponent(SourceHealthComponentType.Logos, state, AlignComponentScore(state, rawScore), summary, totalChannels, logoCount, Math.Max(0, totalChannels - logoCount), 4);
        }

        private static SourceHealthComponentDraft EvaluateFreshnessComponent(
            bool hasSuccessfulSync,
            bool hasLatestImportFailure,
            DateTime? lastSuccessfulSyncAtUtc,
            DateTime evaluatedAtUtc,
            bool guideIsStale)
        {
            if (!hasSuccessfulSync || !lastSuccessfulSyncAtUtc.HasValue)
            {
                return CreateComponent(
                    SourceHealthComponentType.Freshness,
                    hasLatestImportFailure ? SourceHealthComponentState.Problematic : SourceHealthComponentState.NotSynced,
                    hasLatestImportFailure ? 14 : 0,
                    hasLatestImportFailure ? "No successful sync is stored yet after the latest failure." : "Freshness will appear after the first successful sync.",
                    1,
                    0,
                    1,
                    5);
            }

            var age = evaluatedAtUtc - lastSuccessfulSyncAtUtc.Value;
            var rawScore = 100;
            if (age > TimeSpan.FromDays(3)) rawScore -= 14;
            if (age > TimeSpan.FromDays(7)) rawScore -= 24;
            if (age > TimeSpan.FromDays(14)) rawScore -= 28;
            if (guideIsStale) rawScore -= 18;
            if (hasLatestImportFailure) rawScore -= 8;

            var state = guideIsStale || age > TimeSpan.FromDays(7)
                ? SourceHealthComponentState.Outdated
                : hasLatestImportFailure || age > TimeSpan.FromDays(3)
                    ? SourceHealthComponentState.Weak
                    : SourceHealthComponentState.Healthy;

            var summary = state switch
            {
                SourceHealthComponentState.Healthy => $"Last successful sync was {FormatTimestamp(lastSuccessfulSyncAtUtc)}.",
                SourceHealthComponentState.Weak when hasLatestImportFailure => $"Latest sync attempt failed. Using data from {FormatTimestamp(lastSuccessfulSyncAtUtc)}.",
                SourceHealthComponentState.Weak => $"Last successful sync was {FormatTimestamp(lastSuccessfulSyncAtUtc)}.",
                _ => guideIsStale ? "Stored source data is aging and the latest guide refresh failed." : $"Last successful sync was {FormatTimestamp(lastSuccessfulSyncAtUtc)}."
            };

            return CreateComponent(
                SourceHealthComponentType.Freshness,
                state,
                AlignComponentScore(state, rawScore),
                summary,
                1,
                state == SourceHealthComponentState.Healthy ? 1 : 0,
                state == SourceHealthComponentState.Healthy ? 0 : 1,
                5);
        }

        private static SourceHealthState ComposeOverallHealthState(bool hasSuccessfulSync, bool hasLatestImportFailure, IReadOnlyList<SourceHealthComponentDraft> components)
        {
            if (!hasSuccessfulSync)
            {
                return hasLatestImportFailure ? SourceHealthState.Problematic : SourceHealthState.NotSynced;
            }

            var componentMap = components.ToDictionary(item => item.ComponentType);
            var catalogState = GetComponentState(componentMap, SourceHealthComponentType.Catalog);
            var liveState = GetComponentState(componentMap, SourceHealthComponentType.Live);
            var vodState = GetComponentState(componentMap, SourceHealthComponentType.Vod);
            var epgState = GetComponentState(componentMap, SourceHealthComponentType.Epg);
            var logosState = GetComponentState(componentMap, SourceHealthComponentType.Logos);
            var freshnessState = GetComponentState(componentMap, SourceHealthComponentType.Freshness);

            if (catalogState == SourceHealthComponentState.Problematic ||
                liveState == SourceHealthComponentState.Problematic ||
                vodState == SourceHealthComponentState.Problematic ||
                freshnessState == SourceHealthComponentState.Problematic)
            {
                return SourceHealthState.Problematic;
            }

            if (catalogState == SourceHealthComponentState.Incomplete ||
                liveState == SourceHealthComponentState.Incomplete ||
                vodState == SourceHealthComponentState.Incomplete ||
                epgState == SourceHealthComponentState.Incomplete ||
                epgState == SourceHealthComponentState.Problematic)
            {
                return SourceHealthState.Incomplete;
            }

            if (freshnessState == SourceHealthComponentState.Outdated || epgState == SourceHealthComponentState.Outdated)
            {
                return SourceHealthState.Outdated;
            }

            if (logosState == SourceHealthComponentState.Incomplete)
            {
                return SourceHealthState.Weak;
            }

            var weakComponents = components
                .Where(item => item.State == SourceHealthComponentState.Weak)
                .ToList();
            if (weakComponents.Count > 0)
            {
                return hasLatestImportFailure ||
                       weakComponents.Any(item => item.ComponentType is SourceHealthComponentType.Catalog or SourceHealthComponentType.Live or SourceHealthComponentType.Vod)
                    ? SourceHealthState.Weak
                    : SourceHealthState.Good;
            }

            if (hasLatestImportFailure)
            {
                return SourceHealthState.Weak;
            }

            return SourceHealthState.Healthy;
        }

        private static int ComposeOverallHealthScore(bool hasSuccessfulSync, bool hasLatestImportFailure, SourceHealthState healthState, IReadOnlyList<SourceHealthComponentDraft> components)
        {
            if (!hasSuccessfulSync)
            {
                return hasLatestImportFailure ? 18 : 0;
            }

            var scoredComponents = components.Where(item => item.State is not SourceHealthComponentState.NotApplicable and not SourceHealthComponentState.NotSynced).ToList();
            if (scoredComponents.Count == 0)
            {
                return 0;
            }

            var weightedTotal = 0d;
            var weightTotal = 0;
            foreach (var component in scoredComponents)
            {
                var weight = ComponentWeights.TryGetValue(component.ComponentType, out var resolvedWeight) ? resolvedWeight : 10;
                weightedTotal += component.Score * weight;
                weightTotal += weight;
            }

            var score = weightTotal > 0 ? (int)Math.Round(weightedTotal / weightTotal, MidpointRounding.AwayFromZero) : 0;
            if (hasLatestImportFailure) score -= 5;
            return AlignOverallScore(healthState, score);
        }

        private static List<SourceHealthIssueDraft> BuildIssues(
            SourceProfile profile,
            SourceSyncState? syncState,
            EpgSyncLog? epgLog,
            bool hasSuccessfulSync,
            bool hasLatestImportFailure,
            bool hasGuideConfigured,
            EpgActiveMode guideMode,
            int totalChannels,
            int totalCatalogItems,
            int duplicateCount,
            InvalidStreamAnalysis invalidStreams,
            int suspiciousCount,
            SuspiciousAnalysis suspiciousLive,
            SuspiciousAnalysis suspiciousVod,
            int logoCount,
            DuplicateAnalysis liveDuplicates,
            DuplicateAnalysis movieDuplicates,
            DuplicateAnalysis seriesDuplicates,
            GuideMetrics guideMetrics,
            IReadOnlyList<SourceHealthComponentDraft> components,
            SourceHealthProbeDraft? liveProbe,
            SourceHealthProbeDraft? vodProbe)
        {
            var issues = new List<SourceHealthIssueDraft>();
            var componentMap = components.ToDictionary(item => item.ComponentType);
            var catalogState = GetComponentState(componentMap, SourceHealthComponentType.Catalog);
            var liveState = GetComponentState(componentMap, SourceHealthComponentType.Live);
            var vodState = GetComponentState(componentMap, SourceHealthComponentType.Vod);
            var epgState = GetComponentState(componentMap, SourceHealthComponentType.Epg);
            var logosState = GetComponentState(componentMap, SourceHealthComponentType.Logos);
            var freshnessState = GetComponentState(componentMap, SourceHealthComponentType.Freshness);
            var matchedRatio = CalculateRatio(guideMetrics.MatchedChannelCount, totalChannels);
            var logoRatio = CalculateRatio(logoCount, totalChannels);

            if (hasLatestImportFailure && syncState != null)
            {
                issues.Add(new SourceHealthIssueDraft(
                    hasSuccessfulSync ? SourceHealthIssueSeverity.Warning : SourceHealthIssueSeverity.Error,
                    "import_failed",
                    hasSuccessfulSync ? "Latest sync failed" : "Source has not synced successfully",
                    hasSuccessfulSync
                        ? $"The latest sync failed, but older catalog data is still being used. {BuildImportFailureMessage(syncState)}"
                        : BuildImportFailureMessage(syncState),
                    1,
                    Trim(syncState.ErrorLog, 180),
                    0));
            }

            if (!hasSuccessfulSync)
            {
                issues.Add(new SourceHealthIssueDraft(
                    hasLatestImportFailure ? SourceHealthIssueSeverity.Error : SourceHealthIssueSeverity.Warning,
                    "never_synced",
                    "No successful sync yet",
                    hasLatestImportFailure
                        ? "The source has not completed a successful import yet."
                        : "The source is saved, but no successful import has been recorded yet.",
                    1,
                    string.Empty,
                    1));
            }

            if (hasSuccessfulSync && totalCatalogItems == 0)
            {
                issues.Add(new SourceHealthIssueDraft(SourceHealthIssueSeverity.Error, "empty_catalog", "No catalog items imported", "The latest import did not leave any channels, movies, or series in the catalog.", 0, string.Empty, 2));
            }

            if (invalidStreams.TotalCount > 0)
            {
                issues.Add(new SourceHealthIssueDraft(
                    catalogState == SourceHealthComponentState.Problematic ? SourceHealthIssueSeverity.Error : SourceHealthIssueSeverity.Warning,
                    "invalid_streams",
                    "Invalid stream URLs detected",
                    $"{invalidStreams.TotalCount} playable entries have empty or unsupported stream URLs.",
                    invalidStreams.TotalCount,
                    JoinSamples(invalidStreams.Samples),
                    3));
            }

            if (duplicateCount > 0)
            {
                var duplicateSamples = liveDuplicates.Samples.Concat(movieDuplicates.Samples).Concat(seriesDuplicates.Samples).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
                issues.Add(new SourceHealthIssueDraft(
                    catalogState == SourceHealthComponentState.Incomplete ? SourceHealthIssueSeverity.Warning : SourceHealthIssueSeverity.Info,
                    "duplicates",
                    "Duplicate items found",
                    $"{duplicateCount} entries look duplicated inside this source.",
                    duplicateCount,
                    JoinSamples(duplicateSamples),
                    4));
            }

            if (suspiciousCount > 0)
            {
                issues.Add(new SourceHealthIssueDraft(
                    catalogState is SourceHealthComponentState.Incomplete or SourceHealthComponentState.Problematic ? SourceHealthIssueSeverity.Warning : SourceHealthIssueSeverity.Info,
                    "suspicious_entries",
                    "Suspicious entries detected",
                    $"{suspiciousCount} titles or categories look like placeholders, promo rows, or other low-confidence content.",
                    suspiciousCount,
                    JoinSamples(suspiciousLive.Samples.Concat(suspiciousVod.Samples)),
                    5));
            }

            if (totalChannels > 0 && guideMode != EpgActiveMode.None)
            {
                if (!hasGuideConfigured && profile.Type != SourceType.Stalker)
                {
                    issues.Add(new SourceHealthIssueDraft(SourceHealthIssueSeverity.Warning, "guide_missing", "Guide source is missing", "Live channels are present, but no active XMLTV source is available for guide matching.", totalChannels, string.Empty, 6));
                }
                else if (epgState == SourceHealthComponentState.Problematic)
                {
                    issues.Add(new SourceHealthIssueDraft(SourceHealthIssueSeverity.Error, "guide_failed", "Guide refresh failed", "Guide validation failed before any usable live-channel coverage was stored.", totalChannels, Trim(epgLog?.FailureReason ?? string.Empty, 180), 6));
                }
                else if (guideMetrics.MatchedChannelCount == 0)
                {
                    issues.Add(new SourceHealthIssueDraft(SourceHealthIssueSeverity.Warning, "guide_zero_coverage", "Guide coverage is empty", "Live channels imported successfully, but none of them match persisted guide data.", totalChannels, string.Empty, 6));
                }
                else if (guideMetrics.MatchedChannelCount < totalChannels)
                {
                    issues.Add(new SourceHealthIssueDraft(
                        matchedRatio < 0.55d ? SourceHealthIssueSeverity.Warning : SourceHealthIssueSeverity.Info,
                        "guide_partial_coverage",
                        "Guide coverage is partial",
                        $"{guideMetrics.MatchedChannelCount} of {totalChannels} live channels match guide data.",
                        totalChannels - guideMetrics.MatchedChannelCount,
                        string.Empty,
                        6));
                }
            }

            if (totalChannels > 0 && logosState is SourceHealthComponentState.Weak or SourceHealthComponentState.Incomplete)
            {
                issues.Add(new SourceHealthIssueDraft(
                    logoRatio < 0.35d ? SourceHealthIssueSeverity.Warning : SourceHealthIssueSeverity.Info,
                    "missing_logos",
                    "Channel branding is thin",
                    $"{logoCount} of {totalChannels} live channels currently have logos.",
                    totalChannels - logoCount,
                    string.Empty,
                    7));
            }

            if (freshnessState == SourceHealthComponentState.Outdated)
            {
                issues.Add(new SourceHealthIssueDraft(
                    SourceHealthIssueSeverity.Warning,
                    "outdated_data",
                    "Source data is outdated",
                    epgLog?.Status == EpgStatus.Stale ? "The guide is marked stale after a failed refresh." : $"The last successful source sync was {FormatTimestamp(profile.LastSync)}.",
                    1,
                    string.Empty,
                    8));
            }

            AddProbeIssue(issues, liveProbe, liveState, "live_probe", "Live probe found weak reachability", "Live probe could not reach all sampled live streams.", 9);
            AddProbeIssue(issues, vodProbe, vodState, "vod_probe", "VOD probe found weak reachability", "VOD probe could not reach all sampled VOD streams.", 10);

            return issues;
        }

        private static bool HasGuideConfigured(SourceType sourceType, SourceCredential? credential, EpgSyncLog? epgLog)
        {
            if (credential == null)
            {
                return !string.IsNullOrWhiteSpace(epgLog?.ActiveXmltvUrl);
            }

            if (credential.EpgMode == EpgActiveMode.None)
            {
                return false;
            }

            if (credential.EpgMode == EpgActiveMode.Manual)
            {
                return !string.IsNullOrWhiteSpace(credential.ManualEpgUrl);
            }

            return !string.IsNullOrWhiteSpace(epgLog?.ActiveXmltvUrl) ||
                   !string.IsNullOrWhiteSpace(credential.DetectedEpgUrl) ||
                   (sourceType == SourceType.Stalker && !string.IsNullOrWhiteSpace(credential.ManualEpgUrl));
        }

        private async Task<SourceHealthProbeDraft> BuildLiveProbeAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            IReadOnlyList<LiveChannelRecord> liveChannels)
        {
            if (liveChannels.Count == 0)
            {
                return CreateSkippedProbe(SourceHealthProbeType.Live, "Live probing is not applicable because the source has no live catalog.", 0);
            }

            var resolvedCandidates = await ResolveProbeCandidatesAsync(
                db,
                profile.Id,
                liveChannels.Select(channel => new SourceProbeCandidate
                {
                    Name = channel.Name,
                    StreamUrl = channel.StreamUrl
                }).ToList());
            var result = await _sourceProbeService.ProbeAsync(
                SourceHealthProbeType.Live,
                resolvedCandidates,
                _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Probe));

            return CreateProbeDraft(SourceHealthProbeType.Live, result, 0);
        }

        private async Task<SourceHealthProbeDraft> BuildVodProbeAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            IReadOnlyList<MovieRecord> movies,
            IReadOnlyList<EpisodeRecord> episodes)
        {
            if (profile.Type == SourceType.M3U && credential?.M3uImportMode == M3uImportMode.LiveOnly)
            {
                return CreateSkippedProbe(SourceHealthProbeType.Vod, "VOD probing is not applicable because VOD import is disabled for this playlist.", 1);
            }

            var candidates = new List<SourceProbeCandidate>(movies.Count + episodes.Count);
            candidates.AddRange(movies.Select(movie => new SourceProbeCandidate
            {
                Name = movie.Title,
                StreamUrl = movie.StreamUrl
            }));
            candidates.AddRange(episodes.Select(episode => new SourceProbeCandidate
            {
                Name = $"{episode.SeriesTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}",
                StreamUrl = episode.StreamUrl
            }));

            if (candidates.Count == 0)
            {
                return CreateSkippedProbe(SourceHealthProbeType.Vod, "VOD probing is not applicable because the source has no playable VOD sample.", 1);
            }

            var resolvedCandidates = await ResolveProbeCandidatesAsync(db, profile.Id, candidates);
            var result = await _sourceProbeService.ProbeAsync(
                SourceHealthProbeType.Vod,
                resolvedCandidates,
                _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Probe));
            return CreateProbeDraft(SourceHealthProbeType.Vod, result, 1);
        }

        private async Task<List<SourceProbeCandidate>> ResolveProbeCandidatesAsync(
            AppDbContext db,
            int sourceProfileId,
            IReadOnlyList<SourceProbeCandidate> candidates)
        {
            var resolved = new List<SourceProbeCandidate>(Math.Min(candidates.Count, 12));
            foreach (var candidate in candidates
                         .Where(item => !string.IsNullOrWhiteSpace(item.StreamUrl))
                         .GroupBy(item => item.StreamUrl.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First())
                         .Take(12))
            {
                var resolution = await _providerStreamResolverService.ResolveAsync(
                    db,
                    sourceProfileId,
                    candidate.StreamUrl,
                    SourceNetworkPurpose.Probe);
                if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.StreamUrl))
                {
                    continue;
                }

                resolved.Add(new SourceProbeCandidate
                {
                    Name = candidate.Name,
                    StreamUrl = resolution.StreamUrl,
                    Routing = resolution.EffectiveRouting,
                    RouteSummary = string.IsNullOrWhiteSpace(resolution.RoutingSummary)
                        ? resolution.EffectiveRouting.Summary
                        : resolution.RoutingSummary
                });
            }

            return resolved.Count == 0 ? candidates.ToList() : resolved;
        }

        private static bool CanReuseProbeEvidence(SourceHealthReport? report, DateTime? currentLastSuccessfulSyncAtUtc)
        {
            if (report == null || !currentLastSuccessfulSyncAtUtc.HasValue || !report.LastSuccessfulSyncAtUtc.HasValue)
            {
                return false;
            }

            if (NormalizeUtc(report.LastSuccessfulSyncAtUtc.Value) != currentLastSuccessfulSyncAtUtc.Value)
            {
                return false;
            }

            return report.Probes.Any(probe => probe.ProbeType == SourceHealthProbeType.Live) &&
                   report.Probes.Any(probe => probe.ProbeType == SourceHealthProbeType.Vod);
        }

        private static SourceHealthProbeDraft MapPersistedProbe(SourceHealthProbe probe)
        {
            return new SourceHealthProbeDraft
            {
                ProbeType = probe.ProbeType,
                Status = probe.Status,
                ProbedAtUtc = probe.ProbedAtUtc,
                CandidateCount = probe.CandidateCount,
                SampleSize = probe.SampleSize,
                SuccessCount = probe.SuccessCount,
                FailureCount = probe.FailureCount,
                TimeoutCount = probe.TimeoutCount,
                HttpErrorCount = probe.HttpErrorCount,
                TransportErrorCount = probe.TransportErrorCount,
                Summary = probe.Summary,
                SortOrder = probe.SortOrder
            };
        }

        private static SourceHealthProbeDraft CreateSkippedProbe(SourceHealthProbeType probeType, string summary, int sortOrder)
        {
            return new SourceHealthProbeDraft
            {
                ProbeType = probeType,
                Status = SourceHealthProbeStatus.Skipped,
                Summary = summary,
                SortOrder = sortOrder
            };
        }

        private static SourceHealthProbeDraft CreateProbeDraft(SourceHealthProbeType probeType, SourceProbeRunResult result, int sortOrder)
        {
            return new SourceHealthProbeDraft
            {
                ProbeType = probeType,
                Status = result.Status,
                ProbedAtUtc = result.ProbedAtUtc,
                CandidateCount = result.CandidateCount,
                SampleSize = result.SampleSize,
                SuccessCount = result.SuccessCount,
                FailureCount = result.FailureCount,
                TimeoutCount = result.TimeoutCount,
                HttpErrorCount = result.HttpErrorCount,
                TransportErrorCount = result.TransportErrorCount,
                Summary = result.Summary,
                SortOrder = sortOrder
            };
        }

        private static SourceHealthProbeDraft? GetProbe(IReadOnlyDictionary<SourceHealthProbeType, SourceHealthProbeDraft> probes, SourceHealthProbeType probeType)
        {
            return probes.TryGetValue(probeType, out var probe) ? probe : null;
        }

        private static SourceHealthComponentDraft ApplyProbeEvidence(SourceHealthComponentDraft component, SourceHealthProbeDraft? probe)
        {
            if (probe == null || component.State is SourceHealthComponentState.NotApplicable or SourceHealthComponentState.NotSynced)
            {
                return component;
            }

            component.Summary = CombineSummaries(component.Summary, probe.Summary);
            if (probe.Status != SourceHealthProbeStatus.Completed || probe.SampleSize <= 0)
            {
                return component;
            }

            var successRatio = CalculateRatio(probe.SuccessCount, probe.SampleSize);
            if (probe.SuccessCount == probe.SampleSize)
            {
                component.Score = Math.Max(component.Score, component.State == SourceHealthComponentState.Healthy ? 88 : component.Score);
                return component;
            }

            if (successRatio >= 0.75d)
            {
                component.Score = Math.Min(component.Score, component.State == SourceHealthComponentState.Healthy ? 91 : component.Score);
                return component;
            }

            if (successRatio >= 0.50d)
            {
                component.State = MaxComponentState(component.State, SourceHealthComponentState.Weak);
                component.Score = AlignComponentScore(component.State, Math.Min(component.Score, 78));
                component.IssueCount = Math.Max(component.IssueCount, probe.FailureCount);
                return component;
            }

            if (probe.SuccessCount > 0)
            {
                component.State = MaxComponentState(component.State, SourceHealthComponentState.Incomplete);
                component.Score = AlignComponentScore(component.State, Math.Min(component.Score, 58));
                component.IssueCount = Math.Max(component.IssueCount, probe.FailureCount);
                return component;
            }

            component.State = component.State is SourceHealthComponentState.Incomplete or SourceHealthComponentState.Problematic
                ? SourceHealthComponentState.Problematic
                : SourceHealthComponentState.Incomplete;
            component.Score = AlignComponentScore(component.State, component.State == SourceHealthComponentState.Problematic ? 24 : 46);
            component.IssueCount = Math.Max(component.IssueCount, probe.SampleSize);
            return component;
        }

        private static SourceHealthComponentState MaxComponentState(SourceHealthComponentState current, SourceHealthComponentState minimum)
        {
            if (current == SourceHealthComponentState.Problematic || minimum == SourceHealthComponentState.Problematic)
            {
                return SourceHealthComponentState.Problematic;
            }

            if (current == SourceHealthComponentState.Incomplete || minimum == SourceHealthComponentState.Incomplete)
            {
                return SourceHealthComponentState.Incomplete;
            }

            if (current == SourceHealthComponentState.Outdated)
            {
                return current;
            }

            return current == SourceHealthComponentState.Weak || minimum == SourceHealthComponentState.Weak
                ? SourceHealthComponentState.Weak
                : SourceHealthComponentState.Healthy;
        }

        private static string CombineSummaries(string structuralSummary, string probeSummary)
        {
            if (string.IsNullOrWhiteSpace(probeSummary))
            {
                return structuralSummary;
            }

            return string.IsNullOrWhiteSpace(structuralSummary)
                ? probeSummary
                : $"{structuralSummary} {probeSummary}";
        }

        private static void AddProbeIssue(
            ICollection<SourceHealthIssueDraft> issues,
            SourceHealthProbeDraft? probe,
            SourceHealthComponentState componentState,
            string code,
            string title,
            string fallbackMessage,
            int sortOrder)
        {
            if (probe == null || probe.Status != SourceHealthProbeStatus.Completed || probe.SampleSize <= 0 || probe.FailureCount <= 0)
            {
                return;
            }

            var successRatio = CalculateRatio(probe.SuccessCount, probe.SampleSize);
            if (successRatio >= 0.75d)
            {
                return;
            }

            var severity = componentState == SourceHealthComponentState.Problematic
                ? SourceHealthIssueSeverity.Error
                : SourceHealthIssueSeverity.Warning;
            var message = string.IsNullOrWhiteSpace(probe.Summary) ? fallbackMessage : probe.Summary;
            issues.Add(new SourceHealthIssueDraft(
                severity,
                code,
                title,
                message,
                probe.FailureCount,
                string.Empty,
                sortOrder));
        }

        private static SourceHealthComponentDraft CreateComponent(SourceHealthComponentType componentType, SourceHealthComponentState state, int score, string summary, int relevantCount, int healthyCount, int issueCount, int sortOrder)
        {
            return new SourceHealthComponentDraft
            {
                ComponentType = componentType,
                State = state,
                Score = score,
                Summary = summary,
                RelevantCount = Math.Max(0, relevantCount),
                HealthyCount = Math.Max(0, healthyCount),
                IssueCount = Math.Max(0, issueCount),
                SortOrder = sortOrder
            };
        }

        private static SourceHealthComponentState GetComponentState(IReadOnlyDictionary<SourceHealthComponentType, SourceHealthComponentDraft> components, SourceHealthComponentType componentType)
        {
            return components.TryGetValue(componentType, out var component) ? component.State : SourceHealthComponentState.NotApplicable;
        }

        private static int AlignComponentScore(SourceHealthComponentState state, int score) => state switch
        {
            SourceHealthComponentState.Healthy => Math.Clamp(score, 85, 100),
            SourceHealthComponentState.Weak => Math.Clamp(score, 65, 84),
            SourceHealthComponentState.Incomplete => Math.Clamp(score, 40, 64),
            SourceHealthComponentState.Outdated => Math.Clamp(score, 50, 74),
            SourceHealthComponentState.Problematic => Math.Clamp(score, 0, 39),
            _ => 0
        };

        private static int AlignOverallScore(SourceHealthState state, int score) => state switch
        {
            SourceHealthState.Healthy => Math.Clamp(score, 85, 100),
            SourceHealthState.Good => Math.Clamp(score, 75, 89),
            SourceHealthState.Weak => Math.Clamp(score, 65, 84),
            SourceHealthState.Incomplete => Math.Clamp(score, 40, 64),
            SourceHealthState.Outdated => Math.Clamp(score, 50, 74),
            SourceHealthState.Problematic => Math.Clamp(score, 0, 39),
            SourceHealthState.Unknown => Math.Clamp(score, 0, 50),
            _ => 0
        };

        private static string BuildStatusSummary(SourceHealthState healthState, int totalChannels, int totalMovies, int totalSeries, IReadOnlyList<SourceHealthComponentDraft> components)
        {
            if (healthState == SourceHealthState.NotSynced)
            {
                return "Source saved. Validation will appear after the first successful sync.";
            }

            var highlights = SelectComponentHighlights(components, healthState == SourceHealthState.Healthy, 2);
            var lead = healthState switch
            {
                SourceHealthState.Healthy => $"Healthy source. {BuildInventorySummary(totalChannels, totalMovies, totalSeries)}",
                SourceHealthState.Good => "Source is usable with minor quality gaps.",
                SourceHealthState.Weak => "Source works, but quality is uneven.",
                SourceHealthState.Incomplete => "Source synced, but important parts are still incomplete.",
                SourceHealthState.Outdated => "Source data is present, but parts of it are stale.",
                SourceHealthState.Problematic => "Source needs attention before it can be trusted.",
                SourceHealthState.Unknown => "Source health is not known yet.",
                _ => "Source saved. Validation will appear after the first successful sync."
            };

            return highlights.Count == 0 ? Trim(lead, 280) : Trim($"{lead} {string.Join(" ", highlights)}", 280);
        }

        private static string BuildValidationSummary(SourceHealthState healthState, int healthScore, IReadOnlyList<SourceHealthComponentDraft> components)
        {
            var lead = healthState switch
            {
                SourceHealthState.Healthy => "Component health is strong",
                SourceHealthState.Good => "Component health is good with minor gaps",
                SourceHealthState.Weak => "Component health is usable but uneven",
                SourceHealthState.Incomplete => "Component health shows missing core coverage",
                SourceHealthState.Outdated => "Component health is stable, but the data is stale",
                SourceHealthState.Problematic => "Component health found serious source problems",
                SourceHealthState.Unknown => "Component health is unknown",
                _ => "Component health is waiting for the first sync"
            };

            var highlights = SelectComponentHighlights(components, healthState == SourceHealthState.Healthy, 2);
            return highlights.Count == 0 ? $"{lead} ({healthScore}/100)." : $"{lead} ({healthScore}/100). {string.Join(" ", highlights)}";
        }

        private static List<string> SelectComponentHighlights(IReadOnlyList<SourceHealthComponentDraft> components, bool includeHealthyFallback, int maxCount)
        {
            var applicable = components
                .Where(item => item.State is not SourceHealthComponentState.NotApplicable and not SourceHealthComponentState.NotSynced)
                .OrderByDescending(item => GetComponentStateRank(item.State))
                .ThenBy(item => item.SortOrder)
                .ToList();

            var highlights = applicable.Where(item => item.State != SourceHealthComponentState.Healthy).Take(maxCount).Select(item => item.Summary).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            if (highlights.Count == 0 && includeHealthyFallback)
            {
                highlights = applicable.Take(maxCount).Select(item => item.Summary).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            }

            return highlights;
        }

        private static int GetComponentStateRank(SourceHealthComponentState state) => state switch
        {
            SourceHealthComponentState.Problematic => 6,
            SourceHealthComponentState.Incomplete => 5,
            SourceHealthComponentState.Outdated => 4,
            SourceHealthComponentState.Weak => 3,
            SourceHealthComponentState.Healthy => 2,
            SourceHealthComponentState.NotSynced => 1,
            _ => 0
        };

        private static string BuildInventorySummary(int totalChannels, int totalMovies, int totalSeries)
        {
            return totalChannels > 0
                ? $"Imported {totalChannels:N0} live channels, {totalMovies:N0} movies, and {totalSeries:N0} series."
                : $"Imported {totalMovies:N0} movies and {totalSeries:N0} series.";
        }

        private static string BuildGuideSummary(string lead, int fallbackCount, int reuseCount)
        {
            if (fallbackCount <= 0)
            {
                return lead;
            }

            return reuseCount > 0
                ? $"{lead} {fallbackCount:N0} channels rely on fallback matching, including {reuseCount:N0} reused prior matches."
                : $"{lead} {fallbackCount:N0} channels rely on fallback matching.";
        }

        private static string BuildLogoSummary(string lead, int providerLogoCount, int fallbackLogoCount)
        {
            if (fallbackLogoCount <= 0)
            {
                return lead;
            }

            return providerLogoCount > 0
                ? $"{lead} {fallbackLogoCount:N0} logos come from safe fallback coverage."
                : $"{lead} Coverage is driven by safe fallback logos.";
        }

        private static string BuildImportResultSummary(SourceProfile profile, SourceSyncState? syncState, int totalChannels, int totalMovies, int totalSeries)
        {
            if (syncState is { HttpStatusCode: >= 400 })
            {
                return BuildImportFailureMessage(syncState);
            }

            if (!profile.LastSync.HasValue)
            {
                return "No successful source sync has been recorded yet.";
            }

            if (!string.IsNullOrWhiteSpace(syncState?.ErrorLog))
            {
                return Trim(syncState.ErrorLog, 220);
            }

            return $"Imported {totalChannels} live channels, {totalMovies} movies, and {totalSeries} series.";
        }

        private static string BuildImportFailureMessage(SourceSyncState syncState)
        {
            var summary = Trim(syncState.ErrorLog, 180);
            return syncState.HttpStatusCode > 0 ? $"Sync failed with HTTP {syncState.HttpStatusCode}. {summary}" : $"Sync failed. {summary}";
        }

        private static string BuildTopIssueSummary(IReadOnlyList<SourceHealthIssueDraft> issues)
        {
            if (issues.Count == 0)
            {
                return "No obvious source-quality issues were flagged in the latest validation run.";
            }

            return Trim(string.Join(" ", issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.SortOrder).Take(2).Select(issue => issue.Message)), 320);
        }

        private static DuplicateAnalysis AnalyzeLiveDuplicates(IReadOnlyList<LiveChannelRecord> liveChannels)
        {
            var duplicateIds = new HashSet<int>();
            var samples = new List<string>();

            foreach (var group in liveChannels
                         .Where(channel => !string.IsNullOrWhiteSpace(channel.StreamUrl))
                         .GroupBy(channel => NormalizeStreamKey(channel.StreamUrl), StringComparer.OrdinalIgnoreCase)
                         .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
                         .OrderByDescending(group => group.Count()))
            {
                duplicateIds.UnionWith(group.Skip(1).Select(channel => channel.Id));
                if (samples.Count < 4) samples.Add($"{group.First().Name} x{group.Count()}");
            }

            foreach (var group in liveChannels
                         .GroupBy(channel => $"{NormalizeLooseKey(channel.CategoryName)}|{NormalizeLooseKey(channel.Name)}", StringComparer.OrdinalIgnoreCase)
                         .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
                         .OrderByDescending(group => group.Count()))
            {
                duplicateIds.UnionWith(group.Skip(1).Select(channel => channel.Id));
                if (samples.Count < 4) samples.Add($"{group.First().Name} x{group.Count()}");
            }

            return new DuplicateAnalysis(duplicateIds.Count, samples.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList());
        }

        private static DuplicateAnalysis AnalyzeCatalogDuplicates<T>(IReadOnlyList<T> entries, Func<T, string> keySelector, Func<T, string> labelSelector)
        {
            var duplicateCount = 0;
            var samples = new List<string>();

            foreach (var group in entries
                         .Select(entry => new { Entry = entry, Key = keySelector(entry) })
                         .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                         .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .OrderByDescending(group => group.Count()))
            {
                duplicateCount += group.Count() - 1;
                if (samples.Count < 4) samples.Add($"{labelSelector(group.First().Entry)} x{group.Count()}");
            }

            return new DuplicateAnalysis(duplicateCount, samples);
        }

        private static InvalidStreamAnalysis AnalyzeInvalidStreams(IReadOnlyList<LiveChannelRecord> liveChannels, IReadOnlyList<MovieRecord> movies)
        {
            var samples = new List<string>();
            var liveCount = 0;
            var vodCount = 0;

            foreach (var channel in liveChannels)
            {
                if (!IsProbablyValidStream(channel.StreamUrl))
                {
                    liveCount++;
                    if (samples.Count < 4) samples.Add(channel.Name);
                }
            }

            foreach (var movie in movies)
            {
                if (!IsProbablyValidStream(movie.StreamUrl))
                {
                    vodCount++;
                    if (samples.Count < 4) samples.Add(movie.Title);
                }
            }

            return new InvalidStreamAnalysis(liveCount + vodCount, liveCount, vodCount, samples);
        }

        private static SuspiciousAnalysis AnalyzeSuspiciousLiveEntries(SourceType sourceType, IReadOnlyList<LiveChannelRecord> liveChannels)
        {
            var count = 0;
            var samples = new List<string>();
            foreach (var channel in liveChannels)
            {
                if (IsSuspiciousLiveChannel(sourceType, channel))
                {
                    count++;
                    if (samples.Count < 4) samples.Add(channel.Name);
                }
            }

            return new SuspiciousAnalysis(count, samples);
        }

        private static SuspiciousAnalysis AnalyzeSuspiciousVodEntries(SourceType sourceType, IReadOnlyList<MovieRecord> movies, IReadOnlyList<SeriesRecord> series)
        {
            var count = 0;
            var samples = new List<string>();

            foreach (var movie in movies)
            {
                if (IsSuspiciousCatalogTitle(sourceType, movie.Title, movie.CategoryName, movie.RawCategoryName))
                {
                    count++;
                    if (samples.Count < 4) samples.Add(movie.Title);
                }
            }

            foreach (var item in series)
            {
                if (IsSuspiciousCatalogTitle(sourceType, item.Title, item.CategoryName, item.RawCategoryName))
                {
                    count++;
                    if (samples.Count < 4) samples.Add(item.Title);
                }
            }

            return new SuspiciousAnalysis(count, samples);
        }

        private static bool IsSuspiciousLiveChannel(SourceType sourceType, LiveChannelRecord channel)
        {
            if (string.IsNullOrWhiteSpace(channel.Name))
            {
                return true;
            }

            if (ContentClassifier.IsGarbageTitle(channel.Name) ||
                ContentClassifier.IsPromotionalCatalogLabel(channel.Name) ||
                ContentClassifier.IsGarbageCategoryName(channel.CategoryName))
            {
                return true;
            }

            return sourceType == SourceType.M3U &&
                   (ContentClassifier.IsM3uBucketOrAdultLabel(channel.Name) || ContentClassifier.IsM3uBucketOrAdultLabel(channel.CategoryName));
        }

        private static bool IsSuspiciousCatalogTitle(SourceType sourceType, string title, string categoryName, string rawCategoryName)
        {
            if (ContentClassifier.IsGarbageTitle(title) ||
                ContentClassifier.IsPromotionalCatalogLabel(title) ||
                ContentClassifier.IsGarbageCategoryName(categoryName) ||
                ContentClassifier.IsGarbageCategoryName(rawCategoryName))
            {
                return true;
            }

            return sourceType == SourceType.M3U &&
                   (ContentClassifier.IsM3uBucketOrAdultLabel(title) ||
                    ContentClassifier.IsM3uBucketOrAdultLabel(categoryName) ||
                    ContentClassifier.IsM3uBucketOrAdultLabel(rawCategoryName));
        }

        private static bool IsProbablyValidStream(string? streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return false;
            }

            var trimmed = streamUrl.Trim();
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathRooted(trimmed))
            {
                return true;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return ValidStreamSchemes.Contains(uri.Scheme);
        }

        private static string ChooseCatalogKey(string fingerprint, string title, string categoryName)
        {
            return !string.IsNullOrWhiteSpace(fingerprint) ? fingerprint : $"{NormalizeLooseKey(categoryName)}|{NormalizeLooseKey(title)}";
        }

        private static string NormalizeStreamKey(string value)
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

        private static string NormalizeLooseKey(string value)
        {
            return ContentClassifier.NormalizeLabel(value).Trim().ToLowerInvariant();
        }

        private static string JoinSamples(IEnumerable<string> samples)
        {
            return string.Join(", ", samples.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        }

        private static double CalculateRatio(int part, int total)
        {
            return total <= 0 ? 0d : (double)part / total;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string FormatTimestamp(DateTime? value)
        {
            return !value.HasValue ? "Never" : NormalizeUtc(value.Value).ToLocalTime().ToString("g");
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength - 3) + "...";
        }

        private sealed class LiveChannelRecord
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string ProviderLogoUrl { get; set; } = string.Empty;
            public string EpgChannelId { get; set; } = string.Empty;
            public string ProviderEpgChannelId { get; set; } = string.Empty;
            public ChannelEpgMatchSource EpgMatchSource { get; set; }
            public ChannelLogoSource LogoSource { get; set; }
            public string CategoryName { get; set; } = string.Empty;
        }

        private sealed class MovieRecord
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public string RawCategoryName { get; set; } = string.Empty;
            public string DedupFingerprint { get; set; } = string.Empty;
        }

        private sealed class SeriesRecord
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public string RawCategoryName { get; set; } = string.Empty;
            public string DedupFingerprint { get; set; } = string.Empty;
        }

        private sealed class EpisodeRecord
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string SeriesTitle { get; set; } = string.Empty;
            public int SeasonNumber { get; set; }
            public int EpisodeNumber { get; set; }
        }

        private sealed class GuideMetrics
        {
            public int MatchedChannelCount { get; set; }
            public int CurrentCoverageCount { get; set; }
            public int NextCoverageCount { get; set; }
        }

        private sealed class SourceHealthEvaluation
        {
            public DateTime EvaluatedAtUtc { get; set; }
            public DateTime? LastSyncAttemptAtUtc { get; set; }
            public DateTime? LastSuccessfulSyncAtUtc { get; set; }
            public int HealthScore { get; set; }
            public SourceHealthState HealthState { get; set; }
            public string StatusSummary { get; set; } = string.Empty;
            public string ImportResultSummary { get; set; } = string.Empty;
            public string ValidationSummary { get; set; } = string.Empty;
            public string TopIssueSummary { get; set; } = string.Empty;
            public int TotalChannelCount { get; set; }
            public int TotalMovieCount { get; set; }
            public int TotalSeriesCount { get; set; }
            public int DuplicateCount { get; set; }
            public int InvalidStreamCount { get; set; }
            public int ChannelsWithEpgMatchCount { get; set; }
            public int ChannelsWithCurrentProgramCount { get; set; }
            public int ChannelsWithNextProgramCount { get; set; }
            public int ChannelsWithLogoCount { get; set; }
            public int SuspiciousEntryCount { get; set; }
            public int WarningCount { get; set; }
            public int ErrorCount { get; set; }
            public List<SourceHealthProbeDraft> Probes { get; set; } = new();
            public List<SourceHealthComponentDraft> Components { get; set; } = new();
            public List<SourceHealthIssueDraft> Issues { get; set; } = new();
        }

        private sealed class SourceHealthProbeDraft
        {
            public SourceHealthProbeType ProbeType { get; set; }
            public SourceHealthProbeStatus Status { get; set; }
            public DateTime? ProbedAtUtc { get; set; }
            public int CandidateCount { get; set; }
            public int SampleSize { get; set; }
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public int TimeoutCount { get; set; }
            public int HttpErrorCount { get; set; }
            public int TransportErrorCount { get; set; }
            public string Summary { get; set; } = string.Empty;
            public int SortOrder { get; set; }
        }

        private sealed class SourceHealthComponentDraft
        {
            public SourceHealthComponentType ComponentType { get; set; }
            public SourceHealthComponentState State { get; set; }
            public int Score { get; set; }
            public string Summary { get; set; } = string.Empty;
            public int RelevantCount { get; set; }
            public int HealthyCount { get; set; }
            public int IssueCount { get; set; }
            public int SortOrder { get; set; }
        }

        private sealed record DuplicateAnalysis(int Count, List<string> Samples);
        private sealed record InvalidStreamAnalysis(int TotalCount, int LiveCount, int VodCount, List<string> Samples);
        private sealed record SuspiciousAnalysis(int Count, List<string> Samples);
        private sealed record SourceHealthIssueDraft(SourceHealthIssueSeverity Severity, string Code, string Title, string Message, int AffectedCount, string SampleItems, int SortOrder);
    }
}
