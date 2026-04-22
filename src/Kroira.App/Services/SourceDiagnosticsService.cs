#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceDiagnosticsService
    {
        Task<IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>> GetSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds);
    }

    public sealed class SourceDiagnosticsService : ISourceDiagnosticsService
    {
        private readonly ISourceHealthService _sourceHealthService;

        public SourceDiagnosticsService(ISourceHealthService sourceHealthService)
        {
            _sourceHealthService = sourceHealthService;
        }

        public async Task<IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>> GetSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds)
        {
            var ids = sourceIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, SourceDiagnosticsSnapshot>();
            }

            var reportSourceIds = await db.SourceHealthReports
                .AsNoTracking()
                .Where(report => ids.Contains(report.SourceProfileId))
                .Select(report => report.SourceProfileId)
                .ToListAsync();
            if (reportSourceIds.Count > 0)
            {
                var componentSourceIds = await db.SourceHealthComponents
                    .AsNoTracking()
                    .Join(
                        db.SourceHealthReports
                            .AsNoTracking()
                            .Where(report => ids.Contains(report.SourceProfileId)),
                        component => component.SourceHealthReportId,
                        report => report.Id,
                        (component, report) => report.SourceProfileId)
                    .Distinct()
                    .ToListAsync();

                var probeSourceIds = await db.SourceHealthProbes
                    .AsNoTracking()
                    .Join(
                        db.SourceHealthReports
                            .AsNoTracking()
                            .Where(report => ids.Contains(report.SourceProfileId)),
                        probe => probe.SourceHealthReportId,
                        report => report.Id,
                        (probe, report) => report.SourceProfileId)
                    .Distinct()
                    .ToListAsync();

                var sourcesToRefresh = reportSourceIds
                    .Except(componentSourceIds)
                    .Concat(reportSourceIds.Except(probeSourceIds))
                    .Distinct()
                    .ToList();

                foreach (var sourceId in sourcesToRefresh)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceId);
                }

                if (sourcesToRefresh.Count > 0)
                {
                    db.ChangeTracker.Clear();
                }
            }

            var nowUtc = DateTime.UtcNow;
            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => ids.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id);
            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => ids.Contains(state.SourceProfileId))
                .ToDictionaryAsync(state => state.SourceProfileId);
            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(credential => ids.Contains(credential.SourceProfileId))
                .Select(credential => new SourceCredentialView
                {
                    SourceProfileId = credential.SourceProfileId,
                    DetectedEpgUrl = credential.DetectedEpgUrl,
                    ManualEpgUrl = credential.EpgUrl,
                    EpgMode = credential.EpgMode
                })
                .ToDictionaryAsync(credential => credential.SourceProfileId);
            var healthReports = await db.SourceHealthReports
                .AsNoTracking()
                .Where(report => ids.Contains(report.SourceProfileId))
                .ToDictionaryAsync(report => report.SourceProfileId);
            var healthComponents = await db.SourceHealthComponents
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports
                        .AsNoTracking()
                        .Where(report => ids.Contains(report.SourceProfileId)),
                    component => component.SourceHealthReportId,
                    report => report.Id,
                    (component, report) => new
                    {
                        report.SourceProfileId,
                        Component = component
                    })
                .ToListAsync();
            var componentLookup = healthComponents
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SourceDiagnosticsComponentSnapshot>)group
                        .OrderBy(item => item.Component.SortOrder)
                        .Select(item => new SourceDiagnosticsComponentSnapshot
                        {
                            ComponentType = item.Component.ComponentType,
                            State = item.Component.State,
                            Score = item.Component.Score,
                            Summary = item.Component.Summary,
                            RelevantCount = item.Component.RelevantCount,
                            HealthyCount = item.Component.HealthyCount,
                            IssueCount = item.Component.IssueCount
                        })
                        .ToList());
            var healthProbes = await db.SourceHealthProbes
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports
                        .AsNoTracking()
                        .Where(report => ids.Contains(report.SourceProfileId)),
                    probe => probe.SourceHealthReportId,
                    report => report.Id,
                    (probe, report) => new
                    {
                        report.SourceProfileId,
                        Probe = probe
                    })
                .ToListAsync();
            var probeLookup = healthProbes
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SourceDiagnosticsProbeSnapshot>)group
                        .OrderBy(item => item.Probe.SortOrder)
                        .Select(item => new SourceDiagnosticsProbeSnapshot
                        {
                            ProbeType = item.Probe.ProbeType,
                            Status = item.Probe.Status,
                            ProbedAtUtc = item.Probe.ProbedAtUtc,
                            CandidateCount = item.Probe.CandidateCount,
                            SampleSize = item.Probe.SampleSize,
                            SuccessCount = item.Probe.SuccessCount,
                            FailureCount = item.Probe.FailureCount,
                            TimeoutCount = item.Probe.TimeoutCount,
                            HttpErrorCount = item.Probe.HttpErrorCount,
                            TransportErrorCount = item.Probe.TransportErrorCount,
                            Summary = item.Probe.Summary
                        })
                        .ToList());
            var healthIssues = await db.SourceHealthIssues
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports
                        .AsNoTracking()
                        .Where(report => ids.Contains(report.SourceProfileId)),
                    issue => issue.SourceHealthReportId,
                    report => report.Id,
                    (issue, report) => new
                    {
                        report.SourceProfileId,
                        Issue = issue
                    })
                .ToListAsync();
            var issueLookup = healthIssues
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SourceDiagnosticsIssueSnapshot>)group
                        .OrderByDescending(item => item.Issue.Severity)
                        .ThenBy(item => item.Issue.SortOrder)
                        .Select(item => new SourceDiagnosticsIssueSnapshot
                        {
                            Severity = item.Issue.Severity,
                            Title = item.Issue.Title,
                            Message = item.Issue.Message,
                            AffectedCount = item.Issue.AffectedCount,
                            SampleItems = item.Issue.SampleItems
                        })
                        .ToList());

            var epgLogs = new Dictionary<int, EpgSyncLog>();
            try
            {
                epgLogs = await db.EpgSyncLogs
                    .AsNoTracking()
                    .Where(log => ids.Contains(log.SourceProfileId))
                    .ToDictionaryAsync(log => log.SourceProfileId);
            }
            catch
            {
            }

            var liveCounts = await CountBySourceAsync(
                db.ChannelCategories
                    .AsNoTracking()
                    .Where(category => ids.Contains(category.SourceProfileId))
                    .Join(
                        db.Channels.AsNoTracking(),
                        category => category.Id,
                        channel => channel.ChannelCategoryId,
                        (category, channel) => category.SourceProfileId));
            var movieCounts = await CountBySourceAsync(
                db.Movies
                    .AsNoTracking()
                    .Where(movie => ids.Contains(movie.SourceProfileId))
                    .Select(movie => movie.SourceProfileId));
            var seriesCounts = await CountBySourceAsync(
                db.Series
                    .AsNoTracking()
                    .Where(series => ids.Contains(series.SourceProfileId))
                    .Select(series => series.SourceProfileId));

            var matchedCounts = await QueryGuideCountsAsync(db, ids, query => query);
            var currentCoverageCounts = await QueryGuideCountsAsync(
                db,
                ids,
                query => query.Where(program => program.StartTimeUtc <= nowUtc && program.EndTimeUtc > nowUtc));
            var nextCoverageCounts = await QueryGuideCountsAsync(
                db,
                ids,
                query => query.Where(program => program.StartTimeUtc > nowUtc && program.StartTimeUtc < nowUtc.AddHours(24)));

            var snapshots = new Dictionary<int, SourceDiagnosticsSnapshot>(ids.Count);
            foreach (var sourceId in ids)
            {
                profiles.TryGetValue(sourceId, out var profile);
                syncStates.TryGetValue(sourceId, out var syncState);
                credentials.TryGetValue(sourceId, out var credential);
                epgLogs.TryGetValue(sourceId, out var epgLog);
                healthReports.TryGetValue(sourceId, out var healthReport);
                componentLookup.TryGetValue(sourceId, out var sourceComponents);
                probeLookup.TryGetValue(sourceId, out var sourceProbes);
                issueLookup.TryGetValue(sourceId, out var sourceIssues);

                var sourceType = profile?.Type ?? SourceType.M3U;
                var liveCount = healthReport?.TotalChannelCount ?? (liveCounts.TryGetValue(sourceId, out var live) ? live : 0);
                var movieCount = healthReport?.TotalMovieCount ?? (movieCounts.TryGetValue(sourceId, out var movies) ? movies : 0);
                var seriesCount = healthReport?.TotalSeriesCount ?? (seriesCounts.TryGetValue(sourceId, out var series) ? series : 0);
                var matchedCount = Math.Min(healthReport?.ChannelsWithEpgMatchCount ?? (matchedCounts.TryGetValue(sourceId, out var matched) ? matched : 0), liveCount);
                var currentCoverageCount = Math.Min(healthReport?.ChannelsWithCurrentProgramCount ?? (currentCoverageCounts.TryGetValue(sourceId, out var current) ? current : 0), liveCount);
                var nextCoverageCount = Math.Min(healthReport?.ChannelsWithNextProgramCount ?? (nextCoverageCounts.TryGetValue(sourceId, out var next) ? next : 0), liveCount);
                var unmatchedCount = Math.Max(0, liveCount - matchedCount);

                var activeMode = credential?.EpgMode ?? EpgActiveMode.Detected;
                var detectedEpgUrl = credential?.DetectedEpgUrl ?? string.Empty;
                var manualEpgUrl = credential?.ManualEpgUrl ?? string.Empty;
                var activeXmltvUrl = ResolveActiveXmltvUrl(activeMode, detectedEpgUrl, manualEpgUrl, epgLog);
                var status = ResolveEpgStatus(sourceType, liveCount, activeMode, detectedEpgUrl, manualEpgUrl, epgLog);
                var resultCode = epgLog?.ResultCode ?? EpgSyncResultCode.None;
                var failureStage = epgLog?.FailureStage ?? EpgFailureStage.None;
                var importFailure = syncState != null && syncState.HttpStatusCode >= 400;
                var hasCatalog = liveCount + movieCount + seriesCount > 0;
                var hasPersistedGuideData = matchedCount > 0 || (epgLog?.ProgrammeCount ?? 0) > 0;
                var guideWarnings = BuildGuideWarnings(sourceType, liveCount, activeMode, status, resultCode, matchedCount, unmatchedCount, currentCoverageCount, nextCoverageCount, !string.IsNullOrWhiteSpace(detectedEpgUrl), !string.IsNullOrWhiteSpace(manualEpgUrl), hasPersistedGuideData);
                var importWarnings = BuildImportWarnings(hasCatalog, liveCount, sourceType, syncState);
                var failureSummary = BuildFailureSummary(syncState, status, resultCode, failureStage, epgLog);
                var healthLabel = healthReport != null
                    ? BuildHealthLabel(healthReport.HealthState)
                    : ComputeHealthLabel(profile?.LastSync, hasCatalog, importFailure, status, resultCode, guideWarnings.Count);
                var validationResult = healthReport?.ValidationSummary
                    ?? BuildValidationSummaryFallback(liveCount, matchedCount, currentCoverageCount, nextCoverageCount, guideWarnings.Count);
                var warningSummary = healthReport?.TopIssueSummary
                    ?? string.Join(" ", importWarnings.Concat(guideWarnings).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
                var lastSyncAttemptText = FormatTimestamp(healthReport?.LastSyncAttemptAtUtc ?? syncState?.LastAttempt);

                snapshots[sourceId] = new SourceDiagnosticsSnapshot
                {
                    SourceProfileId = sourceId,
                    SourceType = sourceType,
                    LiveChannelCount = liveCount,
                    MovieCount = movieCount,
                    SeriesCount = seriesCount,
                    HasDetectedEpgUrl = !string.IsNullOrWhiteSpace(detectedEpgUrl),
                    HasManualEpgUrl = !string.IsNullOrWhiteSpace(manualEpgUrl),
                    HasActiveXmltvUrl = !string.IsNullOrWhiteSpace(activeXmltvUrl),
                    HasEpgUrl = !string.IsNullOrWhiteSpace(detectedEpgUrl) || !string.IsNullOrWhiteSpace(manualEpgUrl) || !string.IsNullOrWhiteSpace(activeXmltvUrl),
                    HasPersistedGuideData = hasPersistedGuideData,
                    MatchedLiveChannelCount = matchedCount,
                    UnmatchedLiveChannelCount = unmatchedCount,
                    CurrentCoverageCount = currentCoverageCount,
                    NextCoverageCount = nextCoverageCount,
                    DuplicateCount = healthReport?.DuplicateCount ?? 0,
                    InvalidStreamCount = healthReport?.InvalidStreamCount ?? 0,
                    ChannelsWithLogoCount = healthReport?.ChannelsWithLogoCount ?? 0,
                    SuspiciousEntryCount = healthReport?.SuspiciousEntryCount ?? 0,
                    HealthScore = healthReport?.HealthScore ?? 0,
                    EpgProgramCount = epgLog?.ProgrammeCount ?? 0,
                    ImportWarningCount = importWarnings.Count,
                    GuideWarningCount = guideWarnings.Count,
                    HealthLabel = healthLabel,
                    StatusSummary = healthReport?.StatusSummary ?? BuildSourceSummary(profile?.LastSync, hasCatalog, importFailure, status, failureSummary, liveCount, matchedCount, currentCoverageCount, nextCoverageCount),
                    ImportResultText = healthReport?.ImportResultSummary ?? BuildImportResult(syncState, profile?.LastSync, liveCount, movieCount, seriesCount, hasCatalog),
                    ValidationResultText = validationResult,
                    EpgCoverageText = BuildCoverageSummary(liveCount, activeMode, status, resultCode, matchedCount, unmatchedCount, currentCoverageCount, nextCoverageCount, hasPersistedGuideData),
                    EpgStatusText = BuildGuideStatusLabel(status, resultCode, activeMode),
                    EpgStatusSummary = BuildGuideStatusSummary(status, resultCode, failureStage, activeMode, liveCount, matchedCount, unmatchedCount, currentCoverageCount, nextCoverageCount, epgLog),
                    EpgUrlSummaryText = BuildUrlSummary(sourceType, activeMode, detectedEpgUrl, manualEpgUrl, activeXmltvUrl),
                    MatchBreakdownText = FormatMatchBreakdown(epgLog?.MatchBreakdown, matchedCount, unmatchedCount, currentCoverageCount, nextCoverageCount, liveCount),
                    WarningSummaryText = warningSummary,
                    FailureSummaryText = failureSummary,
                    LastSuccessfulSyncText = $"Import {FormatTimestamp(profile?.LastSync)} - Guide {FormatTimestamp(epgLog?.LastSuccessAtUtc)}",
                    LastSyncAttemptText = lastSyncAttemptText,
                    LastImportSuccessText = FormatTimestamp(profile?.LastSync),
                    LastEpgSuccessText = FormatTimestamp(epgLog?.LastSuccessAtUtc),
                    EpgSyncSuccess = epgLog?.IsSuccess ?? false,
                    EpgStatus = status,
                    EpgResultCode = resultCode,
                    EpgFailureStage = failureStage,
                    ActiveEpgMode = activeMode,
                    ActiveEpgModeText = BuildModeLabel(activeMode),
                    DetectedEpgUrl = detectedEpgUrl,
                    ManualEpgUrl = manualEpgUrl,
                    ActiveXmltvUrl = activeXmltvUrl,
                    GuideAvailableForLive = liveCount == 0 || status is EpgStatus.Ready or EpgStatus.ManualOverride,
                    IsPartialGuideMatch = resultCode == EpgSyncResultCode.PartialMatch,
                    HealthComponents = sourceComponents ?? Array.Empty<SourceDiagnosticsComponentSnapshot>(),
                    HealthProbes = sourceProbes ?? Array.Empty<SourceDiagnosticsProbeSnapshot>(),
                    Issues = sourceIssues ?? Array.Empty<SourceDiagnosticsIssueSnapshot>()
                };
            }

            return snapshots;
        }

        private static async Task<Dictionary<int, int>> CountBySourceAsync(IQueryable<int> sourceIds)
        {
            return await sourceIds
                .GroupBy(sourceId => sourceId)
                .Select(group => new { SourceProfileId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);
        }

        private static async Task<Dictionary<int, int>> QueryGuideCountsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds,
            Func<IQueryable<EpgProgram>, IQueryable<EpgProgram>> programFilter)
        {
            try
            {
                return await programFilter(db.EpgPrograms.AsNoTracking())
                    .Join(
                        db.Channels.AsNoTracking(),
                        program => program.ChannelId,
                        channel => channel.Id,
                        (program, channel) => new
                        {
                            program.ChannelId,
                            channel.ChannelCategoryId
                        })
                    .Join(
                        db.ChannelCategories
                            .AsNoTracking()
                            .Where(category => sourceIds.Contains(category.SourceProfileId)),
                        programChannel => programChannel.ChannelCategoryId,
                        category => category.Id,
                        (programChannel, category) => new
                        {
                            category.SourceProfileId,
                            programChannel.ChannelId
                        })
                    .GroupBy(item => item.SourceProfileId)
                    .Select(group => new
                    {
                        SourceProfileId = group.Key,
                        Count = group.Select(item => item.ChannelId).Distinct().Count()
                    })
                    .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);
            }
            catch
            {
                return new Dictionary<int, int>();
            }
        }

        private static EpgStatus ResolveEpgStatus(SourceType sourceType, int liveCount, EpgActiveMode activeMode, string detectedEpgUrl, string manualEpgUrl, EpgSyncLog? epgLog)
        {
            if (epgLog != null && epgLog.Status != EpgStatus.Unknown)
            {
                return epgLog.Status;
            }

            if (activeMode == EpgActiveMode.None)
            {
                return EpgStatus.Unknown;
            }

            if (activeMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(manualEpgUrl))
            {
                return EpgStatus.UnavailableNoXmltv;
            }

            if (sourceType == SourceType.M3U && liveCount > 0 && string.IsNullOrWhiteSpace(detectedEpgUrl) && string.IsNullOrWhiteSpace(manualEpgUrl))
            {
                return EpgStatus.UnavailableNoXmltv;
            }

            return EpgStatus.Unknown;
        }

        private static string ResolveActiveXmltvUrl(EpgActiveMode activeMode, string detectedEpgUrl, string manualEpgUrl, EpgSyncLog? epgLog)
        {
            if (!string.IsNullOrWhiteSpace(epgLog?.ActiveXmltvUrl))
            {
                return epgLog.ActiveXmltvUrl;
            }

            return activeMode switch
            {
                EpgActiveMode.Manual => manualEpgUrl,
                EpgActiveMode.None => string.Empty,
                _ => detectedEpgUrl
            };
        }

        private static List<string> BuildImportWarnings(bool hasCatalog, int liveCount, SourceType sourceType, SourceSyncState? syncState)
        {
            var warnings = new List<string>();
            if (!hasCatalog)
            {
                warnings.Add("Import produced no catalog items.");
            }

            if (liveCount == 0)
            {
                warnings.Add("No live channels imported.");
            }

            if (sourceType == SourceType.M3U &&
                syncState != null &&
                syncState.HttpStatusCode == 200 &&
                syncState.ErrorLog.Contains("LiveOnly mode", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("VOD import is disabled by M3U import mode.");
            }

            return warnings;
        }

        private static List<string> BuildGuideWarnings(SourceType sourceType, int liveCount, EpgActiveMode activeMode, EpgStatus status, EpgSyncResultCode resultCode, int matchedCount, int unmatchedCount, int currentCoverageCount, int nextCoverageCount, bool hasDetectedEpgUrl, bool hasManualEpgUrl, bool hasPersistedGuideData)
        {
            var warnings = new List<string>();
            if (liveCount == 0)
            {
                return warnings;
            }

            if (activeMode == EpgActiveMode.None)
            {
                warnings.Add("Guide mode is disabled for this source.");
                return warnings;
            }

            if (status == EpgStatus.UnavailableNoXmltv)
            {
                warnings.Add("Provider does not advertise an XMLTV guide URL.");
            }

            if (status == EpgStatus.FailedFetchOrParse)
            {
                warnings.Add("Guide sync failed before any usable guide data was stored.");
            }

            if (status == EpgStatus.Stale)
            {
                warnings.Add("Guide refresh failed; older guide data may still be shown.");
            }

            if (resultCode == EpgSyncResultCode.ZeroCoverage)
            {
                warnings.Add(hasPersistedGuideData
                    ? "Guide data stored, but no live channels matched."
                    : "XMLTV parsed, but no live channels matched.");
            }

            if (resultCode == EpgSyncResultCode.PartialMatch && unmatchedCount > 0)
            {
                warnings.Add($"{unmatchedCount} live channels are unmatched.");
            }

            if ((status is EpgStatus.Ready or EpgStatus.ManualOverride or EpgStatus.Stale) && matchedCount > 0 && currentCoverageCount == 0 && nextCoverageCount == 0)
            {
                warnings.Add("Matched guide data exists, but there is no current or next listing in the next 24 hours.");
            }

            if (sourceType == SourceType.M3U && !hasDetectedEpgUrl && !hasManualEpgUrl)
            {
                warnings.Add("No XMLTV URL is currently available for this playlist.");
            }

            return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ComputeHealthLabel(DateTime? lastImportSuccessUtc, bool hasCatalog, bool importFailure, EpgStatus status, EpgSyncResultCode resultCode, int guideWarningCount)
        {
            if (!lastImportSuccessUtc.HasValue)
            {
                return importFailure ? "Failing" : "Not synced";
            }

            if (importFailure || !hasCatalog)
            {
                return "Failing";
            }

            if (status == EpgStatus.Syncing)
            {
                return "Working";
            }

            if (status is EpgStatus.FailedFetchOrParse or EpgStatus.Stale)
            {
                return "Degraded";
            }

            if (resultCode is EpgSyncResultCode.PartialMatch or EpgSyncResultCode.ZeroCoverage || guideWarningCount > 0)
            {
                return "Degraded";
            }

            return "Healthy";
        }

        private static string BuildHealthLabel(SourceHealthState healthState) => healthState switch
        {
            SourceHealthState.Healthy => "Healthy",
            SourceHealthState.Weak => "Weak",
            SourceHealthState.Incomplete => "Incomplete",
            SourceHealthState.Outdated => "Outdated",
            SourceHealthState.Problematic => "Problematic",
            _ => "Not synced"
        };

        private static string BuildValidationSummaryFallback(int liveCount, int matchedCount, int currentCoverageCount, int nextCoverageCount, int guideWarningCount)
        {
            if (liveCount == 0)
            {
                return guideWarningCount > 0
                    ? "Validation is pending richer catalog quality data."
                    : "No live-channel validation metrics are available yet.";
            }

            return $"Guide match {matchedCount}/{liveCount}, current {currentCoverageCount}/{liveCount}, next {nextCoverageCount}/{liveCount}.";
        }

        private static string BuildSourceSummary(DateTime? lastImportSuccessUtc, bool hasCatalog, bool importFailure, EpgStatus status, string failureSummary, int liveCount, int matchedCount, int currentCoverageCount, int nextCoverageCount)
        {
            if (importFailure)
            {
                return string.IsNullOrWhiteSpace(failureSummary) ? "Latest import failed." : failureSummary;
            }

            if (!lastImportSuccessUtc.HasValue)
            {
                return "Source saved. No successful import recorded yet.";
            }

            if (!hasCatalog)
            {
                return "Import completed, but the source produced no catalog items.";
            }

            return status switch
            {
                EpgStatus.Syncing => "Guide sync is in progress.",
                EpgStatus.UnavailableNoXmltv => "Guide unavailable because the provider does not advertise XMLTV.",
                EpgStatus.FailedFetchOrParse => string.IsNullOrWhiteSpace(failureSummary) ? "Guide sync failed." : failureSummary,
                EpgStatus.Stale => string.IsNullOrWhiteSpace(failureSummary) ? "Guide is stale after a failed refresh." : failureSummary,
                _ when liveCount == 0 => "Import succeeded. No live channels are available for guide coverage.",
                _ => $"Guide coverage now: current {currentCoverageCount}/{liveCount}, next {nextCoverageCount}/{liveCount}, matched {matchedCount}/{liveCount}."
            };
        }

        private static string BuildImportResult(SourceSyncState? syncState, DateTime? lastImportSuccessUtc, int liveCount, int movieCount, int seriesCount, bool hasCatalog)
        {
            if (syncState != null && syncState.HttpStatusCode >= 400)
            {
                return $"Failed - HTTP {syncState.HttpStatusCode} - {Trim(syncState.ErrorLog, 88)}";
            }

            if (!lastImportSuccessUtc.HasValue)
            {
                return "No successful import recorded.";
            }

            return hasCatalog
                ? $"Success - {liveCount} live, {movieCount} movies, {seriesCount} series."
                : $"Imported at {FormatTimestamp(lastImportSuccessUtc)} - no catalog items.";
        }

        private static string BuildCoverageSummary(int liveCount, EpgActiveMode activeMode, EpgStatus status, EpgSyncResultCode resultCode, int matchedCount, int unmatchedCount, int currentCoverageCount, int nextCoverageCount, bool hasPersistedGuideData)
        {
            if (liveCount == 0) return "No live channels available.";
            if (activeMode == EpgActiveMode.None) return "Guide disabled for this source.";
            if (status == EpgStatus.UnavailableNoXmltv) return "Provider does not advertise XMLTV.";
            if (status == EpgStatus.FailedFetchOrParse) return "Guide sync failed before usable guide data was stored.";
            if (status == EpgStatus.Stale && !hasPersistedGuideData) return "Guide is stale and no usable coverage is available.";
            if (resultCode == EpgSyncResultCode.ZeroCoverage) return $"0/{liveCount} matched. Current {currentCoverageCount}/{liveCount}, next {nextCoverageCount}/{liveCount}.";
            return $"{matchedCount}/{liveCount} matched, {unmatchedCount} unmatched. Current {currentCoverageCount}/{liveCount}, next {nextCoverageCount}/{liveCount}.";
        }

        private static string BuildGuideStatusLabel(EpgStatus status, EpgSyncResultCode resultCode, EpgActiveMode activeMode)
        {
            if (activeMode == EpgActiveMode.None) return "Guide disabled";

            return status switch
            {
                EpgStatus.Syncing => "Guide syncing",
                EpgStatus.UnavailableNoXmltv => "No XMLTV advertised",
                EpgStatus.FailedFetchOrParse when resultCode == EpgSyncResultCode.ParseFailed => "Guide parse failed",
                EpgStatus.FailedFetchOrParse when resultCode == EpgSyncResultCode.PersistFailed => "Guide persist failed",
                EpgStatus.FailedFetchOrParse => "Guide fetch failed",
                EpgStatus.Stale => "Guide stale",
                EpgStatus.ManualOverride when resultCode == EpgSyncResultCode.PartialMatch => "Manual guide partial",
                EpgStatus.ManualOverride when resultCode == EpgSyncResultCode.ZeroCoverage => "Manual guide zero coverage",
                EpgStatus.ManualOverride => "Manual guide ready",
                EpgStatus.Ready when resultCode == EpgSyncResultCode.PartialMatch => "Guide partial",
                EpgStatus.Ready when resultCode == EpgSyncResultCode.ZeroCoverage => "Guide zero coverage",
                EpgStatus.Ready => "Guide ready",
                _ when activeMode == EpgActiveMode.Manual => "Manual guide configured",
                _ => "Guide not synced"
            };
        }

        private static string BuildGuideStatusSummary(EpgStatus status, EpgSyncResultCode resultCode, EpgFailureStage failureStage, EpgActiveMode activeMode, int liveCount, int matchedCount, int unmatchedCount, int currentCoverageCount, int nextCoverageCount, EpgSyncLog? epgLog)
        {
            if (liveCount == 0) return "No live channels are available for guide coverage.";
            if (activeMode == EpgActiveMode.None) return "Guide mode is disabled for this source.";
            if (status == EpgStatus.UnavailableNoXmltv)
            {
                return activeMode == EpgActiveMode.Manual
                    ? "Manual guide mode is selected, but no manual XMLTV URL is saved."
                    : "The provider does not advertise an XMLTV guide URL.";
            }

            if (status == EpgStatus.FailedFetchOrParse)
            {
                var stageText = failureStage switch
                {
                    EpgFailureStage.Fetch => "XMLTV URL exists, but fetch failed.",
                    EpgFailureStage.Parse => "XMLTV was fetched, but parsing failed.",
                    EpgFailureStage.Persist => "XMLTV was parsed, but persistence failed.",
                    _ => "XMLTV guide sync failed."
                };
                return string.IsNullOrWhiteSpace(epgLog?.FailureReason) ? stageText : $"{stageText} {Trim(epgLog.FailureReason, 140)}";
            }

            if (status == EpgStatus.Stale)
            {
                return string.IsNullOrWhiteSpace(epgLog?.FailureReason)
                    ? "Latest refresh failed, so older guide data may still be shown."
                    : $"Latest refresh failed. {Trim(epgLog.FailureReason, 140)}";
            }

            if (resultCode == EpgSyncResultCode.ZeroCoverage) return "XMLTV was parsed, but none of the live channels matched guide data.";
            return $"{matchedCount}/{liveCount} live channels matched. Current coverage {currentCoverageCount}/{liveCount}, next coverage {nextCoverageCount}/{liveCount}, unmatched {unmatchedCount}.";
        }

        private static string BuildUrlSummary(SourceType sourceType, EpgActiveMode activeMode, string detectedEpgUrl, string manualEpgUrl, string activeXmltvUrl)
        {
            if (activeMode == EpgActiveMode.None)
            {
                return !string.IsNullOrWhiteSpace(detectedEpgUrl)
                    ? "Guide disabled. Detected XMLTV URL is preserved."
                    : !string.IsNullOrWhiteSpace(manualEpgUrl)
                        ? "Guide disabled. Manual XMLTV URL is preserved."
                        : "Guide disabled. No XMLTV URL is active.";
            }

            if (activeMode == EpgActiveMode.Manual)
            {
                return string.IsNullOrWhiteSpace(manualEpgUrl)
                    ? "Manual mode is selected, but no manual XMLTV URL is saved."
                    : !string.IsNullOrWhiteSpace(detectedEpgUrl)
                        ? $"Manual override active. Detected URL is preserved separately. Active: {Trim(activeXmltvUrl, 120)}"
                        : $"Manual override active. Active: {Trim(activeXmltvUrl, 120)}";
            }

            if (!string.IsNullOrWhiteSpace(activeXmltvUrl)) return $"Detected XMLTV active. {Trim(activeXmltvUrl, 120)}";
            return sourceType == SourceType.M3U
                ? "No XMLTV URL was detected from the playlist header."
                : "Xtream XMLTV will be derived from provider credentials on the next sync.";
        }

        private static string FormatMatchBreakdown(string? matchBreakdown, int matchedCount, int unmatchedCount, int currentCoverageCount, int nextCoverageCount, int liveCount)
        {
            if (!string.IsNullOrWhiteSpace(matchBreakdown))
            {
                return matchBreakdown.Replace(";", " · ", StringComparison.Ordinal).Replace("_", " ", StringComparison.Ordinal);
            }

            return liveCount == 0
                ? string.Empty
                : $"{matchedCount}/{liveCount} matched, {unmatchedCount} unmatched. Current {currentCoverageCount}/{liveCount}, next {nextCoverageCount}/{liveCount}.";
        }

        private static string BuildFailureSummary(SourceSyncState? syncState, EpgStatus status, EpgSyncResultCode resultCode, EpgFailureStage failureStage, EpgSyncLog? epgLog)
        {
            if (syncState != null && syncState.HttpStatusCode >= 400)
            {
                return $"Import HTTP {syncState.HttpStatusCode}: {Trim(syncState.ErrorLog, 120)}";
            }

            if (status is not EpgStatus.FailedFetchOrParse and not EpgStatus.Stale)
            {
                return string.Empty;
            }

            var stageText = failureStage switch
            {
                EpgFailureStage.Fetch => "Guide fetch failed",
                EpgFailureStage.Parse => "Guide parse failed",
                EpgFailureStage.Persist => "Guide persist failed",
                _ => resultCode == EpgSyncResultCode.NoXmltvAdvertised ? "Guide unavailable" : "Guide sync failed"
            };

            return string.IsNullOrWhiteSpace(epgLog?.FailureReason)
                ? stageText
                : $"{stageText}: {Trim(epgLog.FailureReason, 120)}";
        }

        private static string BuildModeLabel(EpgActiveMode mode) => mode switch
        {
            EpgActiveMode.None => "No guide",
            EpgActiveMode.Manual => "Manual override",
            _ => "Detected from provider"
        };

        private static string FormatTimestamp(DateTime? timestampUtc)
        {
            if (!timestampUtc.HasValue) return "Never";
            var normalized = timestampUtc.Value.Kind == DateTimeKind.Utc
                ? timestampUtc.Value
                : DateTime.SpecifyKind(timestampUtc.Value, DateTimeKind.Utc);
            return normalized.ToLocalTime().ToString("g");
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength) + "...";
        }

        private sealed class SourceCredentialView
        {
            public int SourceProfileId { get; set; }
            public string DetectedEpgUrl { get; set; } = string.Empty;
            public string ManualEpgUrl { get; set; } = string.Empty;
            public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
        }
    }

    public sealed class SourceDiagnosticsSnapshot
    {
        public int SourceProfileId { get; set; }
        public SourceType SourceType { get; set; }
        public int LiveChannelCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public bool HasEpgUrl { get; set; }
        public bool HasDetectedEpgUrl { get; set; }
        public bool HasManualEpgUrl { get; set; }
        public bool HasActiveXmltvUrl { get; set; }
        public bool HasPersistedGuideData { get; set; }
        public int MatchedLiveChannelCount { get; set; }
        public int UnmatchedLiveChannelCount { get; set; }
        public int CurrentCoverageCount { get; set; }
        public int NextCoverageCount { get; set; }
        public int DuplicateCount { get; set; }
        public int InvalidStreamCount { get; set; }
        public int ChannelsWithLogoCount { get; set; }
        public int SuspiciousEntryCount { get; set; }
        public int HealthScore { get; set; }
        public int EpgProgramCount { get; set; }
        public int ImportWarningCount { get; set; }
        public int GuideWarningCount { get; set; }
        public string HealthLabel { get; set; } = "Saved";
        public string StatusSummary { get; set; } = "Saved source. No successful import recorded yet.";
        public string ImportResultText { get; set; } = "No successful import recorded.";
        public string ValidationResultText { get; set; } = string.Empty;
        public string EpgCoverageText { get; set; } = "Guide not synced.";
        public string EpgStatusText { get; set; } = "Guide not synced";
        public string EpgStatusSummary { get; set; } = "Guide has not synced yet.";
        public string EpgUrlSummaryText { get; set; } = string.Empty;
        public string MatchBreakdownText { get; set; } = string.Empty;
        public string WarningSummaryText { get; set; } = string.Empty;
        public string FailureSummaryText { get; set; } = string.Empty;
        public string LastSuccessfulSyncText { get; set; } = "Import Never - Guide Never";
        public string LastSyncAttemptText { get; set; } = "Never";
        public string LastImportSuccessText { get; set; } = "Never";
        public string LastEpgSuccessText { get; set; } = "Never";
        public bool EpgSyncSuccess { get; set; }
        public bool GuideAvailableForLive { get; set; }
        public bool IsPartialGuideMatch { get; set; }
        public EpgStatus EpgStatus { get; set; }
        public EpgSyncResultCode EpgResultCode { get; set; }
        public EpgFailureStage EpgFailureStage { get; set; }
        public EpgActiveMode ActiveEpgMode { get; set; } = EpgActiveMode.Detected;
        public string ActiveEpgModeText { get; set; } = "Detected from provider";
        public string DetectedEpgUrl { get; set; } = string.Empty;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string ActiveXmltvUrl { get; set; } = string.Empty;
        public IReadOnlyList<SourceDiagnosticsComponentSnapshot> HealthComponents { get; set; } = Array.Empty<SourceDiagnosticsComponentSnapshot>();
        public IReadOnlyList<SourceDiagnosticsProbeSnapshot> HealthProbes { get; set; } = Array.Empty<SourceDiagnosticsProbeSnapshot>();
        public IReadOnlyList<SourceDiagnosticsIssueSnapshot> Issues { get; set; } = Array.Empty<SourceDiagnosticsIssueSnapshot>();
    }

    public sealed class SourceDiagnosticsComponentSnapshot
    {
        public SourceHealthComponentType ComponentType { get; set; }
        public SourceHealthComponentState State { get; set; }
        public int Score { get; set; }
        public string Summary { get; set; } = string.Empty;
        public int RelevantCount { get; set; }
        public int HealthyCount { get; set; }
        public int IssueCount { get; set; }
    }

    public sealed class SourceDiagnosticsProbeSnapshot
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
    }

    public sealed class SourceDiagnosticsIssueSnapshot
    {
        public SourceHealthIssueSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int AffectedCount { get; set; }
        public string SampleItems { get; set; } = string.Empty;
    }
}
