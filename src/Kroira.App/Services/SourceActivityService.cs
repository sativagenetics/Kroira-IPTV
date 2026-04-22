#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceActivityService
    {
        Task<IReadOnlyDictionary<int, SourceActivitySnapshot>> GetSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds,
            IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>? diagnostics = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class SourceActivityService : ISourceActivityService
    {
        private const int MaxTimelineItems = 7;
        private readonly ISourceDiagnosticsService _sourceDiagnosticsService;
        private readonly ISensitiveDataRedactionService _redactionService;

        public SourceActivityService(
            ISourceDiagnosticsService sourceDiagnosticsService,
            ISensitiveDataRedactionService redactionService)
        {
            _sourceDiagnosticsService = sourceDiagnosticsService;
            _redactionService = redactionService;
        }

        public async Task<IReadOnlyDictionary<int, SourceActivitySnapshot>> GetSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds,
            IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>? diagnostics = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(db);

            var ids = sourceIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, SourceActivitySnapshot>();
            }

            var snapshotLookup = diagnostics ?? await _sourceDiagnosticsService.GetSnapshotsAsync(db, ids);
            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => ids.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id, cancellationToken);
            var healthReports = await db.SourceHealthReports
                .AsNoTracking()
                .Where(report => ids.Contains(report.SourceProfileId))
                .ToDictionaryAsync(report => report.SourceProfileId, cancellationToken);
            var epgLogs = await db.EpgSyncLogs
                .AsNoTracking()
                .Where(item => ids.Contains(item.SourceProfileId))
                .ToDictionaryAsync(item => item.SourceProfileId, cancellationToken);
            var stalkerSnapshots = await db.StalkerPortalSnapshots
                .AsNoTracking()
                .Where(item => ids.Contains(item.SourceProfileId))
                .ToDictionaryAsync(item => item.SourceProfileId, cancellationToken);
            var acquisitionRuns = await db.SourceAcquisitionRuns
                .AsNoTracking()
                .Where(run => ids.Contains(run.SourceProfileId))
                .OrderByDescending(run => run.CompletedAtUtc ?? run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .ToListAsync(cancellationToken);
            var runIds = acquisitionRuns.Select(run => run.Id).ToList();
            var acquisitionEvidence = runIds.Count == 0
                ? new List<SourceAcquisitionEvidence>()
                : await db.SourceAcquisitionEvidence
                    .AsNoTracking()
                    .Where(item => runIds.Contains(item.SourceAcquisitionRunId) &&
                                   item.Outcome != SourceAcquisitionOutcome.Matched &&
                                   item.Outcome != SourceAcquisitionOutcome.Backfilled)
                    .OrderBy(item => item.SortOrder)
                    .ToListAsync(cancellationToken);
            var evidenceLookup = acquisitionEvidence
                .GroupBy(item => item.SourceAcquisitionRunId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<SourceAcquisitionEvidence>)group.ToList());
            var catchupAttempts = await db.CatchupPlaybackAttempts
                .AsNoTracking()
                .Where(item => ids.Contains(item.SourceProfileId))
                .OrderByDescending(item => item.RequestedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToListAsync(cancellationToken);

            var runsBySource = acquisitionRuns
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<SourceAcquisitionRun>)group.ToList());
            var attemptsBySource = catchupAttempts
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CatchupPlaybackAttempt>)group.ToList());

            var result = new Dictionary<int, SourceActivitySnapshot>(ids.Count);
            foreach (var id in ids)
            {
                if (!profiles.TryGetValue(id, out var profile))
                {
                    continue;
                }

                snapshotLookup.TryGetValue(id, out var diagnosticsSnapshot);
                diagnosticsSnapshot ??= new SourceDiagnosticsSnapshot
                {
                    SourceProfileId = id,
                    SourceType = profile.Type
                };

                healthReports.TryGetValue(id, out var healthReport);
                epgLogs.TryGetValue(id, out var epgLog);
                stalkerSnapshots.TryGetValue(id, out var stalkerSnapshot);
                runsBySource.TryGetValue(id, out var sourceRuns);
                sourceRuns ??= Array.Empty<SourceAcquisitionRun>();
                attemptsBySource.TryGetValue(id, out var sourceAttempts);
                sourceAttempts ??= Array.Empty<CatchupPlaybackAttempt>();

                result[id] = BuildSnapshot(
                    profile,
                    diagnosticsSnapshot,
                    healthReport,
                    epgLog,
                    stalkerSnapshot,
                    sourceRuns,
                    sourceAttempts,
                    evidenceLookup);
            }

            return result;
        }

        private SourceActivitySnapshot BuildSnapshot(
            SourceProfile profile,
            SourceDiagnosticsSnapshot diagnostics,
            SourceHealthReport? healthReport,
            EpgSyncLog? epgLog,
            StalkerPortalSnapshot? stalkerSnapshot,
            IReadOnlyList<SourceAcquisitionRun> runs,
            IReadOnlyList<CatchupPlaybackAttempt> attempts,
            IReadOnlyDictionary<int, IReadOnlyList<SourceAcquisitionEvidence>> evidenceLookup)
        {
            var latestRun = runs.FirstOrDefault();
            var previousRun = runs.Skip(1).FirstOrDefault();
            var lastSuccessfulRun = runs.FirstOrDefault(IsSuccessfulRun);
            var latestCatchupAttempt = attempts.FirstOrDefault();
            var currentFocus = ResolveCurrentFocus(diagnostics, latestRun, latestCatchupAttempt);
            var timeline = BuildTimeline(diagnostics, healthReport, epgLog, stalkerSnapshot, runs, attempts, evidenceLookup);

            var snapshot = new SourceActivitySnapshot
            {
                SourceProfileId = profile.Id,
                SourceName = profile.Name,
                SourceType = profile.Type,
                HeadlineText = BuildHeadline(diagnostics, latestRun, previousRun, latestCatchupAttempt),
                TrendText = BuildTrendText(diagnostics, latestRun, previousRun, lastSuccessfulRun, latestCatchupAttempt),
                CurrentStateText = BuildCurrentStateText(diagnostics),
                LatestAttemptText = BuildLatestAttemptText(latestRun, epgLog, latestCatchupAttempt),
                LastSuccessText = BuildLastSuccessText(lastSuccessfulRun),
                QuietStateText = timeline.Count == 0
                    ? "No source history has been recorded yet. Future syncs, guide refreshes, and playback attempts will show up here."
                    : string.Empty,
                Metrics = BuildMetrics(diagnostics, latestRun, lastSuccessfulRun, latestCatchupAttempt, currentFocus),
                Timeline = timeline
            };

            snapshot.SafeReportText = BuildSafeReport(snapshot, diagnostics, currentFocus);
            return snapshot;
        }

        private IReadOnlyList<SourceActivityMetric> BuildMetrics(
            SourceDiagnosticsSnapshot diagnostics,
            SourceAcquisitionRun? latestRun,
            SourceAcquisitionRun? lastSuccessfulRun,
            CatchupPlaybackAttempt? latestCatchupAttempt,
            ActivityFocus currentFocus)
        {
            var metrics = new List<SourceActivityMetric>
            {
                new()
                {
                    Label = "Latest attempt",
                    Value = BuildAttemptMetricValue(latestRun, latestCatchupAttempt),
                    Detail = BuildAttemptMetricDetail(latestRun, latestCatchupAttempt),
                    Tone = ResolveLatestAttemptTone(latestRun, latestCatchupAttempt)
                },
                new()
                {
                    Label = "Last success",
                    Value = lastSuccessfulRun == null ? "Never" : FormatLocalTimestamp(lastSuccessfulRun.CompletedAtUtc ?? lastSuccessfulRun.StartedAtUtc),
                    Detail = lastSuccessfulRun == null
                        ? "No completed source sync has succeeded yet."
                        : SanitizeText(BuildRunBadgeText(lastSuccessfulRun)),
                    Tone = lastSuccessfulRun == null ? SourceActivityTone.Neutral : SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Current health",
                    Value = string.IsNullOrWhiteSpace(diagnostics.HealthLabel) ? "Unknown" : diagnostics.HealthLabel,
                    Detail = diagnostics.HealthScore > 0
                        ? $"{diagnostics.HealthScore}/100 confidence"
                        : SanitizeText(diagnostics.ValidationResultText),
                    Tone = MapHealthTone(diagnostics.HealthLabel)
                },
                new()
                {
                    Label = "Current focus",
                    Value = currentFocus.Label,
                    Detail = currentFocus.Detail,
                    Tone = currentFocus.Tone
                }
            };

            return metrics;
        }

        private IReadOnlyList<SourceActivityTimelineItem> BuildTimeline(
            SourceDiagnosticsSnapshot diagnostics,
            SourceHealthReport? healthReport,
            EpgSyncLog? epgLog,
            StalkerPortalSnapshot? stalkerSnapshot,
            IReadOnlyList<SourceAcquisitionRun> runs,
            IReadOnlyList<CatchupPlaybackAttempt> attempts,
            IReadOnlyDictionary<int, IReadOnlyList<SourceAcquisitionEvidence>> evidenceLookup)
        {
            var items = new List<SourceActivityTimelineItem>();

            foreach (var run in runs.Take(3))
            {
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = run.CompletedAtUtc ?? run.StartedAtUtc,
                    Category = "Sync",
                    Title = BuildRunTitle(run),
                    Subtitle = SanitizeText($"{run.Trigger} - {run.Scope}"),
                    Detail = BuildRunDetail(run),
                    BadgeText = BuildRunBadgeText(run),
                    Tone = MapRunTone(run.Status)
                });

                if (evidenceLookup.TryGetValue(run.Id, out var evidence) && evidence.Count > 0)
                {
                    var signal = evidence
                        .OrderBy(GetEvidencePriority)
                        .ThenBy(item => item.SortOrder)
                        .FirstOrDefault();
                    if (signal != null)
                    {
                        items.Add(new SourceActivityTimelineItem
                        {
                            TimestampUtc = run.CompletedAtUtc ?? run.StartedAtUtc,
                            Category = BuildEvidenceCategory(signal.Stage),
                            Title = BuildEvidenceTitle(signal),
                            Subtitle = string.IsNullOrWhiteSpace(signal.RuleCode) ? string.Empty : SanitizeText(signal.RuleCode),
                            Detail = BuildEvidenceDetail(signal),
                            BadgeText = signal.Outcome.ToString(),
                            Tone = MapEvidenceTone(signal.Outcome)
                        });
                    }
                }
            }

            if (epgLog != null && epgLog.SyncedAtUtc > DateTime.MinValue)
            {
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = epgLog.SyncedAtUtc,
                    Category = "Guide",
                    Title = epgLog.IsSuccess ? "Guide sync is ready" : "Guide sync needs review",
                    Subtitle = SanitizeText(epgLog.ActiveMode.ToString()),
                    Detail = BuildGuideDetail(epgLog),
                    BadgeText = epgLog.Status.ToString(),
                    Tone = epgLog.IsSuccess ? SourceActivityTone.Healthy : MapGuideTone(epgLog.Status)
                });
            }

            if (healthReport != null)
            {
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = healthReport.EvaluatedAtUtc,
                    Category = "Health",
                    Title = $"Current health: {SanitizeText(diagnostics.HealthLabel)}",
                    Subtitle = healthReport.HealthScore > 0
                        ? $"{healthReport.HealthScore}/100 confidence"
                        : string.Empty,
                    Detail = BuildHealthDetail(diagnostics),
                    BadgeText = SanitizeText(diagnostics.HealthLabel),
                    Tone = MapHealthTone(diagnostics.HealthLabel)
                });
            }

            foreach (var attempt in attempts.Take(3))
            {
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = attempt.RequestedAtUtc,
                    Category = "Catchup",
                    Title = BuildCatchupTitle(attempt),
                    Subtitle = string.IsNullOrWhiteSpace(attempt.ProgramTitle)
                        ? string.Empty
                        : SanitizeText(attempt.ProgramTitle),
                    Detail = BuildCatchupDetail(attempt),
                    BadgeText = attempt.Status.ToString(),
                    Tone = MapCatchupTone(attempt.Status)
                });
            }

            var portalTimestamp = stalkerSnapshot?.LastProfileSyncAtUtc ?? stalkerSnapshot?.LastHandshakeAtUtc;
            if (stalkerSnapshot != null && portalTimestamp.HasValue && portalTimestamp.Value > DateTime.MinValue)
            {
                var hasPortalError = !string.IsNullOrWhiteSpace(stalkerSnapshot.LastError);
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = portalTimestamp.Value,
                    Category = "Portal",
                    Title = hasPortalError ? "Portal profile needs review" : "Portal profile discovered",
                    Subtitle = SanitizeText(stalkerSnapshot.PortalName),
                    Detail = hasPortalError
                        ? SanitizeText(stalkerSnapshot.LastError)
                        : SanitizeText(stalkerSnapshot.LastSummary),
                    BadgeText = hasPortalError ? "Issue" : "Ready",
                    Tone = hasPortalError ? SourceActivityTone.Failed : SourceActivityTone.Healthy
                });
            }

            if (ShouldAddRoutingItem(diagnostics))
            {
                items.Add(new SourceActivityTimelineItem
                {
                    TimestampUtc = ResolveRoutingTimestamp(runs, attempts, healthReport),
                    Category = "Routing",
                    Title = BuildRoutingTitle(diagnostics),
                    Subtitle = string.Empty,
                    Detail = BuildRoutingDetail(diagnostics),
                    BadgeText = BuildRoutingBadge(diagnostics),
                    Tone = BuildRoutingTone(diagnostics)
                });
            }

            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .OrderByDescending(item => item.TimestampUtc)
                .Take(MaxTimelineItems)
                .ToList();
        }

        private string BuildSafeReport(
            SourceActivitySnapshot snapshot,
            SourceDiagnosticsSnapshot diagnostics,
            ActivityFocus focus)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{snapshot.SourceName} [{snapshot.SourceType}]");
            AppendReportLine(builder, "Summary", snapshot.HeadlineText);
            AppendReportLine(builder, "Trend", snapshot.TrendText);
            AppendReportLine(builder, "Latest attempt", snapshot.LatestAttemptText);
            AppendReportLine(builder, "Last success", snapshot.LastSuccessText);
            AppendReportLine(builder, "Current state", snapshot.CurrentStateText);
            AppendReportLine(builder, "Current focus", $"{focus.Label}. {focus.Detail}");
            AppendReportLine(builder, "Guide", ComposeSummary(
                SanitizeText(diagnostics.EpgStatusText),
                SanitizeText(diagnostics.EpgUrlSummaryText)));
            AppendReportLine(builder, "Catchup", ComposeSummary(
                SanitizeText(diagnostics.CatchupStatusText),
                SanitizeText(diagnostics.CatchupLatestAttemptText)));
            AppendReportLine(builder, "Routing", ComposeSummary(
                SanitizeText(diagnostics.ProxyStatusText),
                SanitizeText(diagnostics.CompanionStatusText),
                SanitizeText(diagnostics.OperationalStatusText)));

            builder.AppendLine("Recent activity:");
            if (snapshot.Timeline.Count == 0)
            {
                builder.AppendLine("- No historical source activity recorded yet.");
            }
            else
            {
                foreach (var item in snapshot.Timeline)
                {
                    builder.Append("- ");
                    if (item.TimestampUtc > DateTime.MinValue)
                    {
                        builder.Append(FormatReportTimestamp(item.TimestampUtc));
                        builder.Append(" | ");
                    }

                    builder.Append(item.Title);
                    if (!string.IsNullOrWhiteSpace(item.BadgeText))
                    {
                        builder.Append(" | ");
                        builder.Append(item.BadgeText);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Detail))
                    {
                        builder.Append(" | ");
                        builder.Append(item.Detail);
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString().Trim();
        }

        private ActivityFocus ResolveCurrentFocus(
            SourceDiagnosticsSnapshot diagnostics,
            SourceAcquisitionRun? latestRun,
            CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText))
            {
                return new ActivityFocus("Portal", SanitizeText(diagnostics.StalkerPortalErrorText), SourceActivityTone.Failed);
            }

            if (latestRun != null && latestRun.Status == SourceAcquisitionRunStatus.Failed)
            {
                return new ActivityFocus(
                    "Sync",
                    SanitizeText(FirstNonEmpty(latestRun.Message, latestRun.GuideSummary, latestRun.ValidationSummary, latestRun.CatalogSummary, diagnostics.FailureSummaryText)),
                    SourceActivityTone.Failed);
            }

            if (latestRun != null && latestRun.Status == SourceAcquisitionRunStatus.Partial)
            {
                return new ActivityFocus(
                    "Sync",
                    SanitizeText(FirstNonEmpty(latestRun.Message, latestRun.ValidationSummary, diagnostics.WarningSummaryText, diagnostics.ValidationResultText)),
                    SourceActivityTone.Warning);
            }

            if (diagnostics.EpgStatus is EpgStatus.FailedFetchOrParse or EpgStatus.Stale)
            {
                return new ActivityFocus(
                    "Guide",
                    SanitizeText(FirstNonEmpty(diagnostics.EpgStatusSummary, diagnostics.EpgCoverageText, diagnostics.WarningSummaryText)),
                    diagnostics.EpgStatus == EpgStatus.FailedFetchOrParse ? SourceActivityTone.Failed : SourceActivityTone.Warning);
            }

            var failedProbe = diagnostics.HealthProbes.FirstOrDefault(item =>
                item.Status == SourceHealthProbeStatus.Completed &&
                (item.FailureCount > 0 || item.TimeoutCount > 0 || item.HttpErrorCount > 0 || item.TransportErrorCount > 0));
            if (failedProbe != null)
            {
                return new ActivityFocus("Probing", SanitizeText(failedProbe.Summary), SourceActivityTone.Warning);
            }

            if (latestCatchupAttempt != null && latestCatchupAttempt.Status != CatchupResolutionStatus.Resolved)
            {
                return new ActivityFocus(
                    "Catchup",
                    SanitizeText(FirstNonEmpty(latestCatchupAttempt.Message, latestCatchupAttempt.RoutingSummary)),
                    MapCatchupTone(latestCatchupAttempt.Status));
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.CompanionStatusText) &&
                !diagnostics.CompanionStatusText.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                return new ActivityFocus(
                    "Relay",
                    SanitizeText(FirstNonEmpty(diagnostics.CompanionStatusText, diagnostics.ProxyStatusText, diagnostics.OperationalStatusText)),
                    SourceActivityTone.Info);
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.WarningSummaryText) || !string.IsNullOrWhiteSpace(diagnostics.ValidationResultText))
            {
                return new ActivityFocus(
                    "Validation",
                    SanitizeText(FirstNonEmpty(diagnostics.WarningSummaryText, diagnostics.ValidationResultText)),
                    SourceActivityTone.Warning);
            }

            return new ActivityFocus("Stable", "No single issue is dominating this source right now.", SourceActivityTone.Healthy);
        }

        private string BuildHeadline(
            SourceDiagnosticsSnapshot diagnostics,
            SourceAcquisitionRun? latestRun,
            SourceAcquisitionRun? previousRun,
            CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText))
            {
                return "Portal connection needs review";
            }

            if (latestRun?.Status == SourceAcquisitionRunStatus.Failed)
            {
                return "Latest sync failed";
            }

            if (latestRun?.Status == SourceAcquisitionRunStatus.Partial)
            {
                return "Recent sync is degraded";
            }

            if (latestRun != null &&
                IsSuccessfulRun(latestRun) &&
                previousRun?.Status == SourceAcquisitionRunStatus.Failed)
            {
                return "Recovered on the latest sync";
            }

            if (MapHealthTone(diagnostics.HealthLabel) == SourceActivityTone.Failed)
            {
                return "Current source state needs attention";
            }

            if (latestCatchupAttempt != null &&
                latestCatchupAttempt.Status != CatchupResolutionStatus.Resolved &&
                latestRun != null &&
                IsSuccessfulRun(latestRun))
            {
                return "Sync is stable but playback edges are not";
            }

            if (latestRun != null && IsSuccessfulRun(latestRun))
            {
                return "Source activity is stable";
            }

            return "No sync history recorded yet";
        }

        private string BuildTrendText(
            SourceDiagnosticsSnapshot diagnostics,
            SourceAcquisitionRun? latestRun,
            SourceAcquisitionRun? previousRun,
            SourceAcquisitionRun? lastSuccessfulRun,
            CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (latestRun?.Status == SourceAcquisitionRunStatus.Failed && lastSuccessfulRun != null)
            {
                return $"Last full success was {FormatLocalTimestamp(lastSuccessfulRun.CompletedAtUtc ?? lastSuccessfulRun.StartedAtUtc)}. The latest refresh did not hold.";
            }

            if (latestRun != null &&
                IsSuccessfulRun(latestRun) &&
                previousRun?.Status == SourceAcquisitionRunStatus.Failed)
            {
                return "The latest refresh recovered after an earlier failed attempt.";
            }

            if (latestRun?.Status == SourceAcquisitionRunStatus.Partial)
            {
                return "The latest refresh completed with warnings, so the source remains usable but not clean.";
            }

            if (latestCatchupAttempt != null &&
                latestCatchupAttempt.Status != CatchupResolutionStatus.Resolved &&
                latestRun != null &&
                IsSuccessfulRun(latestRun))
            {
                return "Import and guide state are currently usable, but recent replay or start-over launches were not stable.";
            }

            if (MapHealthTone(diagnostics.HealthLabel) == SourceActivityTone.Warning ||
                MapHealthTone(diagnostics.HealthLabel) == SourceActivityTone.Failed)
            {
                return SanitizeText(FirstNonEmpty(
                    diagnostics.WarningSummaryText,
                    diagnostics.ValidationResultText,
                    diagnostics.StatusSummary));
            }

            if (latestRun != null && IsSuccessfulRun(latestRun))
            {
                return "Recent sync, guide, and validation signals are stable.";
            }

            return "KROIRA has not recorded a completed source sync for this source yet.";
        }

        private string BuildCurrentStateText(SourceDiagnosticsSnapshot diagnostics)
        {
            return ComposeSummary(
                $"Health {SanitizeText(diagnostics.HealthLabel)}",
                string.IsNullOrWhiteSpace(diagnostics.EpgStatusText)
                    ? string.Empty
                    : $"Guide {SanitizeText(diagnostics.EpgStatusText)}",
                SanitizeText(diagnostics.ProxyStatusText),
                SanitizeText(diagnostics.CompanionStatusText));
        }

        private string BuildLatestAttemptText(
            SourceAcquisitionRun? latestRun,
            EpgSyncLog? epgLog,
            CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            var candidates = new List<(DateTime TimestampUtc, string Text)>
            {
                latestRun == null
                    ? default
                    : (latestRun.CompletedAtUtc ?? latestRun.StartedAtUtc, $"{BuildRunBadgeText(latestRun)} at {FormatLocalTimestamp(latestRun.CompletedAtUtc ?? latestRun.StartedAtUtc)}"),
                epgLog == null || epgLog.SyncedAtUtc <= DateTime.MinValue
                    ? default
                    : (epgLog.SyncedAtUtc, $"{(epgLog.IsSuccess ? "Guide sync ready" : "Guide sync needs review")} at {FormatLocalTimestamp(epgLog.SyncedAtUtc)}"),
                latestCatchupAttempt == null
                    ? default
                    : (latestCatchupAttempt.RequestedAtUtc, $"{BuildCatchupTitle(latestCatchupAttempt)} at {FormatLocalTimestamp(latestCatchupAttempt.RequestedAtUtc)}")
            };

            var latest = candidates
                .Where(item => item.TimestampUtc > DateTime.MinValue && !string.IsNullOrWhiteSpace(item.Text))
                .OrderByDescending(item => item.TimestampUtc)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(latest.Text)
                ? "No source attempts recorded"
                : SanitizeText(latest.Text);
        }

        private string BuildLastSuccessText(SourceAcquisitionRun? lastSuccessfulRun)
        {
            if (lastSuccessfulRun == null)
            {
                return "No successful source sync recorded";
            }

            var completedAtUtc = lastSuccessfulRun.CompletedAtUtc ?? lastSuccessfulRun.StartedAtUtc;
            return $"{FormatLocalTimestamp(completedAtUtc)} - {BuildRunBadgeText(lastSuccessfulRun)}";
        }

        private string BuildRunTitle(SourceAcquisitionRun run)
        {
            return run.Status switch
            {
                SourceAcquisitionRunStatus.Succeeded => "Source sync completed",
                SourceAcquisitionRunStatus.Partial => "Source sync completed with issues",
                SourceAcquisitionRunStatus.Failed => "Source sync failed",
                SourceAcquisitionRunStatus.Backfilled => "Historical sync state backfilled",
                SourceAcquisitionRunStatus.Running => "Source sync is in progress",
                _ => "Source sync update"
            };
        }

        private string BuildRunBadgeText(SourceAcquisitionRun run)
        {
            return run.Status switch
            {
                SourceAcquisitionRunStatus.Succeeded => "Sync succeeded",
                SourceAcquisitionRunStatus.Partial => "Sync partial",
                SourceAcquisitionRunStatus.Failed => "Sync failed",
                SourceAcquisitionRunStatus.Backfilled => "Backfilled",
                SourceAcquisitionRunStatus.Running => "Running",
                _ => run.Status.ToString()
            };
        }

        private string BuildRunDetail(SourceAcquisitionRun run)
        {
            var counts = new List<string>();
            if (run.AcceptedCount > 0)
            {
                counts.Add($"{run.AcceptedCount:N0} accepted");
            }

            if (run.SuppressedCount > 0)
            {
                counts.Add($"{run.SuppressedCount:N0} suppressed");
            }

            if (run.UnmatchedCount > 0)
            {
                counts.Add($"{run.UnmatchedCount:N0} unmatched");
            }

            if (run.ProbeFailureCount > 0)
            {
                counts.Add($"{run.ProbeFailureCount:N0} probe failures");
            }

            return ComposeSummary(
                SanitizeText(FirstNonEmpty(
                    run.Message,
                    run.CatalogSummary,
                    run.GuideSummary,
                    run.ValidationSummary)),
                counts.Count == 0 ? string.Empty : string.Join(", ", counts),
                SanitizeText(FirstNonEmpty(run.RoutingSummary, run.ValidationRoutingSummary)));
        }

        private string BuildEvidenceTitle(SourceAcquisitionEvidence evidence)
        {
            return evidence.Stage switch
            {
                SourceAcquisitionStage.GuideMatch when evidence.Outcome == SourceAcquisitionOutcome.Unmatched => "Guide matching missed channels",
                SourceAcquisitionStage.GuideMatch => "Guide matching raised issues",
                SourceAcquisitionStage.Validation when evidence.Outcome == SourceAcquisitionOutcome.Failure => "Validation found unstable streams",
                SourceAcquisitionStage.Validation => "Validation raised warnings",
                SourceAcquisitionStage.RuntimeRepair => "Runtime repair recorded follow-up work",
                SourceAcquisitionStage.Acquire when evidence.Outcome == SourceAcquisitionOutcome.Suppressed => "Import filtered problematic rows",
                SourceAcquisitionStage.Acquire when evidence.Outcome == SourceAcquisitionOutcome.Demoted => "Import demoted unstable content",
                _ => "Sync evidence recorded"
            };
        }

        private string BuildEvidenceCategory(SourceAcquisitionStage stage)
        {
            return stage switch
            {
                SourceAcquisitionStage.Acquire => "Import",
                SourceAcquisitionStage.GuideMatch => "Guide",
                SourceAcquisitionStage.Validation => "Validation",
                SourceAcquisitionStage.RuntimeRepair => "Repair",
                _ => "Evidence"
            };
        }

        private string BuildEvidenceDetail(SourceAcquisitionEvidence evidence)
        {
            return ComposeSummary(
                SanitizeText(evidence.Reason),
                string.IsNullOrWhiteSpace(evidence.RawName)
                    ? string.Empty
                    : $"Item {SanitizeText(evidence.RawName)}",
                string.IsNullOrWhiteSpace(evidence.MatchedTarget)
                    ? string.Empty
                    : $"Matched {SanitizeText(evidence.MatchedTarget)}");
        }

        private string BuildGuideDetail(EpgSyncLog log)
        {
            var coverage = log.TotalLiveChannelCount > 0
                ? $"{log.MatchedChannelCount:N0}/{log.TotalLiveChannelCount:N0} mapped"
                : $"{log.ProgrammeCount:N0} programmes";
            return ComposeSummary(
                coverage,
                SanitizeText(log.MatchBreakdown),
                SanitizeText(log.FailureReason));
        }

        private string BuildHealthDetail(SourceDiagnosticsSnapshot diagnostics)
        {
            var probe = diagnostics.HealthProbes.FirstOrDefault(item =>
                item.Status == SourceHealthProbeStatus.Completed &&
                !string.IsNullOrWhiteSpace(item.Summary));
            var issue = diagnostics.Issues.FirstOrDefault();

            return ComposeSummary(
                SanitizeText(FirstNonEmpty(
                    diagnostics.ValidationResultText,
                    diagnostics.WarningSummaryText,
                    diagnostics.StatusSummary)),
                issue == null ? string.Empty : SanitizeText(issue.Message),
                probe == null ? string.Empty : SanitizeText(probe.Summary));
        }

        private string BuildCatchupTitle(CatchupPlaybackAttempt attempt)
        {
            var requestLabel = attempt.RequestKind switch
            {
                CatchupRequestKind.StartOver => "Start over",
                CatchupRequestKind.ReplayProgram => "Replay program",
                _ => "Catchup launch"
            };

            return attempt.Status == CatchupResolutionStatus.Resolved
                ? $"{requestLabel} resolved"
                : $"{requestLabel} needs review";
        }

        private string BuildCatchupDetail(CatchupPlaybackAttempt attempt)
        {
            return ComposeSummary(
                SanitizeText(attempt.Message),
                SanitizeText(attempt.RoutingSummary),
                attempt.WindowHours > 0 ? $"{attempt.WindowHours}h archive window" : string.Empty);
        }

        private bool ShouldAddRoutingItem(SourceDiagnosticsSnapshot diagnostics)
        {
            return (!string.IsNullOrWhiteSpace(diagnostics.ProxyStatusText) &&
                    !string.Equals(diagnostics.ProxyStatusText, "Direct routing", StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(diagnostics.CompanionStatusText) &&
                    !diagnostics.CompanionStatusText.Contains("off", StringComparison.OrdinalIgnoreCase)) ||
                   !string.IsNullOrWhiteSpace(diagnostics.OperationalStatusText);
        }

        private string BuildRoutingTitle(SourceDiagnosticsSnapshot diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.CompanionStatusText) &&
                !diagnostics.CompanionStatusText.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                return "Companion relay policy is active";
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.ProxyStatusText) &&
                !string.Equals(diagnostics.ProxyStatusText, "Direct routing", StringComparison.OrdinalIgnoreCase))
            {
                return "Proxy routing policy is active";
            }

            return "Operational routing state updated";
        }

        private string BuildRoutingDetail(SourceDiagnosticsSnapshot diagnostics)
        {
            return ComposeSummary(
                SanitizeText(diagnostics.ProxyStatusText),
                SanitizeText(diagnostics.CompanionStatusText),
                SanitizeText(diagnostics.OperationalStatusText));
        }

        private string BuildRoutingBadge(SourceDiagnosticsSnapshot diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.CompanionStatusText) &&
                !diagnostics.CompanionStatusText.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                return "Companion";
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.ProxyStatusText) &&
                !string.Equals(diagnostics.ProxyStatusText, "Direct routing", StringComparison.OrdinalIgnoreCase))
            {
                return "Proxy";
            }

            return "Routing";
        }

        private SourceActivityTone BuildRoutingTone(SourceDiagnosticsSnapshot diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(diagnostics.CompanionStatusText) &&
                diagnostics.CompanionStatusText.Contains("fallback", StringComparison.OrdinalIgnoreCase))
            {
                return SourceActivityTone.Warning;
            }

            return SourceActivityTone.Info;
        }

        private DateTime ResolveRoutingTimestamp(
            IReadOnlyList<SourceAcquisitionRun> runs,
            IReadOnlyList<CatchupPlaybackAttempt> attempts,
            SourceHealthReport? healthReport)
        {
            return runs.FirstOrDefault()?.CompletedAtUtc ??
                   runs.FirstOrDefault()?.StartedAtUtc ??
                   attempts.FirstOrDefault()?.RequestedAtUtc ??
                   healthReport?.EvaluatedAtUtc ??
                   DateTime.MinValue;
        }

        private string BuildAttemptMetricValue(SourceAcquisitionRun? latestRun, CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (latestRun != null)
            {
                return BuildRunBadgeText(latestRun);
            }

            return latestCatchupAttempt == null
                ? "Quiet"
                : SanitizeText(latestCatchupAttempt.Status.ToString());
        }

        private string BuildAttemptMetricDetail(SourceAcquisitionRun? latestRun, CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (latestRun != null)
            {
                return FormatLocalTimestamp(latestRun.CompletedAtUtc ?? latestRun.StartedAtUtc);
            }

            return latestCatchupAttempt == null
                ? "No recent activity"
                : FormatLocalTimestamp(latestCatchupAttempt.RequestedAtUtc);
        }

        private SourceActivityTone ResolveLatestAttemptTone(SourceAcquisitionRun? latestRun, CatchupPlaybackAttempt? latestCatchupAttempt)
        {
            if (latestRun != null)
            {
                return MapRunTone(latestRun.Status);
            }

            return latestCatchupAttempt == null
                ? SourceActivityTone.Neutral
                : MapCatchupTone(latestCatchupAttempt.Status);
        }

        private static bool IsSuccessfulRun(SourceAcquisitionRun run)
        {
            return run.Status is SourceAcquisitionRunStatus.Succeeded or SourceAcquisitionRunStatus.Backfilled;
        }

        private static int GetEvidencePriority(SourceAcquisitionEvidence evidence)
        {
            return evidence.Outcome switch
            {
                SourceAcquisitionOutcome.Failure => 0,
                SourceAcquisitionOutcome.Warning => 1,
                SourceAcquisitionOutcome.Unmatched => 2,
                SourceAcquisitionOutcome.Suppressed => 3,
                SourceAcquisitionOutcome.Demoted => 4,
                _ => 5
            };
        }

        private static SourceActivityTone MapRunTone(SourceAcquisitionRunStatus status)
        {
            return status switch
            {
                SourceAcquisitionRunStatus.Succeeded or SourceAcquisitionRunStatus.Backfilled => SourceActivityTone.Healthy,
                SourceAcquisitionRunStatus.Partial => SourceActivityTone.Warning,
                SourceAcquisitionRunStatus.Failed => SourceActivityTone.Failed,
                SourceAcquisitionRunStatus.Running => SourceActivityTone.Syncing,
                _ => SourceActivityTone.Neutral
            };
        }

        private static SourceActivityTone MapEvidenceTone(SourceAcquisitionOutcome outcome)
        {
            return outcome switch
            {
                SourceAcquisitionOutcome.Failure => SourceActivityTone.Failed,
                SourceAcquisitionOutcome.Warning or SourceAcquisitionOutcome.Unmatched or SourceAcquisitionOutcome.Suppressed or SourceAcquisitionOutcome.Demoted => SourceActivityTone.Warning,
                SourceAcquisitionOutcome.Matched => SourceActivityTone.Healthy,
                _ => SourceActivityTone.Info
            };
        }

        private static SourceActivityTone MapCatchupTone(CatchupResolutionStatus status)
        {
            return status switch
            {
                CatchupResolutionStatus.Resolved => SourceActivityTone.Healthy,
                CatchupResolutionStatus.Failed or CatchupResolutionStatus.InvalidStream or CatchupResolutionStatus.InvalidTemplate => SourceActivityTone.Failed,
                CatchupResolutionStatus.None => SourceActivityTone.Neutral,
                _ => SourceActivityTone.Warning
            };
        }

        private static SourceActivityTone MapHealthTone(string? healthLabel)
        {
            return healthLabel switch
            {
                "Healthy" or "Ready" => SourceActivityTone.Healthy,
                "Weak" or "Incomplete" or "Outdated" or "Attention" or "Degraded" => SourceActivityTone.Warning,
                "Working" => SourceActivityTone.Syncing,
                "Failing" or "Problematic" => SourceActivityTone.Failed,
                _ => SourceActivityTone.Neutral
            };
        }

        private static SourceActivityTone MapGuideTone(EpgStatus status)
        {
            return status switch
            {
                EpgStatus.Ready or EpgStatus.ManualOverride => SourceActivityTone.Healthy,
                EpgStatus.Syncing => SourceActivityTone.Syncing,
                EpgStatus.Stale => SourceActivityTone.Warning,
                EpgStatus.FailedFetchOrParse => SourceActivityTone.Failed,
                EpgStatus.UnavailableNoXmltv => SourceActivityTone.Info,
                _ => SourceActivityTone.Neutral
            };
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static string ComposeSummary(params string?[] segments)
        {
            return string.Join(" ", segments.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        }

        private static string FormatLocalTimestamp(DateTime utcValue)
        {
            return utcValue.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        private static string FormatReportTimestamp(DateTime utcValue)
        {
            return utcValue.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        private static void AppendReportLine(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(value.Trim());
        }

        private string SanitizeText(string? value, int maxLength = 320)
        {
            var redacted = _redactionService.RedactLooseText(value);
            if (string.IsNullOrWhiteSpace(redacted) || redacted.Length <= maxLength)
            {
                return redacted;
            }

            return redacted[..Math.Max(0, maxLength - 3)] + "...";
        }

        private sealed record ActivityFocus(string Label, string Detail, SourceActivityTone Tone);
    }
}
