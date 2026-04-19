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
        public async Task<IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>> GetSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds)
        {
            if (sourceIds.Count == 0)
            {
                return new Dictionary<int, SourceDiagnosticsSnapshot>();
            }

            var ids = sourceIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, SourceDiagnosticsSnapshot>();
            }

            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => ids.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id);

            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => ids.Contains(state.SourceProfileId))
                .ToDictionaryAsync(state => state.SourceProfileId);

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
                epgLogs = new Dictionary<int, EpgSyncLog>();
            }

            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(credential => ids.Contains(credential.SourceProfileId))
                .Select(credential => new
                {
                    credential.SourceProfileId,
                    credential.EpgUrl
                })
                .ToDictionaryAsync(
                    credential => credential.SourceProfileId,
                    credential => credential.EpgUrl ?? string.Empty);

            var liveCounts = await db.ChannelCategories
                .AsNoTracking()
                .Where(category => ids.Contains(category.SourceProfileId))
                .Join(
                    db.Channels.AsNoTracking(),
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => category.SourceProfileId)
                .GroupBy(sourceProfileId => sourceProfileId)
                .Select(group => new
                {
                    SourceProfileId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);

            var movieCounts = await db.Movies
                .AsNoTracking()
                .Where(movie => ids.Contains(movie.SourceProfileId))
                .GroupBy(movie => movie.SourceProfileId)
                .Select(group => new
                {
                    SourceProfileId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);

            var seriesCounts = await db.Series
                .AsNoTracking()
                .Where(series => ids.Contains(series.SourceProfileId))
                .GroupBy(series => series.SourceProfileId)
                .Select(group => new
                {
                    SourceProfileId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);

            var matchedLiveChannels = new Dictionary<int, int>();
            try
            {
                matchedLiveChannels = await db.ChannelCategories
                    .AsNoTracking()
                    .Where(category => ids.Contains(category.SourceProfileId))
                    .Join(
                        db.Channels.AsNoTracking(),
                        category => category.Id,
                        channel => channel.ChannelCategoryId,
                        (category, channel) => new
                        {
                            category.SourceProfileId,
                            ChannelId = channel.Id
                        })
                    .Join(
                        db.EpgPrograms
                            .AsNoTracking()
                            .Select(program => program.ChannelId)
                            .Distinct(),
                        channel => channel.ChannelId,
                        epgChannelId => epgChannelId,
                        (channel, epgChannelId) => channel.SourceProfileId)
                    .GroupBy(sourceProfileId => sourceProfileId)
                    .Select(group => new
                    {
                        SourceProfileId = group.Key,
                        Count = group.Count()
                    })
                    .ToDictionaryAsync(item => item.SourceProfileId, item => item.Count);
            }
            catch
            {
                matchedLiveChannels = new Dictionary<int, int>();
            }

            var snapshots = new Dictionary<int, SourceDiagnosticsSnapshot>(ids.Count);
            foreach (var sourceId in ids)
            {
                profiles.TryGetValue(sourceId, out var profile);
                syncStates.TryGetValue(sourceId, out var syncState);
                epgLogs.TryGetValue(sourceId, out var epgLog);

                var type = profile?.Type ?? SourceType.M3U;
                var liveCount = liveCounts.TryGetValue(sourceId, out var lc) ? lc : 0;
                var movieCount = movieCounts.TryGetValue(sourceId, out var mc) ? mc : 0;
                var seriesCount = seriesCounts.TryGetValue(sourceId, out var sc) ? sc : 0;
                var matchedCount = matchedLiveChannels.TryGetValue(sourceId, out var matchCount)
                    ? Math.Min(matchCount, liveCount)
                    : 0;
                var unmatchedCount = Math.Max(0, liveCount - matchedCount);
                var hasEpgUrl = credentials.TryGetValue(sourceId, out var epgUrl) && !string.IsNullOrWhiteSpace(epgUrl);
                var hasPersistedGuideData = matchedCount > 0 || (epgLog?.ProgrammeCount ?? 0) > 0;
                var hasAnyCatalog = liveCount + movieCount + seriesCount > 0;

                var importFailure = DetectImportFailure(syncState);
                var epgFailure = epgLog is { IsSuccess: false };

                var warnings = BuildWarnings(
                    type,
                    hasAnyCatalog,
                    liveCount,
                    matchedCount,
                    unmatchedCount,
                    hasEpgUrl,
                    hasPersistedGuideData,
                    syncState,
                    epgLog);

                var healthLabel = ComputeHealthLabel(
                    profile?.LastSync,
                    hasAnyCatalog,
                    importFailure,
                    epgFailure,
                    warnings.Count,
                    hasEpgUrl,
                    liveCount,
                    matchedCount);

                var status = BuildStatusSummary(
                    healthLabel,
                    profile?.LastSync,
                    importFailure,
                    epgFailure,
                    liveCount,
                    matchedCount,
                    hasEpgUrl);

                snapshots[sourceId] = new SourceDiagnosticsSnapshot(
                    sourceId,
                    type,
                    liveCount,
                    movieCount,
                    seriesCount,
                    hasEpgUrl,
                    hasPersistedGuideData,
                    matchedCount,
                    unmatchedCount,
                    epgLog?.ProgrammeCount ?? 0,
                    healthLabel,
                    status,
                    BuildImportResultText(profile?.LastSync, liveCount, movieCount, seriesCount, hasAnyCatalog),
                    BuildEpgCoverageText(hasEpgUrl, liveCount, matchedCount, unmatchedCount, epgLog),
                    BuildWarningSummary(warnings),
                    BuildFailureSummary(syncState, epgLog),
                    BuildLastSuccessfulSyncText(profile?.LastSync, epgLog, hasEpgUrl),
                    FormatTimestamp(profile?.LastSync),
                    epgLog != null && epgLog.IsSuccess ? FormatTimestamp(epgLog.SyncedAtUtc) : string.Empty,
                    epgLog?.IsSuccess ?? false);
            }

            return snapshots;
        }

        private static bool DetectImportFailure(SourceSyncState? syncState)
        {
            if (syncState == null || syncState.HttpStatusCode < 400)
            {
                return false;
            }

            return !syncState.ErrorLog.StartsWith("EPG", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> BuildWarnings(
            SourceType type,
            bool hasAnyCatalog,
            int liveCount,
            int matchedCount,
            int unmatchedCount,
            bool hasEpgUrl,
            bool hasPersistedGuideData,
            SourceSyncState? syncState,
            EpgSyncLog? epgLog)
        {
            var warnings = new List<string>();

            if (!hasAnyCatalog)
            {
                warnings.Add("import produced no catalog items");
            }

            if (liveCount == 0)
            {
                warnings.Add("no live channels imported");
            }

            if (hasEpgUrl && liveCount > 0)
            {
                if (matchedCount == 0 && hasPersistedGuideData)
                {
                    warnings.Add("guide data stored but no live channels matched");
                }
                else if (matchedCount > 0 && unmatchedCount > 0)
                {
                    warnings.Add($"{unmatchedCount} live channels unmatched");
                }
                else if (!hasPersistedGuideData && epgLog == null)
                {
                    warnings.Add("guide configured but not synced");
                }
            }

            if (syncState != null &&
                syncState.HttpStatusCode == 200 &&
                syncState.ErrorLog.Contains("suppressed", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("import applied source filtering");
            }

            if (type == SourceType.M3U && liveCount > 0 && matchedCount == 0 && hasEpgUrl)
            {
                warnings.Add("embedded or configured XMLTV has not covered live channels yet");
            }

            return warnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ComputeHealthLabel(
            DateTime? lastImportSuccessUtc,
            bool hasAnyCatalog,
            bool importFailure,
            bool epgFailure,
            int warningCount,
            bool hasEpgUrl,
            int liveCount,
            int matchedCount)
        {
            if (!lastImportSuccessUtc.HasValue)
            {
                return importFailure ? "Failing" : "Not synced";
            }

            if (importFailure || !hasAnyCatalog)
            {
                return "Failing";
            }

            if (epgFailure)
            {
                return "Degraded";
            }

            if (warningCount > 0)
            {
                return "Degraded";
            }

            if (hasEpgUrl && liveCount > 0 && matchedCount < liveCount)
            {
                return "Degraded";
            }

            return "Healthy";
        }

        private static string BuildStatusSummary(
            string healthLabel,
            DateTime? lastImportSuccessUtc,
            bool importFailure,
            bool epgFailure,
            int liveCount,
            int matchedCount,
            bool hasEpgUrl)
        {
            return healthLabel switch
            {
                "Healthy" => hasEpgUrl && liveCount > 0
                    ? $"Import stable. Guide matched {matchedCount} live channels."
                    : "Import stable. No active diagnostics issues.",
                "Degraded" when epgFailure => "Catalog is available, but guide sync is failing or incomplete.",
                "Degraded" when hasEpgUrl && liveCount > 0 && matchedCount < liveCount => "Catalog is available, but guide coverage is partial.",
                "Degraded" => "Catalog is available, but diagnostics found recoverable issues.",
                "Failing" when importFailure => "Latest source import attempt failed.",
                "Failing" => "Source has not produced a healthy catalog state.",
                _ => lastImportSuccessUtc.HasValue
                    ? "Import history is incomplete."
                    : "Source saved. No successful import recorded yet."
            };
        }

        private static string BuildImportResultText(
            DateTime? lastImportSuccessUtc,
            int liveCount,
            int movieCount,
            int seriesCount,
            bool hasAnyCatalog)
        {
            if (!lastImportSuccessUtc.HasValue)
            {
                return "No successful import recorded";
            }

            if (!hasAnyCatalog)
            {
                return $"Imported at {FormatTimestamp(lastImportSuccessUtc)} - no catalog items";
            }

            return $"Success - {liveCount} live, {movieCount} movies, {seriesCount} series";
        }

        private static string BuildEpgCoverageText(
            bool hasEpgUrl,
            int liveCount,
            int matchedCount,
            int unmatchedCount,
            EpgSyncLog? epgLog)
        {
            if (liveCount == 0)
            {
                return "No live channels available";
            }

            if (!hasEpgUrl && epgLog == null)
            {
                return "No EPG configured";
            }

            var coverage = liveCount == 0
                ? 0
                : (int)Math.Round((double)matchedCount / liveCount * 100, MidpointRounding.AwayFromZero);

            if (matchedCount == 0)
            {
                return $"0/{liveCount} matched - {unmatchedCount} unmatched";
            }

            return $"{coverage}% coverage - {matchedCount} matched / {unmatchedCount} unmatched";
        }

        private static string BuildWarningSummary(IReadOnlyList<string> warnings)
        {
            if (warnings.Count == 0)
            {
                return string.Empty;
            }

            var preview = string.Join(" - ", warnings.Take(2));
            return warnings.Count > 2
                ? $"{warnings.Count} warnings - {preview}"
                : $"{warnings.Count} warning{(warnings.Count == 1 ? string.Empty : "s")} - {preview}";
        }

        private static string BuildFailureSummary(SourceSyncState? syncState, EpgSyncLog? epgLog)
        {
            var failures = new List<(DateTime TimestampUtc, string Summary)>();

            if (syncState != null && syncState.HttpStatusCode >= 400)
            {
                var httpSummary = syncState.HttpStatusCode > 0
                    ? $"HTTP {syncState.HttpStatusCode}"
                    : "network failure";
                failures.Add((
                    syncState.LastAttempt,
                    $"{httpSummary} - {TrimSummary(syncState.ErrorLog, 84)}"));
            }

            if (epgLog is { IsSuccess: false } && !string.IsNullOrWhiteSpace(epgLog.FailureReason))
            {
                failures.Add((
                    epgLog.SyncedAtUtc,
                    $"EPG - {TrimSummary(epgLog.FailureReason, 84)}"));
            }

            return failures.Count == 0
                ? string.Empty
                : failures
                    .OrderByDescending(failure => failure.TimestampUtc)
                    .Select(failure => failure.Summary)
                    .First();
        }

        private static string BuildLastSuccessfulSyncText(
            DateTime? lastImportSuccessUtc,
            EpgSyncLog? epgLog,
            bool hasEpgUrl)
        {
            var importText = $"Import {FormatTimestamp(lastImportSuccessUtc)}";
            if (!hasEpgUrl && epgLog == null)
            {
                return importText;
            }

            var epgText = epgLog != null && epgLog.IsSuccess
                ? FormatTimestamp(epgLog.SyncedAtUtc)
                : "Never";

            return $"{importText} - EPG {epgText}";
        }

        private static string FormatTimestamp(DateTime? timestampUtc)
        {
            if (!timestampUtc.HasValue)
            {
                return "Never";
            }

            var normalized = timestampUtc.Value.Kind == DateTimeKind.Utc
                ? timestampUtc.Value
                : DateTime.SpecifyKind(timestampUtc.Value, DateTimeKind.Utc);

            return normalized.ToLocalTime().ToString("g");
        }

        private static string TrimSummary(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "No details recorded";
            }

            var trimmed = text.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed.Substring(0, maxLength) + "...";
        }
    }

    public sealed record SourceDiagnosticsSnapshot(
        int SourceProfileId,
        SourceType SourceType,
        int LiveChannelCount,
        int MovieCount,
        int SeriesCount,
        bool HasEpgUrl,
        bool HasPersistedGuideData,
        int MatchedLiveChannelCount,
        int UnmatchedLiveChannelCount,
        int EpgProgramCount,
        string HealthLabel,
        string StatusSummary,
        string ImportResultText,
        string EpgCoverageText,
        string WarningSummaryText,
        string FailureSummaryText,
        string LastSuccessfulSyncText,
        string LastImportSuccessText,
        string LastEpgSuccessText,
        bool EpgSyncSuccess);
}
