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
    public interface IContentOperationalService
    {
        Task RefreshOperationalStateAsync(AppDbContext db);
        Task<OperationalPlaybackResolution?> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            IReadOnlyCollection<int>? excludedContentIds = null);
        Task MarkPlaybackSucceededAsync(AppDbContext db, PlaybackLaunchContext context);
        Task<OperationalPlaybackResolution?> MarkPlaybackFailedAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            string reason,
            IReadOnlyCollection<int>? excludedContentIds = null);
    }

    public sealed class ContentOperationalService : IContentOperationalService
    {
        private readonly ISourceRoutingService _sourceRoutingService;

        public ContentOperationalService(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public async Task RefreshOperationalStateAsync(AppDbContext db)
        {
            var liveCandidates = await LoadLiveCandidatesAsync(db);
            var movieCandidates = await LoadMovieCandidatesAsync(db);
            var sourceIds = liveCandidates.Select(item => item.SourceProfileId)
                .Concat(movieCandidates.Select(item => item.SourceProfileId))
                .Distinct()
                .ToList();
            var sourceSnapshots = await LoadSourceSnapshotsAsync(db, sourceIds);

            var candidateGroups = BuildCandidateGroups(liveCandidates, movieCandidates, sourceSnapshots);
            var existingStates = await db.LogicalOperationalStates
                .Include(state => state.Candidates)
                .ToListAsync();
            var existingLookup = existingStates.ToDictionary(
                state => BuildStateKey(state.ContentType, state.LogicalContentKey),
                state => state,
                StringComparer.OrdinalIgnoreCase);

            var nowUtc = DateTime.UtcNow;
            var touchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in candidateGroups)
            {
                var stateKey = BuildStateKey(group.ContentType, group.LogicalContentKey);
                touchedKeys.Add(stateKey);
                existingLookup.TryGetValue(stateKey, out var existingState);
                var selection = ChooseSelection(existingState, group.Candidates);

                var state = existingState ?? new LogicalOperationalState
                {
                    ContentType = group.ContentType,
                    LogicalContentKey = group.LogicalContentKey
                };

                if (existingState == null)
                {
                    db.LogicalOperationalStates.Add(state);
                }
                else if (state.Candidates.Count > 0)
                {
                    db.LogicalOperationalCandidates.RemoveRange(state.Candidates);
                    state.Candidates.Clear();
                }

                state.CandidateCount = group.Candidates.Count;
                state.PreferredContentId = selection.Preferred.ContentId;
                state.PreferredSourceProfileId = selection.Preferred.SourceProfileId;
                state.PreferredScore = selection.Preferred.Score;
                state.SelectionSummary = Trim(selection.SelectionSummary, 240);
                state.RecoveryAction = selection.RecoveryAction;
                state.RecoverySummary = Trim(selection.RecoverySummary, 240);
                state.SnapshotEvaluatedAtUtc = nowUtc;
                state.PreferredUpdatedAtUtc = nowUtc;

                var lastKnownGood = selection.LastKnownGood ?? selection.Preferred;
                state.LastKnownGoodContentId = lastKnownGood.ContentId;
                state.LastKnownGoodSourceProfileId = lastKnownGood.SourceProfileId;
                state.LastKnownGoodScore = lastKnownGood.Score;
                state.LastKnownGoodAtUtc ??= nowUtc;

                foreach (var candidate in group.Candidates)
                {
                    state.Candidates.Add(new LogicalOperationalCandidate
                    {
                        ContentId = candidate.ContentId,
                        SourceProfileId = candidate.SourceProfileId,
                        Rank = candidate.Rank,
                        Score = candidate.Score,
                        IsSelected = candidate.ContentId == selection.Preferred.ContentId,
                        IsLastKnownGood = candidate.ContentId == lastKnownGood.ContentId,
                        SupportsProxy = candidate.SupportsProxy,
                        SourceName = candidate.SourceName,
                        StreamUrl = candidate.StreamUrl,
                        Summary = Trim(candidate.Summary, 240),
                        LastSeenAtUtc = nowUtc
                    });
                }
            }

            var staleStates = existingStates
                .Where(state => !touchedKeys.Contains(BuildStateKey(state.ContentType, state.LogicalContentKey)))
                .ToList();
            if (staleStates.Count > 0)
            {
                db.LogicalOperationalStates.RemoveRange(staleStates);
            }

            await db.SaveChangesAsync();
        }

        public async Task<OperationalPlaybackResolution?> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            IReadOnlyCollection<int>? excludedContentIds = null)
        {
            if (context == null || !TryMapContentType(context.ContentType, out var contentType))
            {
                return null;
            }

            var logicalKey = await ResolveLogicalKeyAsync(db, context, contentType);
            if (string.IsNullOrWhiteSpace(logicalKey))
            {
                return null;
            }

            var state = await db.LogicalOperationalStates
                .Include(item => item.Candidates)
                .FirstOrDefaultAsync(item => item.ContentType == contentType && item.LogicalContentKey == logicalKey);
            if (state == null)
            {
                await RefreshOperationalStateAsync(db);
                state = await db.LogicalOperationalStates
                    .Include(item => item.Candidates)
                    .FirstOrDefaultAsync(item => item.ContentType == contentType && item.LogicalContentKey == logicalKey);
            }

            if (state == null || state.Candidates.Count == 0)
            {
                return null;
            }

            var excluded = excludedContentIds?.ToHashSet() ?? new HashSet<int>();
            var preferred = state.Candidates
                .Where(candidate => !excluded.Contains(candidate.ContentId))
                .OrderByDescending(candidate => candidate.IsSelected)
                .ThenBy(candidate => candidate.Rank)
                .FirstOrDefault();
            if (preferred == null)
            {
                return null;
            }

            if (state.PreferredContentId != preferred.ContentId)
            {
                state.PreferredContentId = preferred.ContentId;
                state.PreferredSourceProfileId = preferred.SourceProfileId;
                state.PreferredScore = preferred.Score;
                state.SelectionSummary = BuildSelectionSummary(preferred.SourceName, state.CandidateCount, preferred.Summary);
                state.PreferredUpdatedAtUtc = DateTime.UtcNow;

                foreach (var candidate in state.Candidates)
                {
                    candidate.IsSelected = candidate.ContentId == preferred.ContentId;
                }

                await db.SaveChangesAsync();
            }

            var credential = await db.SourceCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == preferred.SourceProfileId);
            var routing = _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Playback);

            context.ContentId = preferred.ContentId;
            context.LogicalContentKey = logicalKey;
            context.PreferredSourceProfileId = preferred.SourceProfileId;
            context.StreamUrl = preferred.StreamUrl;
            context.ProxyScope = routing.Scope;
            context.ProxyUrl = routing.UseProxy ? routing.ProxyUrl : string.Empty;
            context.RoutingSummary = routing.Summary;
            context.MirrorCandidateCount = state.CandidateCount;
            context.OperationalSummary = !string.IsNullOrWhiteSpace(state.RecoverySummary)
                ? state.RecoverySummary
                : state.SelectionSummary;

            return new OperationalPlaybackResolution
            {
                ContentType = contentType,
                ContentId = preferred.ContentId,
                SourceProfileId = preferred.SourceProfileId,
                LogicalContentKey = logicalKey,
                StreamUrl = preferred.StreamUrl,
                SourceName = preferred.SourceName,
                CandidateCount = state.CandidateCount,
                Score = preferred.Score,
                SelectionSummary = state.SelectionSummary,
                RecoverySummary = state.RecoverySummary,
                UsedLastKnownGood = preferred.IsLastKnownGood,
                Routing = routing
            };
        }

        public async Task MarkPlaybackSucceededAsync(AppDbContext db, PlaybackLaunchContext context)
        {
            if (context == null || !TryMapContentType(context.ContentType, out var contentType))
            {
                return;
            }

            var state = await db.LogicalOperationalStates
                .Include(item => item.Candidates)
                .FirstOrDefaultAsync(item => item.ContentType == contentType && item.LogicalContentKey == context.LogicalContentKey);
            if (state == null)
            {
                return;
            }

            var candidate = state.Candidates.FirstOrDefault(item => item.ContentId == context.ContentId)
                            ?? state.Candidates.FirstOrDefault(item => item.SourceProfileId == context.PreferredSourceProfileId);
            if (candidate == null)
            {
                return;
            }

            state.PreferredContentId = candidate.ContentId;
            state.PreferredSourceProfileId = candidate.SourceProfileId;
            state.PreferredScore = candidate.Score;
            state.SelectionSummary = BuildSelectionSummary(candidate.SourceName, state.CandidateCount, candidate.Summary);
            state.LastKnownGoodContentId = candidate.ContentId;
            state.LastKnownGoodSourceProfileId = candidate.SourceProfileId;
            state.LastKnownGoodScore = candidate.Score;
            state.LastKnownGoodAtUtc = DateTime.UtcNow;
            state.LastPlaybackSuccessAtUtc = DateTime.UtcNow;
            state.ConsecutivePlaybackFailures = 0;
            state.RecoveryAction = OperationalRecoveryAction.None;
            state.RecoverySummary = string.Empty;
            state.PreferredUpdatedAtUtc = DateTime.UtcNow;

            foreach (var item in state.Candidates)
            {
                item.IsSelected = item.ContentId == candidate.ContentId;
                item.IsLastKnownGood = item.ContentId == candidate.ContentId;
            }

            await db.SaveChangesAsync();
        }

        public async Task<OperationalPlaybackResolution?> MarkPlaybackFailedAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            string reason,
            IReadOnlyCollection<int>? excludedContentIds = null)
        {
            if (context == null || !TryMapContentType(context.ContentType, out var contentType))
            {
                return null;
            }

            var state = await db.LogicalOperationalStates
                .Include(item => item.Candidates)
                .FirstOrDefaultAsync(item => item.ContentType == contentType && item.LogicalContentKey == context.LogicalContentKey);
            if (state == null)
            {
                return null;
            }

            state.LastPlaybackFailureAtUtc = DateTime.UtcNow;
            state.ConsecutivePlaybackFailures++;

            var excluded = excludedContentIds?.ToHashSet() ?? new HashSet<int>();
            if (context.ContentId > 0)
            {
                excluded.Add(context.ContentId);
            }

            var current = state.Candidates.FirstOrDefault(item => item.ContentId == context.ContentId);
            var fallback = state.Candidates
                .Where(item => !excluded.Contains(item.ContentId))
                .OrderBy(item => item.Rank)
                .FirstOrDefault();

            if (fallback != null && (current == null || fallback.Score >= Math.Max(40, current.Score - 18)))
            {
                state.PreferredContentId = fallback.ContentId;
                state.PreferredSourceProfileId = fallback.SourceProfileId;
                state.PreferredScore = fallback.Score;
                state.PreferredUpdatedAtUtc = DateTime.UtcNow;
                state.RecoveryAction = OperationalRecoveryAction.SwitchedMirror;
                state.RecoverySummary = Trim(
                    $"Switched to backup mirror from {fallback.SourceName} after playback failure. {BuildFailureSummary(reason)}",
                    240);
                state.SelectionSummary = BuildSelectionSummary(fallback.SourceName, state.CandidateCount, fallback.Summary);

                foreach (var candidate in state.Candidates)
                {
                    candidate.IsSelected = candidate.ContentId == fallback.ContentId;
                }

                await db.SaveChangesAsync();

                var credential = await db.SourceCredentials
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.SourceProfileId == fallback.SourceProfileId);
                var routing = _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Playback);

                return new OperationalPlaybackResolution
                {
                    ContentType = contentType,
                    ContentId = fallback.ContentId,
                    SourceProfileId = fallback.SourceProfileId,
                    LogicalContentKey = state.LogicalContentKey,
                    StreamUrl = fallback.StreamUrl,
                    SourceName = fallback.SourceName,
                    CandidateCount = state.CandidateCount,
                    Score = fallback.Score,
                    SelectionSummary = state.SelectionSummary,
                    RecoverySummary = state.RecoverySummary,
                    UsedLastKnownGood = fallback.IsLastKnownGood,
                    Routing = routing
                };
            }

            state.RecoveryAction = OperationalRecoveryAction.Degraded;
            state.RecoverySummary = Trim(
                $"Current mirror failed and no stronger fallback was available. {BuildFailureSummary(reason)}",
                240);
            await db.SaveChangesAsync();
            return null;
        }

        private async Task<string> ResolveLogicalKeyAsync(AppDbContext db, PlaybackLaunchContext context, OperationalContentType contentType)
        {
            if (!string.IsNullOrWhiteSpace(context.LogicalContentKey))
            {
                return context.LogicalContentKey.Trim();
            }

            switch (contentType)
            {
                case OperationalContentType.Channel:
                {
                    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == context.ContentId);
                    return channel == null ? string.Empty : BuildChannelLogicalKey(channel);
                }
                case OperationalContentType.Movie:
                {
                    var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == context.ContentId);
                    return movie == null ? string.Empty : BuildMovieLogicalKey(movie);
                }
                default:
                    return string.Empty;
            }
        }

        private static bool TryMapContentType(PlaybackContentType contentType, out OperationalContentType operationalContentType)
        {
            switch (contentType)
            {
                case PlaybackContentType.Channel:
                    operationalContentType = OperationalContentType.Channel;
                    return true;
                case PlaybackContentType.Movie:
                    operationalContentType = OperationalContentType.Movie;
                    return true;
                default:
                    operationalContentType = OperationalContentType.Channel;
                    return false;
            }
        }

        private async Task<Dictionary<int, SourceOperationalSnapshot>> LoadSourceSnapshotsAsync(AppDbContext db, IReadOnlyCollection<int> sourceIds)
        {
            if (sourceIds.Count == 0)
            {
                return new Dictionary<int, SourceOperationalSnapshot>();
            }

            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(item => sourceIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id);
            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(item => sourceIds.Contains(item.SourceProfileId))
                .ToDictionaryAsync(item => item.SourceProfileId);
            var reports = await db.SourceHealthReports
                .AsNoTracking()
                .Where(item => sourceIds.Contains(item.SourceProfileId))
                .ToDictionaryAsync(item => item.SourceProfileId);
            var components = await db.SourceHealthComponents
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports.AsNoTracking().Where(item => sourceIds.Contains(item.SourceProfileId)),
                    component => component.SourceHealthReportId,
                    report => report.Id,
                    (component, report) => new { report.SourceProfileId, Component = component })
                .ToListAsync();
            var probes = await db.SourceHealthProbes
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports.AsNoTracking().Where(item => sourceIds.Contains(item.SourceProfileId)),
                    probe => probe.SourceHealthReportId,
                    report => report.Id,
                    (probe, report) => new { report.SourceProfileId, Probe = probe })
                .ToListAsync();

            var componentLookup = components
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(item => item.Component.ComponentType, item => item.Component));
            var probeLookup = probes
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(item => item.Probe.ProbeType, item => item.Probe));

            var snapshots = new Dictionary<int, SourceOperationalSnapshot>(sourceIds.Count);
            foreach (var sourceId in sourceIds)
            {
                profiles.TryGetValue(sourceId, out var profile);
                credentials.TryGetValue(sourceId, out var credential);
                reports.TryGetValue(sourceId, out var report);
                componentLookup.TryGetValue(sourceId, out var sourceComponents);
                probeLookup.TryGetValue(sourceId, out var sourceProbes);

                snapshots[sourceId] = new SourceOperationalSnapshot
                {
                    SourceId = sourceId,
                    SourceName = profile?.Name ?? $"Source {sourceId}",
                    Credential = credential,
                    HealthScore = report?.HealthScore ?? 45,
                    LiveComponentScore = GetComponentScore(sourceComponents, SourceHealthComponentType.Live),
                    VodComponentScore = GetComponentScore(sourceComponents, SourceHealthComponentType.Vod),
                    FreshnessScore = GetComponentScore(sourceComponents, SourceHealthComponentType.Freshness),
                    LiveProbeScore = GetProbeScore(sourceProbes, SourceHealthProbeType.Live),
                    VodProbeScore = GetProbeScore(sourceProbes, SourceHealthProbeType.Vod),
                    LastSuccessfulSyncAtUtc = report?.LastSuccessfulSyncAtUtc ?? profile?.LastSync,
                    Routing = _sourceRoutingService.Resolve(credential, SourceNetworkPurpose.Playback)
                };
            }

            return snapshots;
        }

        private async Task<List<LiveCandidateRow>> LoadLiveCandidatesAsync(AppDbContext db)
        {
            var rows = await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new { Channel = channel, SourceProfileId = category.SourceProfileId })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.SourceProfileId,
                    source => source.Id,
                    (item, source) => new { item.Channel, SourceId = source.Id, SourceName = source.Name })
                .ToListAsync();

            return rows
                .Select(item => new LiveCandidateRow
                {
                    ContentId = item.Channel.Id,
                    SourceProfileId = item.SourceId,
                    SourceName = item.SourceName,
                    LogicalKey = BuildChannelLogicalKey(item.Channel),
                    StreamUrl = item.Channel.StreamUrl,
                    HasGuide = !string.IsNullOrWhiteSpace(item.Channel.EpgChannelId),
                    HasLogo = !string.IsNullOrWhiteSpace(item.Channel.LogoUrl),
                    SupportsCatchup = item.Channel.SupportsCatchup,
                    EpgConfidence = item.Channel.EpgMatchConfidence,
                    LogoConfidence = item.Channel.LogoConfidence
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.LogicalKey) && !string.IsNullOrWhiteSpace(item.StreamUrl))
                .ToList();
        }

        private async Task<List<MovieCandidateRow>> LoadMovieCandidatesAsync(AppDbContext db)
        {
            var rows = await db.Movies
                .AsNoTracking()
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    movie => movie.SourceProfileId,
                    source => source.Id,
                    (movie, source) => new { Movie = movie, SourceId = source.Id, SourceName = source.Name })
                .ToListAsync();

            return rows
                .Select(item => new MovieCandidateRow
                {
                    ContentId = item.Movie.Id,
                    SourceProfileId = item.SourceId,
                    SourceName = item.SourceName,
                    LogicalKey = BuildMovieLogicalKey(item.Movie),
                    StreamUrl = item.Movie.StreamUrl,
                    HasPoster = !string.IsNullOrWhiteSpace(item.Movie.DisplayPosterUrl),
                    HasOverview = !string.IsNullOrWhiteSpace(item.Movie.Overview),
                    HasExternalMetadata = !string.IsNullOrWhiteSpace(item.Movie.TmdbId) || !string.IsNullOrWhiteSpace(item.Movie.ImdbId)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.LogicalKey) && !string.IsNullOrWhiteSpace(item.StreamUrl))
                .ToList();
        }

        private List<CandidateGroup> BuildCandidateGroups(
            IReadOnlyList<LiveCandidateRow> liveRows,
            IReadOnlyList<MovieCandidateRow> movieRows,
            IReadOnlyDictionary<int, SourceOperationalSnapshot> sourceSnapshots)
        {
            var groups = new List<CandidateGroup>();

            foreach (var group in liveRows.GroupBy(item => item.LogicalKey, StringComparer.OrdinalIgnoreCase))
            {
                var ranked = group
                    .Select(item => BuildLiveCandidate(item, sourceSnapshots.TryGetValue(item.SourceProfileId, out var snapshot) ? snapshot : SourceOperationalSnapshot.Empty(item.SourceProfileId, item.SourceName)))
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ContentId)
                    .ToList();
                ApplyRanks(ranked);
                groups.Add(new CandidateGroup(OperationalContentType.Channel, group.Key, ranked));
            }

            foreach (var group in movieRows.GroupBy(item => item.LogicalKey, StringComparer.OrdinalIgnoreCase))
            {
                var ranked = group
                    .Select(item => BuildMovieCandidate(item, sourceSnapshots.TryGetValue(item.SourceProfileId, out var snapshot) ? snapshot : SourceOperationalSnapshot.Empty(item.SourceProfileId, item.SourceName)))
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ContentId)
                    .ToList();
                ApplyRanks(ranked);
                groups.Add(new CandidateGroup(OperationalContentType.Movie, group.Key, ranked));
            }

            return groups;
        }

        private static CandidateScore BuildLiveCandidate(LiveCandidateRow row, SourceOperationalSnapshot source)
        {
            var itemSignal = 38;
            if (row.HasGuide) itemSignal += 22;
            if (row.HasLogo) itemSignal += 10;
            if (row.SupportsCatchup) itemSignal += 10;
            itemSignal += Math.Min(10, row.EpgConfidence / 10);
            itemSignal += Math.Min(10, row.LogoConfidence / 15);

            var score = WeightedScore(
                source.HealthScore,
                source.LiveComponentScore,
                source.FreshnessScore,
                source.LiveProbeScore,
                Math.Min(itemSignal, 100));

            return new CandidateScore(
                row.ContentId,
                row.SourceProfileId,
                row.SourceName,
                row.StreamUrl,
                score,
                BuildLiveSummary(row, source),
                source.Routing.UseProxy);
        }

        private static CandidateScore BuildMovieCandidate(MovieCandidateRow row, SourceOperationalSnapshot source)
        {
            var itemSignal = 42;
            if (row.HasPoster) itemSignal += 14;
            if (row.HasOverview) itemSignal += 14;
            if (row.HasExternalMetadata) itemSignal += 18;

            var score = WeightedScore(
                source.HealthScore,
                source.VodComponentScore,
                source.FreshnessScore,
                source.VodProbeScore,
                Math.Min(itemSignal, 100));

            return new CandidateScore(
                row.ContentId,
                row.SourceProfileId,
                row.SourceName,
                row.StreamUrl,
                score,
                BuildMovieSummary(row, source),
                source.Routing.UseProxy);
        }

        private static SelectionDecision ChooseSelection(LogicalOperationalState? existingState, IReadOnlyList<CandidateScore> candidates)
        {
            var top = candidates[0];
            var preferred = top;
            var recoveryAction = OperationalRecoveryAction.None;
            var recoverySummary = string.Empty;
            var lastKnownGood = MatchPersistedCandidate(existingState, candidates, lastKnownGood: true);
            var previousPreferred = MatchPersistedCandidate(existingState, candidates, lastKnownGood: false);

            if (lastKnownGood != null &&
                (existingState?.ConsecutivePlaybackFailures ?? 0) == 0 &&
                lastKnownGood.Score >= 60 &&
                lastKnownGood.Score >= top.Score + 12 &&
                top.Score < 60)
            {
                preferred = lastKnownGood;
                recoveryAction = OperationalRecoveryAction.PreservedLastKnownGood;
                recoverySummary = $"Holding {lastKnownGood.SourceName} as the last-known-good mirror while newer options look weaker.";
            }
            else if (previousPreferred != null &&
                     previousPreferred.Score >= 58 &&
                     previousPreferred.Score >= top.Score - 6)
            {
                preferred = previousPreferred;
            }
            else if (existingState != null &&
                     previousPreferred != null &&
                     previousPreferred.ContentId != top.ContentId)
            {
                recoveryAction = OperationalRecoveryAction.RolledForward;
                recoverySummary = $"Promoted {top.SourceName} as the stronger operational mirror after refresh.";
            }
            else if (top.Score < 45)
            {
                recoveryAction = OperationalRecoveryAction.Degraded;
                recoverySummary = "Only weak mirrors are currently available for this item.";
            }

            return new SelectionDecision(
                preferred,
                lastKnownGood ?? preferred,
                recoveryAction,
                Trim(recoverySummary, 240),
                BuildSelectionSummary(preferred.SourceName, candidates.Count, preferred.Summary));
        }

        private static CandidateScore? MatchPersistedCandidate(
            LogicalOperationalState? existingState,
            IReadOnlyList<CandidateScore> candidates,
            bool lastKnownGood)
        {
            if (existingState == null)
            {
                return null;
            }

            var targetContentId = lastKnownGood ? existingState.LastKnownGoodContentId : existingState.PreferredContentId;
            var targetSourceId = lastKnownGood ? existingState.LastKnownGoodSourceProfileId : existingState.PreferredSourceProfileId;

            return candidates.FirstOrDefault(item => item.ContentId == targetContentId)
                   ?? candidates
                       .Where(item => item.SourceProfileId == targetSourceId)
                       .OrderByDescending(item => item.Score)
                       .FirstOrDefault();
        }

        private static int WeightedScore(int healthScore, int componentScore, int freshnessScore, int probeScore, int itemSignal)
        {
            var weighted = healthScore * 0.25d +
                           componentScore * 0.35d +
                           freshnessScore * 0.15d +
                           probeScore * 0.15d +
                           itemSignal * 0.10d;
            return Math.Clamp((int)Math.Round(weighted, MidpointRounding.AwayFromZero), 0, 100);
        }

        private static string BuildSelectionSummary(string sourceName, int candidateCount, string candidateSummary)
        {
            if (candidateCount <= 1)
            {
                return string.IsNullOrWhiteSpace(candidateSummary)
                    ? $"Using {sourceName} as the current source."
                    : $"Using {sourceName}. {candidateSummary}";
            }

            return string.IsNullOrWhiteSpace(candidateSummary)
                ? $"Preferred {sourceName} from {candidateCount} mirrored candidates."
                : $"Preferred {sourceName} from {candidateCount} mirrored candidates. {candidateSummary}";
        }

        private static string BuildFailureSummary(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? "Playback could not be stabilized."
                : $"{reason.TrimEnd('.')}.".Replace("..", ".");
        }

        private static int GetComponentScore(
            IReadOnlyDictionary<SourceHealthComponentType, SourceHealthComponent>? components,
            SourceHealthComponentType componentType)
        {
            if (components == null || !components.TryGetValue(componentType, out var component))
            {
                return 50;
            }

            return component.State == SourceHealthComponentState.NotApplicable ? 60 : Math.Max(component.Score, 20);
        }

        private static int GetProbeScore(
            IReadOnlyDictionary<SourceHealthProbeType, SourceHealthProbe>? probes,
            SourceHealthProbeType probeType)
        {
            if (probes == null || !probes.TryGetValue(probeType, out var probe))
            {
                return 55;
            }

            if (probe.Status == SourceHealthProbeStatus.Skipped || probe.SampleSize <= 0)
            {
                return 50;
            }

            var ratio = probe.SuccessCount / (double)Math.Max(probe.SampleSize, 1);
            if (ratio >= 0.99d) return 100;
            if (ratio >= 0.75d) return 82;
            if (ratio >= 0.50d) return 62;
            if (ratio >= 0.25d) return 38;
            return 16;
        }

        private static void ApplyRanks(List<CandidateScore> candidates)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                candidates[index] = candidates[index] with { Rank = index + 1 };
            }
        }

        private static string BuildLiveSummary(LiveCandidateRow row, SourceOperationalSnapshot source)
        {
            var parts = new List<string>();
            if (source.LiveProbeScore >= 80) parts.Add("probe-backed");
            else if (source.LiveProbeScore <= 38) parts.Add("weak probe confidence");
            if (row.HasGuide) parts.Add("guide mapped");
            if (row.HasLogo) parts.Add("logo ready");
            if (row.SupportsCatchup) parts.Add("catchup");
            if (source.FreshnessScore <= 45) parts.Add("stale source");
            if (source.Routing.UseProxy) parts.Add("proxy routed");

            return parts.Count == 0
                ? "Operationally usable live mirror."
                : $"{UppercaseFirst(parts[0])}{(parts.Count > 1 ? $", {string.Join(", ", parts.Skip(1))}" : string.Empty)}.";
        }

        private static string BuildMovieSummary(MovieCandidateRow row, SourceOperationalSnapshot source)
        {
            var parts = new List<string>();
            if (source.VodProbeScore >= 80) parts.Add("probe-backed");
            else if (source.VodProbeScore <= 38) parts.Add("weak probe confidence");
            if (row.HasPoster) parts.Add("poster");
            if (row.HasOverview) parts.Add("overview");
            if (row.HasExternalMetadata) parts.Add("metadata");
            if (source.FreshnessScore <= 45) parts.Add("stale source");
            if (source.Routing.UseProxy) parts.Add("proxy routed");

            return parts.Count == 0
                ? "Operationally usable movie source."
                : $"{UppercaseFirst(parts[0])}{(parts.Count > 1 ? $", {string.Join(", ", parts.Skip(1))}" : string.Empty)}.";
        }

        private static string BuildChannelLogicalKey(Channel channel)
        {
            if (!string.IsNullOrWhiteSpace(channel.NormalizedIdentityKey))
            {
                return channel.NormalizedIdentityKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId))
            {
                return $"live:{channel.ProviderEpgChannelId.Trim().ToLowerInvariant()}";
            }

            return $"live:{NormalizeToken(channel.Name)}";
        }

        private static string BuildMovieLogicalKey(Movie movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.DedupFingerprint))
            {
                return movie.DedupFingerprint.Trim();
            }

            if (!string.IsNullOrWhiteSpace(movie.CanonicalTitleKey))
            {
                return $"movie:title:{movie.CanonicalTitleKey.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(movie.ExternalId))
            {
                return $"movie:external:{movie.SourceProfileId}:{movie.ExternalId.Trim()}";
            }

            return $"movie:raw:{movie.SourceProfileId}:{NormalizeToken(movie.Title)}";
        }

        private static string NormalizeToken(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : string.Concat(value
                    .Trim()
                    .ToLowerInvariant()
                    .Where(ch => char.IsLetterOrDigit(ch)));
        }

        private static string UppercaseFirst(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : char.ToUpperInvariant(value[0]) + value[1..];
        }

        private static string BuildStateKey(OperationalContentType contentType, string logicalKey)
        {
            return $"{(int)contentType}:{logicalKey}";
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
        }

        private sealed record CandidateGroup(
            OperationalContentType ContentType,
            string LogicalContentKey,
            List<CandidateScore> Candidates);

        private sealed record CandidateScore(
            int ContentId,
            int SourceProfileId,
            string SourceName,
            string StreamUrl,
            int Score,
            string Summary,
            bool SupportsProxy,
            int Rank = 0);

        private sealed record SelectionDecision(
            CandidateScore Preferred,
            CandidateScore? LastKnownGood,
            OperationalRecoveryAction RecoveryAction,
            string RecoverySummary,
            string SelectionSummary);

        private sealed class SourceOperationalSnapshot
        {
            public int SourceId { get; init; }
            public string SourceName { get; init; } = string.Empty;
            public SourceCredential? Credential { get; init; }
            public int HealthScore { get; init; } = 45;
            public int LiveComponentScore { get; init; } = 50;
            public int VodComponentScore { get; init; } = 50;
            public int FreshnessScore { get; init; } = 50;
            public int LiveProbeScore { get; init; } = 55;
            public int VodProbeScore { get; init; } = 55;
            public DateTime? LastSuccessfulSyncAtUtc { get; init; }
            public SourceRoutingDecision Routing { get; init; } = new();

            public static SourceOperationalSnapshot Empty(int sourceId, string sourceName)
            {
                return new SourceOperationalSnapshot
                {
                    SourceId = sourceId,
                    SourceName = sourceName
                };
            }
        }

        private sealed class LiveCandidateRow
        {
            public int ContentId { get; init; }
            public int SourceProfileId { get; init; }
            public string SourceName { get; init; } = string.Empty;
            public string LogicalKey { get; init; } = string.Empty;
            public string StreamUrl { get; init; } = string.Empty;
            public bool HasGuide { get; init; }
            public bool HasLogo { get; init; }
            public bool SupportsCatchup { get; init; }
            public int EpgConfidence { get; init; }
            public int LogoConfidence { get; init; }
        }

        private sealed class MovieCandidateRow
        {
            public int ContentId { get; init; }
            public int SourceProfileId { get; init; }
            public string SourceName { get; init; } = string.Empty;
            public string LogicalKey { get; init; } = string.Empty;
            public string StreamUrl { get; init; } = string.Empty;
            public bool HasPoster { get; init; }
            public bool HasOverview { get; init; }
            public bool HasExternalMetadata { get; init; }
        }
    }
}
