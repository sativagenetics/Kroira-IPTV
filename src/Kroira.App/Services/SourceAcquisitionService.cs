#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceAcquisitionService
    {
        Task<SourceAcquisitionSession> BeginSessionAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope);

        Task CompleteSessionAsync(
            AppDbContext db,
            SourceAcquisitionSession session,
            SourceAcquisitionRunStatus status,
            string message,
            string catalogSummary,
            string guideSummary,
            string validationSummary);

        Task BackfillAsync(
            AppDbContext db,
            IReadOnlyCollection<int>? sourceIds = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class SourceAcquisitionService : ISourceAcquisitionService
    {
        private const int MaxRunsPerSource = 6;
        private readonly ISourceRoutingService _sourceRoutingService;

        public SourceAcquisitionService(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public async Task<SourceAcquisitionSession> BeginSessionAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope)
        {
            var acquisitionProfile = await EnsureProfileAsync(db, profile, credential);
            var importPurpose = scope == SourceRefreshScope.EpgOnly
                ? SourceNetworkPurpose.Guide
                : SourceNetworkPurpose.Import;
            var importRouting = _sourceRoutingService.Resolve(credential, importPurpose);
            var validationRouting = _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Probe);

            var run = new SourceAcquisitionRun
            {
                SourceProfileId = profile.Id,
                Trigger = trigger,
                Scope = scope,
                Status = SourceAcquisitionRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow,
                ProfileKey = acquisitionProfile.ProfileKey,
                ProfileLabel = acquisitionProfile.ProfileLabel,
                ProviderKey = acquisitionProfile.ProviderKey,
                RoutingSummary = importRouting.Summary,
                ValidationRoutingSummary = validationRouting.Summary,
                Message = string.Empty,
                CatalogSummary = string.Empty,
                GuideSummary = string.Empty,
                ValidationSummary = string.Empty
            };

            db.SourceAcquisitionRuns.Add(run);
            await db.SaveChangesAsync();

            return new SourceAcquisitionSession(
                run.Id,
                profile.Id,
                acquisitionProfile,
                importRouting.Summary,
                validationRouting.Summary);
        }

        public async Task CompleteSessionAsync(
            AppDbContext db,
            SourceAcquisitionSession session,
            SourceAcquisitionRunStatus status,
            string message,
            string catalogSummary,
            string guideSummary,
            string validationSummary)
        {
            var run = await db.SourceAcquisitionRuns.FirstOrDefaultAsync(item => item.Id == session.RunId);
            if (run == null)
            {
                return;
            }

            if (session.DroppedEvidenceCount > 0)
            {
                session.RecordWarning(
                    SourceAcquisitionStage.Validation,
                    SourceAcquisitionItemKind.Source,
                    "telemetry.evidence_capped",
                    $"{session.DroppedEvidenceCount} lower-priority evidence item(s) were omitted to keep diagnostics bounded.");
            }

            run.Status = status;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Message = Trim(message, 420);
            run.CatalogSummary = Trim(catalogSummary, 280);
            run.GuideSummary = Trim(guideSummary, 280);
            run.ValidationSummary = Trim(
                string.IsNullOrWhiteSpace(validationSummary)
                    ? session.ValidationSummary
                    : validationSummary,
                320);
            run.RawItemCount = session.RawItemCount;
            run.AcceptedCount = session.AcceptedCount;
            run.SuppressedCount = session.SuppressedCount;
            run.DemotedCount = session.DemotedCount;
            run.MatchedCount = session.MatchedCount;
            run.UnmatchedCount = session.UnmatchedCount;
            run.LiveCount = session.LiveCount;
            run.MovieCount = session.MovieCount;
            run.SeriesCount = session.SeriesCount;
            run.EpisodeCount = session.EpisodeCount;
            run.AliasMatchCount = session.AliasMatchCount;
            run.RegexMatchCount = session.RegexMatchCount;
            run.FuzzyMatchCount = session.FuzzyMatchCount;
            run.ProbeSuccessCount = session.ProbeSuccessCount;
            run.ProbeFailureCount = session.ProbeFailureCount;
            run.WarningCount = session.WarningCount;
            run.ErrorCount = session.ErrorCount;

            if (session.Evidence.Count > 0)
            {
                db.SourceAcquisitionEvidence.AddRange(session.Evidence.Select(item => item.ToEntity()));
            }

            await db.SaveChangesAsync();
            await PruneRunsAsync(db, session.SourceProfileId);
        }

        public async Task BackfillAsync(
            AppDbContext db,
            IReadOnlyCollection<int>? sourceIds = null,
            CancellationToken cancellationToken = default)
        {
            var sourceProfiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => sourceIds == null || sourceIds.Count == 0 || sourceIds.Contains(profile.Id))
                .OrderBy(profile => profile.Id)
                .ToListAsync(cancellationToken);
            if (sourceProfiles.Count == 0)
            {
                return;
            }

            var ids = sourceProfiles.Select(profile => profile.Id).ToList();
            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(credential => ids.Contains(credential.SourceProfileId))
                .ToDictionaryAsync(credential => credential.SourceProfileId, cancellationToken);

            foreach (var profile in sourceProfiles)
            {
                credentials.TryGetValue(profile.Id, out var credential);
                await EnsureProfileAsync(db, profile, credential);
            }

            var existingRunSourceIds = await db.SourceAcquisitionRuns
                .AsNoTracking()
                .Where(run => ids.Contains(run.SourceProfileId))
                .Select(run => run.SourceProfileId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var existingRunSet = existingRunSourceIds.ToHashSet();

            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => ids.Contains(state.SourceProfileId))
                .ToDictionaryAsync(state => state.SourceProfileId, cancellationToken);
            var healthReports = await db.SourceHealthReports
                .AsNoTracking()
                .Where(report => ids.Contains(report.SourceProfileId))
                .ToDictionaryAsync(report => report.SourceProfileId, cancellationToken);
            var epgLogs = await db.EpgSyncLogs
                .AsNoTracking()
                .Where(log => ids.Contains(log.SourceProfileId))
                .ToDictionaryAsync(log => log.SourceProfileId, cancellationToken);

            var liveRows = await db.ChannelCategories
                .AsNoTracking()
                .Where(category => ids.Contains(category.SourceProfileId))
                .Join(
                    db.Channels.AsNoTracking(),
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => new
                    {
                        category.SourceProfileId,
                        channel.EpgMatchSource
                    })
                .ToListAsync(cancellationToken);
            var liveCountsBySource = liveRows
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.Count());
            var aliasMatchCounts = liveRows
                .Where(item => item.EpgMatchSource == ChannelEpgMatchSource.Alias)
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.Count());
            var regexMatchCounts = liveRows
                .Where(item => item.EpgMatchSource == ChannelEpgMatchSource.Regex)
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.Count());
            var fuzzyMatchCounts = liveRows
                .Where(item => item.EpgMatchSource == ChannelEpgMatchSource.Fuzzy)
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.Count());
            var movieCounts = await db.Movies
                .AsNoTracking()
                .Where(item => ids.Contains(item.SourceProfileId))
                .GroupBy(item => item.SourceProfileId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
            var seriesCounts = await db.Series
                .AsNoTracking()
                .Where(item => ids.Contains(item.SourceProfileId))
                .GroupBy(item => item.SourceProfileId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
            var episodeCounts = await db.Episodes
                .AsNoTracking()
                .Join(
                    db.Seasons.AsNoTracking(),
                    episode => episode.SeasonId,
                    season => season.Id,
                    (episode, season) => new { episode, season.SeriesId })
                .Join(
                    db.Series.AsNoTracking().Where(series => ids.Contains(series.SourceProfileId)),
                    item => item.SeriesId,
                    series => series.Id,
                    (item, series) => series.SourceProfileId)
                .GroupBy(sourceProfileId => sourceProfileId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);

            foreach (var profile in sourceProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (existingRunSet.Contains(profile.Id))
                {
                    continue;
                }

                var hasCatalog = liveCountsBySource.GetValueOrDefault(profile.Id) > 0 ||
                                 movieCounts.GetValueOrDefault(profile.Id) > 0 ||
                                 seriesCounts.GetValueOrDefault(profile.Id) > 0 ||
                                 profile.LastSync.HasValue;
                if (!hasCatalog && !syncStates.ContainsKey(profile.Id) && !healthReports.ContainsKey(profile.Id))
                {
                    continue;
                }

                credentials.TryGetValue(profile.Id, out var credential);
                var acquisitionProfile = await EnsureProfileAsync(db, profile, credential);
                var routingSummary = _sourceRoutingService.Resolve(
                    credential,
                    SourceNetworkPurpose.Import).Summary;
                var validationRoutingSummary = _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Probe).Summary;
                syncStates.TryGetValue(profile.Id, out var syncState);
                healthReports.TryGetValue(profile.Id, out var healthReport);
                epgLogs.TryGetValue(profile.Id, out var epgLog);

                var liveCount = liveCountsBySource.GetValueOrDefault(profile.Id);
                var movieCount = movieCounts.GetValueOrDefault(profile.Id);
                var seriesCount = seriesCounts.GetValueOrDefault(profile.Id);
                var episodeCount = episodeCounts.GetValueOrDefault(profile.Id);
                var matchedCount = epgLog?.MatchedChannelCount ?? 0;
                var unmatchedCount = epgLog?.UnmatchedChannelCount ?? Math.Max(0, liveCount - matchedCount);

                db.SourceAcquisitionRuns.Add(new SourceAcquisitionRun
                {
                    SourceProfileId = profile.Id,
                    Trigger = SourceRefreshTrigger.InitialImport,
                    Scope = SourceRefreshScope.Full,
                    Status = SourceAcquisitionRunStatus.Backfilled,
                    StartedAtUtc = profile.LastSync ?? syncState?.LastAttempt ?? DateTime.UtcNow,
                    CompletedAtUtc = profile.LastSync ?? syncState?.LastAttempt ?? DateTime.UtcNow,
                    ProfileKey = acquisitionProfile.ProfileKey,
                    ProfileLabel = acquisitionProfile.ProfileLabel,
                    ProviderKey = acquisitionProfile.ProviderKey,
                    RoutingSummary = routingSummary,
                    ValidationRoutingSummary = validationRoutingSummary,
                    Message = Trim(
                        string.IsNullOrWhiteSpace(syncState?.ErrorLog)
                            ? "Backfilled from persisted source state."
                            : syncState!.ErrorLog,
                        420),
                    CatalogSummary = Trim(healthReport?.ImportResultSummary ?? BuildCatalogSummary(profile.Type, liveCount, movieCount, seriesCount), 280),
                    GuideSummary = Trim(epgLog?.FailureReason ?? epgLog?.MatchBreakdown ?? string.Empty, 280),
                    ValidationSummary = Trim(healthReport?.ValidationSummary ?? "Validation will refresh on the next sync.", 320),
                    RawItemCount = liveCount + movieCount + seriesCount + episodeCount,
                    AcceptedCount = liveCount + movieCount + seriesCount + episodeCount,
                    SuppressedCount = 0,
                    DemotedCount = 0,
                    MatchedCount = matchedCount,
                    UnmatchedCount = unmatchedCount,
                    LiveCount = liveCount,
                    MovieCount = movieCount,
                    SeriesCount = seriesCount,
                    EpisodeCount = episodeCount,
                    AliasMatchCount = aliasMatchCounts.GetValueOrDefault(profile.Id),
                    RegexMatchCount = regexMatchCounts.GetValueOrDefault(profile.Id),
                    FuzzyMatchCount = fuzzyMatchCounts.GetValueOrDefault(profile.Id),
                    ProbeSuccessCount = healthReport?.ChannelsWithCurrentProgramCount ?? 0,
                    ProbeFailureCount = 0,
                    WarningCount = healthReport?.WarningCount ?? 0,
                    ErrorCount = healthReport?.ErrorCount ?? 0
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var sourceId in ids)
            {
                await PruneRunsAsync(db, sourceId, cancellationToken);
            }
        }

        private async Task<SourceAcquisitionProfile> EnsureProfileAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential)
        {
            var providerKey = DeriveProviderKey(profile, credential);
            var profileKey = profile.Type switch
            {
                SourceType.Xtream => "kroira.xtream.structured",
                SourceType.Stalker => "kroira.stalker.portal",
                _ => "kroira.m3u.balanced"
            };
            var profileLabel = profile.Type switch
            {
                SourceType.Xtream => "KROIRA Xtream Structured",
                SourceType.Stalker => "KROIRA Stalker Portal",
                _ => "KROIRA M3U Balanced"
            };
            var normalizationSummary = profile.Type switch
            {
                SourceType.Xtream => "Structured API titles and categories normalize through title cleanup while keeping provider values intact.",
                SourceType.Stalker => "Portal titles, genre ids, and command locators normalize into stable catalog identities while keeping provider fields intact.",
                _ => "Playlist titles, ids, and group labels normalize into stable identities while keeping provider values intact."
            };
            var matchingSummary = profile.Type switch
            {
                SourceType.Xtream => "Identifier-first upserts use normalized aliases, regex-safe guide matching, and bounded fuzzy fallback.",
                SourceType.Stalker => "Portal item ids stay durable while live channels reuse normalized aliases, regex-safe guide matching, and bounded fuzzy fallback.",
                _ => "Guide matching prefers provider ids, then normalized aliases, regex-safe aliases, and bounded fuzzy fallback."
            };
            var suppressionSummary = profile.Type switch
            {
                SourceType.Xtream => "Garbage titles, unsafe categories, missing live category bindings, and invalid stream extensions are suppressed.",
                SourceType.Stalker => "Broken portal rows, unsafe categories, missing commands, and series without usable episodes are suppressed without poisoning the source.",
                _ => "Provider buckets, promotional rows, garbage labels, and unsafe adult or bundle groups are suppressed."
            };
            var validationSummary = "Bounded live and VOD probing stays routing-aware and preserves last-known-good operational recovery paths.";
            var preferProxyDuringValidation = credential != null &&
                                              _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Probe).UseProxy;

            var acquisitionProfile = await db.SourceAcquisitionProfiles
                .FirstOrDefaultAsync(item => item.SourceProfileId == profile.Id);
            if (acquisitionProfile == null)
            {
                acquisitionProfile = new SourceAcquisitionProfile
                {
                    SourceProfileId = profile.Id
                };
                db.SourceAcquisitionProfiles.Add(acquisitionProfile);
            }

            acquisitionProfile.ProfileKey = profileKey;
            acquisitionProfile.ProfileLabel = profileLabel;
            acquisitionProfile.ProviderKey = providerKey;
            acquisitionProfile.NormalizationSummary = normalizationSummary;
            acquisitionProfile.MatchingSummary = matchingSummary;
            acquisitionProfile.SuppressionSummary = suppressionSummary;
            acquisitionProfile.ValidationSummary = validationSummary;
            acquisitionProfile.SupportsRegexMatching = true;
            acquisitionProfile.PreferProxyDuringValidation = preferProxyDuringValidation;
            acquisitionProfile.PreferLastKnownGoodRollback = true;
            acquisitionProfile.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return acquisitionProfile;
        }

        private async Task PruneRunsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken = default)
        {
            var staleRuns = await db.SourceAcquisitionRuns
                .Where(run => run.SourceProfileId == sourceProfileId)
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .Skip(MaxRunsPerSource)
                .ToListAsync(cancellationToken);
            if (staleRuns.Count == 0)
            {
                return;
            }

            db.SourceAcquisitionRuns.RemoveRange(staleRuns);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static string DeriveProviderKey(SourceProfile profile, SourceCredential? credential)
        {
            var primary = credential?.Url ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(primary))
            {
                if (Uri.TryCreate(primary, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        return host;
                    }
                }

                var fileName = Path.GetFileName(primary.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName.Trim().ToLowerInvariant();
                }

                return Trim(primary, 180).ToLowerInvariant();
            }

            return string.IsNullOrWhiteSpace(profile.Name)
                ? profile.Type.ToString().ToLowerInvariant()
                : Trim(profile.Name, 180).ToLowerInvariant();
        }

        private static string BuildCatalogSummary(SourceType sourceType, int liveCount, int movieCount, int seriesCount)
        {
            return sourceType switch
            {
                SourceType.Xtream => $"Xtream catalog state: {liveCount} live, {movieCount} movies, {seriesCount} series.",
                SourceType.Stalker => $"Stalker catalog state: {liveCount} live, {movieCount} movies, {seriesCount} series.",
                _ => $"Playlist catalog state: {liveCount} live, {movieCount} movies, {seriesCount} series."
            };
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
        }
    }

    public sealed class SourceAcquisitionSession
    {
        private const int MaxEvidenceItems = 240;
        private readonly List<SourceAcquisitionEvidenceDraft> _evidence = new();
        private int _sortOrder;

        internal SourceAcquisitionSession(
            int runId,
            int sourceProfileId,
            SourceAcquisitionProfile profile,
            string routingSummary,
            string validationRoutingSummary)
        {
            RunId = runId;
            SourceProfileId = sourceProfileId;
            Profile = profile;
            RoutingSummary = routingSummary;
            ValidationRoutingSummary = validationRoutingSummary;
        }

        public int RunId { get; }
        public int SourceProfileId { get; }
        public SourceAcquisitionProfile Profile { get; }
        public string RoutingSummary { get; }
        public string ValidationRoutingSummary { get; }
        public string ValidationSummary { get; private set; } = string.Empty;
        public int RawItemCount { get; private set; }
        public int AcceptedCount { get; private set; }
        public int SuppressedCount { get; private set; }
        public int DemotedCount { get; private set; }
        public int MatchedCount { get; private set; }
        public int UnmatchedCount { get; private set; }
        public int LiveCount { get; private set; }
        public int MovieCount { get; private set; }
        public int SeriesCount { get; private set; }
        public int EpisodeCount { get; private set; }
        public int AliasMatchCount { get; private set; }
        public int RegexMatchCount { get; private set; }
        public int FuzzyMatchCount { get; private set; }
        public int ProbeSuccessCount { get; private set; }
        public int ProbeFailureCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int DroppedEvidenceCount { get; private set; }
        internal IReadOnlyList<SourceAcquisitionEvidenceDraft> Evidence => _evidence;

        public void RegisterRawItems(int count)
        {
            if (count > 0)
            {
                RawItemCount += count;
            }
        }

        public void RegisterAccepted(SourceAcquisitionItemKind itemKind, int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            AcceptedCount += count;
            switch (itemKind)
            {
                case SourceAcquisitionItemKind.LiveChannel:
                    LiveCount += count;
                    break;
                case SourceAcquisitionItemKind.Movie:
                    MovieCount += count;
                    break;
                case SourceAcquisitionItemKind.Series:
                    SeriesCount += count;
                    break;
                case SourceAcquisitionItemKind.Episode:
                    EpisodeCount += count;
                    break;
            }
        }

        public void RecordSuppressed(
            SourceAcquisitionItemKind itemKind,
            string ruleCode,
            string reason,
            string rawName,
            string rawCategory,
            string normalizedName = "",
            string normalizedCategory = "")
        {
            SuppressedCount++;
            WarningCount++;
            AddEvidence(
                SourceAcquisitionStage.Acquire,
                SourceAcquisitionOutcome.Suppressed,
                itemKind,
                ruleCode,
                reason,
                rawName,
                rawCategory,
                normalizedName,
                normalizedCategory);
        }

        public void RecordDemotion(
            string ruleCode,
            string reason,
            string rawName,
            string rawCategory,
            string normalizedName,
            string normalizedCategory)
        {
            DemotedCount++;
            WarningCount++;
            AddEvidence(
                SourceAcquisitionStage.Acquire,
                SourceAcquisitionOutcome.Demoted,
                SourceAcquisitionItemKind.Episode,
                ruleCode,
                reason,
                rawName,
                rawCategory,
                normalizedName,
                normalizedCategory);
        }

        public void RecordGuideMatch(
            SourceAcquisitionItemKind itemKind,
            ChannelEpgMatchSource matchSource,
            int confidence,
            string ruleCode,
            string reason,
            string rawName,
            string normalizedName,
            string identityKey,
            string aliasKeys,
            string matchedValue,
            string matchedTarget,
            bool captureDetail = true)
        {
            MatchedCount++;
            switch (matchSource)
            {
                case ChannelEpgMatchSource.Alias:
                    AliasMatchCount++;
                    break;
                case ChannelEpgMatchSource.Regex:
                    RegexMatchCount++;
                    break;
                case ChannelEpgMatchSource.Fuzzy:
                    FuzzyMatchCount++;
                    break;
            }

            if (!captureDetail)
            {
                return;
            }

            AddEvidence(
                SourceAcquisitionStage.GuideMatch,
                SourceAcquisitionOutcome.Matched,
                itemKind,
                ruleCode,
                reason,
                rawName,
                string.Empty,
                normalizedName,
                string.Empty,
                identityKey,
                aliasKeys,
                matchedValue,
                matchedTarget,
                confidence);
        }

        public void RecordGuideUnmatched(
            string ruleCode,
            string reason,
            string rawName,
            string normalizedName,
            string identityKey,
            string aliasKeys)
        {
            UnmatchedCount++;
            WarningCount++;
            AddEvidence(
                SourceAcquisitionStage.GuideMatch,
                SourceAcquisitionOutcome.Unmatched,
                SourceAcquisitionItemKind.LiveChannel,
                ruleCode,
                reason,
                rawName,
                string.Empty,
                normalizedName,
                string.Empty,
                identityKey,
                aliasKeys);
        }

        public void RecordValidationProbe(
            SourceHealthProbeType probeType,
            SourceHealthProbeStatus status,
            int successCount,
            int failureCount,
            string summary)
        {
            ProbeSuccessCount += Math.Max(0, successCount);
            ProbeFailureCount += Math.Max(0, failureCount);

            if (failureCount > 0)
            {
                if (successCount == 0)
                {
                    ErrorCount++;
                }
                else
                {
                    WarningCount++;
                }
            }

            AddEvidence(
                SourceAcquisitionStage.Validation,
                failureCount > 0 ? SourceAcquisitionOutcome.Warning : SourceAcquisitionOutcome.Matched,
                SourceAcquisitionItemKind.Probe,
                $"validation.probe.{probeType.ToString().ToLowerInvariant()}",
                summary,
                probeType.ToString(),
                status.ToString(),
                confidence: successCount > 0 && failureCount == 0 ? 100 : Math.Max(0, 100 - failureCount * 25));
        }

        public void RecordValidationIssue(
            SourceHealthIssueSeverity severity,
            string code,
            string title,
            string message,
            int confidence = 0)
        {
            if (severity == SourceHealthIssueSeverity.Error)
            {
                ErrorCount++;
            }
            else if (severity == SourceHealthIssueSeverity.Warning)
            {
                WarningCount++;
            }

            AddEvidence(
                SourceAcquisitionStage.Validation,
                severity == SourceHealthIssueSeverity.Error
                    ? SourceAcquisitionOutcome.Failure
                    : SourceAcquisitionOutcome.Warning,
                SourceAcquisitionItemKind.Source,
                string.IsNullOrWhiteSpace(code) ? "validation.issue" : code,
                message,
                title,
                string.Empty,
                confidence: confidence);
        }

        public void RecordRuntimeRepairWarning(
            string ruleCode,
            string reason)
        {
            WarningCount++;
            AddEvidence(
                SourceAcquisitionStage.RuntimeRepair,
                SourceAcquisitionOutcome.Warning,
                SourceAcquisitionItemKind.Source,
                ruleCode,
                reason,
                "Runtime repair");
        }

        public void RecordFailure(
            SourceAcquisitionStage stage,
            SourceAcquisitionItemKind itemKind,
            string ruleCode,
            string reason)
        {
            ErrorCount++;
            AddEvidence(
                stage,
                SourceAcquisitionOutcome.Failure,
                itemKind,
                ruleCode,
                reason,
                string.Empty);
        }

        public void RecordWarning(
            SourceAcquisitionStage stage,
            SourceAcquisitionItemKind itemKind,
            string ruleCode,
            string reason)
        {
            WarningCount++;
            AddEvidence(
                stage,
                SourceAcquisitionOutcome.Warning,
                itemKind,
                ruleCode,
                reason,
                string.Empty);
        }

        public void SetValidationSummary(string summary)
        {
            ValidationSummary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
        }

        private void AddEvidence(
            SourceAcquisitionStage stage,
            SourceAcquisitionOutcome outcome,
            SourceAcquisitionItemKind itemKind,
            string ruleCode,
            string reason,
            string rawName,
            string rawCategory = "",
            string normalizedName = "",
            string normalizedCategory = "",
            string identityKey = "",
            string aliasKeys = "",
            string matchedValue = "",
            string matchedTarget = "",
            int confidence = 0)
        {
            if (_evidence.Count >= MaxEvidenceItems)
            {
                DroppedEvidenceCount++;
                return;
            }

            _evidence.Add(new SourceAcquisitionEvidenceDraft
            {
                SourceAcquisitionRunId = RunId,
                SourceProfileId = SourceProfileId,
                Stage = stage,
                Outcome = outcome,
                ItemKind = itemKind,
                RuleCode = ruleCode,
                Reason = reason,
                RawName = rawName,
                RawCategory = rawCategory,
                NormalizedName = normalizedName,
                NormalizedCategory = normalizedCategory,
                IdentityKey = identityKey,
                AliasKeys = aliasKeys,
                MatchedValue = matchedValue,
                MatchedTarget = matchedTarget,
                Confidence = confidence,
                SortOrder = ++_sortOrder
            });
        }
    }

    internal sealed class SourceAcquisitionEvidenceDraft
    {
        public int SourceAcquisitionRunId { get; init; }
        public int SourceProfileId { get; init; }
        public SourceAcquisitionStage Stage { get; init; }
        public SourceAcquisitionOutcome Outcome { get; init; }
        public SourceAcquisitionItemKind ItemKind { get; init; }
        public string RuleCode { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string RawName { get; init; } = string.Empty;
        public string RawCategory { get; init; } = string.Empty;
        public string NormalizedName { get; init; } = string.Empty;
        public string NormalizedCategory { get; init; } = string.Empty;
        public string IdentityKey { get; init; } = string.Empty;
        public string AliasKeys { get; init; } = string.Empty;
        public string MatchedValue { get; init; } = string.Empty;
        public string MatchedTarget { get; init; } = string.Empty;
        public int Confidence { get; init; }
        public int SortOrder { get; init; }

        public SourceAcquisitionEvidence ToEntity()
        {
            return new SourceAcquisitionEvidence
            {
                SourceAcquisitionRunId = SourceAcquisitionRunId,
                SourceProfileId = SourceProfileId,
                Stage = Stage,
                Outcome = Outcome,
                ItemKind = ItemKind,
                RuleCode = Trim(RuleCode, 96),
                Reason = Trim(Reason, 320),
                RawName = Trim(RawName, 220),
                RawCategory = Trim(RawCategory, 220),
                NormalizedName = Trim(NormalizedName, 220),
                NormalizedCategory = Trim(NormalizedCategory, 220),
                IdentityKey = Trim(IdentityKey, 220),
                AliasKeys = Trim(AliasKeys, 1600),
                MatchedValue = Trim(MatchedValue, 220),
                MatchedTarget = Trim(MatchedTarget, 220),
                Confidence = Confidence,
                SortOrder = SortOrder
            };
        }

        private static string Trim(string value, int maxLength)
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
