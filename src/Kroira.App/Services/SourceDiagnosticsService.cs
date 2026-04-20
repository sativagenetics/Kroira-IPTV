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

                var guideUnavailable = IsGuideUnavailable(syncState, epgLog);
                var importFailure = DetectImportFailure(syncState, guideUnavailable);
                var epgFailure = epgLog is { IsSuccess: false } && !guideUnavailable;

                var importWarnings = BuildImportWarnings(
                    type,
                    hasAnyCatalog,
                    liveCount,
                    syncState);

                var guideWarnings = BuildGuideWarnings(
                    type,
                    liveCount,
                    matchedCount,
                    unmatchedCount,
                    hasEpgUrl,
                    hasPersistedGuideData,
                    guideUnavailable,
                    epgLog);

                var warnings = importWarnings
                    .Concat(guideWarnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var healthLabel = ComputeHealthLabel(
                    profile?.LastSync,
                    hasAnyCatalog,
                    importFailure,
                    epgFailure,
                    warnings.Count,
                    hasEpgUrl,
                    liveCount,
                    matchedCount);

                var primaryReason = BuildPrimaryReason(
                    healthLabel,
                    importFailure,
                    guideUnavailable,
                    epgFailure,
                    warnings,
                    syncState,
                    epgLog,
                    liveCount,
                    matchedCount,
                    unmatchedCount,
                    hasAnyCatalog,
                    hasEpgUrl);

                var status = BuildStatusSummary(
                    healthLabel,
                    profile?.LastSync,
                    primaryReason);

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
                    importWarnings.Count,
                    guideWarnings.Count,
                    healthLabel,
                    status,
                    BuildImportResultText(syncState, profile?.LastSync, liveCount, movieCount, seriesCount, hasAnyCatalog, guideUnavailable),
                    BuildEpgCoverageText(hasEpgUrl, liveCount, matchedCount, unmatchedCount, guideUnavailable, epgLog),
                    BuildWarningSummary(importWarnings.Count, guideWarnings.Count, warnings),
                    BuildFailureSummary(syncState, epgLog, guideUnavailable),
                    BuildLastSuccessfulSyncText(profile?.LastSync, epgLog, hasEpgUrl, guideUnavailable),
                    FormatTimestamp(profile?.LastSync),
                    epgLog != null && epgLog.IsSuccess ? FormatTimestamp(epgLog.SyncedAtUtc) : string.Empty,
                    epgLog?.IsSuccess ?? false);
            }

            return snapshots;
        }

        private static bool DetectImportFailure(SourceSyncState? syncState, bool guideUnavailable)
        {
            if (syncState == null || syncState.HttpStatusCode < 400 || guideUnavailable)
            {
                return false;
            }

            return !syncState.ErrorLog.StartsWith("EPG", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> BuildImportWarnings(
            SourceType type,
            bool hasAnyCatalog,
            int liveCount,
            SourceSyncState? syncState)
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

            if (syncState != null &&
                syncState.HttpStatusCode == 200 &&
                syncState.ErrorLog.Contains("suppressed", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("import applied source filtering");
            }

            if (type == SourceType.M3U &&
                syncState != null &&
                syncState.HttpStatusCode == 200 &&
                syncState.ErrorLog.Contains("LiveOnly mode", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("VOD import disabled by source mode");
            }

            return warnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> BuildGuideWarnings(
            SourceType type,
            int liveCount,
            int matchedCount,
            int unmatchedCount,
            bool hasEpgUrl,
            bool hasPersistedGuideData,
            bool guideUnavailable,
            EpgSyncLog? epgLog)
        {
            var warnings = new List<string>();

            if (guideUnavailable && liveCount > 0)
            {
                warnings.Add("Playlist does not advertise an XMLTV guide URL");
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
            string primaryReason)
        {
            return healthLabel switch
            {
                "Healthy" => string.IsNullOrWhiteSpace(primaryReason)
                    ? "Import stable. No active diagnostics issues."
                    : primaryReason,
                "Degraded" => primaryReason,
                "Failing" => primaryReason,
                _ => lastImportSuccessUtc.HasValue
                    ? "Import history is incomplete."
                    : "Source saved. No successful import recorded yet."
            };
        }

        private static string BuildImportResultText(
            SourceSyncState? syncState,
            DateTime? lastImportSuccessUtc,
            int liveCount,
            int movieCount,
            int seriesCount,
            bool hasAnyCatalog,
            bool guideUnavailable)
        {
            if (guideUnavailable && lastImportSuccessUtc.HasValue && hasAnyCatalog)
            {
                return $"Success - {liveCount} live, {movieCount} movies, {seriesCount} series";
            }

            if (syncState != null)
            {
                if (syncState.HttpStatusCode >= 400)
                {
                    var httpSummary = syncState.HttpStatusCode > 0
                        ? $"HTTP {syncState.HttpStatusCode}"
                        : "network failure";
                    return $"Failed - {httpSummary} - {TrimSummary(syncState.ErrorLog, 72)}";
                }

                if (!string.IsNullOrWhiteSpace(syncState.ErrorLog) &&
                    !syncState.ErrorLog.StartsWith("EPG", StringComparison.OrdinalIgnoreCase))
                {
                    return TrimSummary(syncState.ErrorLog, 96);
                }
            }

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
            bool guideUnavailable,
            EpgSyncLog? epgLog)
        {
            if (liveCount == 0)
            {
                return "No live channels available";
            }

            if (guideUnavailable)
            {
                return "Playlist does not advertise an XMLTV guide URL";
            }

            if (!hasEpgUrl && epgLog == null)
            {
                return "Guide unavailable unless your provider supplies an XMLTV URL";
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

        private static string BuildWarningSummary(
            int importWarningCount,
            int guideWarningCount,
            IReadOnlyList<string> warnings)
        {
            if (warnings.Count == 0)
            {
                return string.Empty;
            }

            var preview = string.Join(" - ", warnings.Take(2));
            var scope = importWarningCount > 0 && guideWarningCount > 0
                ? $"{importWarningCount} import/classification, {guideWarningCount} guide"
                : importWarningCount > 0
                    ? $"{importWarningCount} import/classification"
                    : $"{guideWarningCount} guide";

            return warnings.Count > 2
                ? $"{scope} warnings - {preview}"
                : $"{scope} warning{(warnings.Count == 1 ? string.Empty : "s")} - {preview}";
        }

        private static string BuildPrimaryReason(
            string healthLabel,
            bool importFailure,
            bool guideUnavailable,
            bool epgFailure,
            IReadOnlyList<string> warnings,
            SourceSyncState? syncState,
            EpgSyncLog? epgLog,
            int liveCount,
            int matchedCount,
            int unmatchedCount,
            bool hasAnyCatalog,
            bool hasEpgUrl)
        {
            if (importFailure)
            {
                var httpSummary = syncState?.HttpStatusCode > 0
                    ? $"Import failed with HTTP {syncState.HttpStatusCode}"
                    : "Latest import attempt failed";
                return $"{httpSummary}: {TrimSummary(syncState?.ErrorLog ?? string.Empty, 72)}";
            }

            if (guideUnavailable)
            {
                return "Guide unavailable unless your provider supplies an XMLTV URL";
            }

            if (!hasAnyCatalog)
            {
                return "No catalog items imported";
            }

            if (epgFailure)
            {
                return $"Guide sync failed: {TrimSummary(epgLog?.FailureReason ?? string.Empty, 72)}";
            }

            if (hasEpgUrl && liveCount > 0 && matchedCount < liveCount)
            {
                return matchedCount == 0
                    ? "Guide coverage is missing for all live channels"
                    : $"{unmatchedCount} live channels are still unmatched to guide data";
            }

            if (warnings.Count > 0)
            {
                return warnings[0];
            }

            return healthLabel switch
            {
                "Healthy" => hasEpgUrl && liveCount > 0
                    ? $"Guide matched {matchedCount} live channels"
                    : "Import stable",
                "Not synced" => "No successful import recorded yet",
                _ => string.Empty
            };
        }

        private static string BuildFailureSummary(SourceSyncState? syncState, EpgSyncLog? epgLog, bool guideUnavailable)
        {
            var failures = new List<(DateTime TimestampUtc, string Summary)>();

            if (!guideUnavailable && syncState != null && syncState.HttpStatusCode >= 400)
            {
                var httpSummary = syncState.HttpStatusCode > 0
                    ? $"HTTP {syncState.HttpStatusCode}"
                    : "network failure";
                failures.Add((
                    syncState.LastAttempt,
                    $"{httpSummary} - {TrimSummary(syncState.ErrorLog, 84)}"));
            }

            if (!guideUnavailable &&
                epgLog is { IsSuccess: false } &&
                !string.IsNullOrWhiteSpace(epgLog.FailureReason))
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
            bool hasEpgUrl,
            bool guideUnavailable)
        {
            var importText = $"Import {FormatTimestamp(lastImportSuccessUtc)}";
            if (guideUnavailable)
            {
                return $"{importText} - Guide unavailable";
            }

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

        private static bool IsGuideUnavailable(SourceSyncState? syncState, EpgSyncLog? epgLog)
        {
            return ContainsGuideUnavailableText(syncState?.ErrorLog) ||
                   ContainsGuideUnavailableText(epgLog?.FailureReason);
        }

        private static bool ContainsGuideUnavailableText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("does not advertise an XMLTV guide URL", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("No XMLTV EPG URL was configured or found", StringComparison.OrdinalIgnoreCase);
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
        int ImportWarningCount,
        int GuideWarningCount,
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
