#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface IEpgCoverageReportService
    {
        Task<EpgCoverageReport> BuildReportAsync(AppDbContext db);
    }

    public sealed class EpgCoverageReportService : IEpgCoverageReportService
    {
        public async Task<EpgCoverageReport> BuildReportAsync(AppDbContext db)
        {
            var sources = await db.SourceProfiles.AsNoTracking()
                .OrderBy(source => source.Name)
                .ToListAsync();
            var sourceIds = sources.Select(source => source.Id).ToList();
            var credentials = await db.SourceCredentials.AsNoTracking()
                .Where(credential => sourceIds.Contains(credential.SourceProfileId))
                .ToDictionaryAsync(credential => credential.SourceProfileId);
            var logs = await db.EpgSyncLogs.AsNoTracking()
                .Where(log => sourceIds.Contains(log.SourceProfileId))
                .ToDictionaryAsync(log => log.SourceProfileId);
            var mappingDecisions = await db.EpgMappingDecisions.AsNoTracking()
                .Where(decision => sourceIds.Contains(decision.SourceProfileId))
                .ToListAsync();
            var decisionsBySource = mappingDecisions
                .GroupBy(decision => decision.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var decisionsByChannelAndGuide = mappingDecisions
                .GroupBy(decision => BuildDecisionKey(decision.ChannelId, decision.XmltvChannelId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(decision => decision.UpdatedAtUtc).First(), StringComparer.OrdinalIgnoreCase);

            var channelRows = await db.Channels.AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new
                    {
                        Channel = channel,
                        Category = category
                    })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    row => row.Category.SourceProfileId,
                    source => source.Id,
                    (row, source) => new
                    {
                        row.Channel,
                        row.Category,
                        Source = source
                    })
                .ToListAsync();

            var channelIds = channelRows.Select(row => row.Channel.Id).ToList();
            var programmeCounts = channelIds.Count == 0
                ? new Dictionary<int, int>()
                : await db.EpgPrograms.AsNoTracking()
                    .Where(program => channelIds.Contains(program.ChannelId))
                    .GroupBy(program => program.ChannelId)
                    .Select(group => new { ChannelId = group.Key, Count = group.Count() })
                    .ToDictionaryAsync(item => item.ChannelId, item => item.Count);

            var channelsWithGuideProgrammes = programmeCounts.Keys.ToHashSet();
            var reportSources = new List<EpgSourceCoverageReportItem>(sources.Count);
            foreach (var source in sources)
            {
                credentials.TryGetValue(source.Id, out var credential);
                logs.TryGetValue(source.Id, out var log);

                var sourceChannels = channelRows
                    .Where(row => row.Category.SourceProfileId == source.Id)
                    .ToList();
                var sourceChannelsWithGuide = sourceChannels
                    .Count(row => channelsWithGuideProgrammes.Contains(row.Channel.Id));
                var exact = CountMatchType(sourceChannels.Select(row => row.Channel), ChannelEpgMatchSource.Provider);
                var normalized = CountMatchType(sourceChannels.Select(row => row.Channel), ChannelEpgMatchSource.Normalized);
                var approvedMatches = CountMatchType(sourceChannels.Select(row => row.Channel), ChannelEpgMatchSource.UserApproved);
                var weak = sourceChannels
                    .Select(row => row.Channel)
                    .Count(channel => channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy);
                var sourceDecisions = decisionsBySource.TryGetValue(source.Id, out var decisions)
                    ? decisions
                    : new List<EpgMappingDecision>();
                var reviewSuggestions = sourceChannels
                    .Count(row => IsReviewSuggestion(row.Channel, decisionsByChannelAndGuide));
                var potentialCoverage = Math.Min(
                    sourceChannels.Count,
                    sourceChannelsWithGuide + sourceChannels.Count(row => IsPotentialReviewCoverage(row.Channel, channelsWithGuideProgrammes, decisionsByChannelAndGuide)));

                reportSources.Add(new EpgSourceCoverageReportItem
                {
                    SourceProfileId = source.Id,
                    SourceName = source.Name,
                    SourceType = source.Type,
                    ActiveMode = credential?.EpgMode ?? EpgActiveMode.Detected,
                    Status = log?.Status ?? EpgStatus.Unknown,
                    ResultCode = log?.ResultCode ?? EpgSyncResultCode.None,
                    TotalLiveChannels = sourceChannels.Count,
                    ChannelsWithGuideProgrammes = sourceChannelsWithGuide,
                    ReviewSuggestionChannels = reviewSuggestions,
                    PotentialCoverageChannels = potentialCoverage,
                    ExactMatches = log?.ExactMatchCount > 0 ? log.ExactMatchCount : exact,
                    NormalizedMatches = log?.NormalizedMatchCount > 0 ? log.NormalizedMatchCount : normalized,
                    ApprovedMatches = log?.ApprovedMatchCount > 0 ? log.ApprovedMatchCount : approvedMatches,
                    WeakMatches = log?.WeakMatchCount > 0 ? log.WeakMatchCount : weak,
                    ApprovedMappingDecisions = sourceDecisions.Count(decision => decision.Decision == EpgMappingDecisionState.Approved),
                    RejectedMappingDecisions = sourceDecisions.Count(decision => decision.Decision == EpgMappingDecisionState.Rejected),
                    UnmatchedChannels = Math.Max(0, sourceChannels.Count - sourceChannelsWithGuide),
                    ProgrammeCount = log?.ProgrammeCount ?? sourceChannels.Sum(row => programmeCounts.TryGetValue(row.Channel.Id, out var count) ? count : 0),
                    XmltvChannelCount = log?.XmltvChannelCount ?? 0,
                    LastSyncAttemptUtc = log?.SyncedAtUtc == default ? null : log?.SyncedAtUtc,
                    LastSuccessUtc = log?.LastSuccessAtUtc,
                    ActiveXmltvUrl = log?.ActiveXmltvUrl ?? string.Empty,
                    DetectedEpgUrl = credential?.DetectedEpgUrl ?? string.Empty,
                    ManualEpgUrl = credential?.ManualEpgUrl ?? string.Empty,
                    FallbackEpgUrls = credential?.FallbackEpgUrls ?? string.Empty,
                    FailureReason = log?.FailureReason ?? string.Empty,
                    WarningSummary = log?.GuideWarningSummary ?? string.Empty,
                    GuideSources = ParseGuideSources(log, credential),
                    CanSync = credential != null && credential.EpgMode != EpgActiveMode.None
                });
            }

            var unmatched = channelRows
                .Where(row => !channelsWithGuideProgrammes.Contains(row.Channel.Id))
                .OrderBy(row => row.Source.Name)
                .ThenBy(row => row.Category.Name)
                .ThenBy(row => row.Channel.Name)
                .Take(100)
                .Select(row => BuildChannelItem(row.Source, row.Category, row.Channel, programmeCounts, decisionsByChannelAndGuide))
                .ToList();

            var weakMatches = channelRows
                .Where(row => row.Channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy)
                .Where(row => GetDecisionState(row.Channel, decisionsByChannelAndGuide) != EpgMappingDecisionState.Rejected)
                .OrderBy(row => row.Channel.EpgMatchConfidence)
                .ThenBy(row => row.Source.Name)
                .ThenBy(row => row.Channel.Name)
                .Take(100)
                .Select(row => BuildChannelItem(row.Source, row.Category, row.Channel, programmeCounts, decisionsByChannelAndGuide))
                .ToList();

            var warnings = reportSources
                .SelectMany(source => new[] { source.WarningSummary, source.FailureReason })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            return new EpgCoverageReport
            {
                TotalLiveChannels = channelRows.Count,
                ChannelsWithGuideProgrammes = channelsWithGuideProgrammes.Count,
                TrustedCoverageChannels = channelsWithGuideProgrammes.Count,
                ReviewSuggestionChannels = reportSources.Sum(source => source.ReviewSuggestionChannels),
                PotentialCoverageChannels = Math.Min(
                    channelRows.Count,
                    channelsWithGuideProgrammes.Count + channelRows.Count(row => IsPotentialReviewCoverage(row.Channel, channelsWithGuideProgrammes, decisionsByChannelAndGuide))),
                ExactMatches = reportSources.Sum(source => source.ExactMatches),
                NormalizedMatches = reportSources.Sum(source => source.NormalizedMatches),
                ApprovedMatches = reportSources.Sum(source => source.ApprovedMatches),
                WeakMatches = reportSources.Sum(source => source.WeakMatches),
                ApprovedMappingDecisions = reportSources.Sum(source => source.ApprovedMappingDecisions),
                RejectedMappingDecisions = reportSources.Sum(source => source.RejectedMappingDecisions),
                UnmatchedChannels = Math.Max(0, channelRows.Count - channelsWithGuideProgrammes.Count),
                ProgrammeCount = reportSources.Sum(source => source.ProgrammeCount),
                XmltvChannelCount = reportSources.Sum(source => source.XmltvChannelCount),
                LastSyncAttemptUtc = reportSources.Select(source => source.LastSyncAttemptUtc).Where(value => value.HasValue).Max(),
                LastSuccessUtc = reportSources.Select(source => source.LastSuccessUtc).Where(value => value.HasValue).Max(),
                Sources = reportSources,
                TopUnmatchedChannels = unmatched,
                TopWeakMatches = weakMatches,
                Warnings = warnings
            };
        }

        private static int CountMatchType(IEnumerable<Channel> channels, ChannelEpgMatchSource source)
        {
            return channels.Count(channel => channel.EpgMatchSource == source);
        }

        private static bool IsWeakMatchType(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy;
        }

        private static bool IsReviewSuggestion(
            Channel channel,
            IReadOnlyDictionary<string, EpgMappingDecision> decisionsByChannelAndGuide)
        {
            return IsWeakMatchType(channel.EpgMatchSource) &&
                   GetDecisionState(channel, decisionsByChannelAndGuide) == EpgMappingDecisionState.None;
        }

        private static bool IsPotentialReviewCoverage(
            Channel channel,
            ISet<int> channelsWithGuideProgrammes,
            IReadOnlyDictionary<string, EpgMappingDecision> decisionsByChannelAndGuide)
        {
            return !channelsWithGuideProgrammes.Contains(channel.Id) &&
                   IsReviewSuggestion(channel, decisionsByChannelAndGuide);
        }

        private static EpgChannelCoverageReportItem BuildChannelItem(
            SourceProfile source,
            ChannelCategory category,
            Channel channel,
            IReadOnlyDictionary<int, int> programmeCounts,
            IReadOnlyDictionary<string, EpgMappingDecision> decisionsByChannelAndGuide)
        {
            var decision = GetDecision(channel, decisionsByChannelAndGuide);
            var decisionState = decision?.Decision ?? EpgMappingDecisionState.None;
            var isWeak = IsWeakMatchType(channel.EpgMatchSource);
            return new EpgChannelCoverageReportItem
            {
                ChannelId = channel.Id,
                SourceProfileId = source.Id,
                SourceName = source.Name,
                CategoryName = category.Name,
                ChannelName = channel.Name,
                ProviderEpgChannelId = channel.ProviderEpgChannelId,
                MatchedXmltvChannelId = channel.EpgChannelId,
                MatchType = BuildMatchType(channel.EpgMatchSource),
                MatchConfidence = channel.EpgMatchConfidence,
                MatchSummary = channel.EpgMatchSummary,
                ProgrammeCount = programmeCounts.TryGetValue(channel.Id, out var count) ? count : 0,
                IsActiveGuideAssignment = count > 0 && IsTrustedMatchType(channel.EpgMatchSource),
                ReviewStatus = BuildReviewStatus(channel.EpgMatchSource, count, decisionState),
                MappingDecision = decisionState,
                CanApprove = isWeak && decisionState != EpgMappingDecisionState.Approved,
                CanReject = isWeak && decisionState != EpgMappingDecisionState.Rejected,
                CanClearDecision = decisionState != EpgMappingDecisionState.None
            };
        }

        private static bool IsTrustedMatchType(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.UserApproved;
        }

        private static string BuildReviewStatus(ChannelEpgMatchSource source, int programmeCount, EpgMappingDecisionState decisionState)
        {
            if (decisionState == EpgMappingDecisionState.Approved && IsWeakMatchType(source))
            {
                return "Approved - refresh EPG to activate this mapping";
            }

            return source switch
            {
                ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.UserApproved => programmeCount > 0
                    ? "Active guide assignment"
                    : "Trusted match, no programmes in the active window",
                ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy => "Suggestion only - not used for current/next guide display",
                _ => "No guide match"
            };
        }

        private static string BuildMatchType(ChannelEpgMatchSource source)
        {
            return source switch
            {
                ChannelEpgMatchSource.Provider => "Exact",
                ChannelEpgMatchSource.Normalized => "Normalized",
                ChannelEpgMatchSource.UserApproved => "Approved",
                ChannelEpgMatchSource.Previous => "Reused",
                ChannelEpgMatchSource.Alias => "Weak alias",
                ChannelEpgMatchSource.Regex => "Weak regex",
                ChannelEpgMatchSource.Fuzzy => "Fuzzy",
                _ => "Unmatched"
            };
        }

        private static EpgMappingDecision? GetDecision(
            Channel channel,
            IReadOnlyDictionary<string, EpgMappingDecision> decisionsByChannelAndGuide)
        {
            if (string.IsNullOrWhiteSpace(channel.EpgChannelId))
            {
                return null;
            }

            return decisionsByChannelAndGuide.TryGetValue(BuildDecisionKey(channel.Id, channel.EpgChannelId), out var decision)
                ? decision
                : null;
        }

        private static EpgMappingDecisionState GetDecisionState(
            Channel channel,
            IReadOnlyDictionary<string, EpgMappingDecision> decisionsByChannelAndGuide)
        {
            return GetDecision(channel, decisionsByChannelAndGuide)?.Decision ?? EpgMappingDecisionState.None;
        }

        private static string BuildDecisionKey(int channelId, string xmltvChannelId)
        {
            return $"{channelId}|{(xmltvChannelId ?? string.Empty).Trim()}";
        }

        private static IReadOnlyList<EpgGuideSourceStatusSnapshot> ParseGuideSources(EpgSyncLog? log, SourceCredential? credential)
        {
            if (!string.IsNullOrWhiteSpace(log?.GuideSourceStatusJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<EpgGuideSourceStatusSnapshot>>(log.GuideSourceStatusJson);
                    return parsed is null
                        ? Array.Empty<EpgGuideSourceStatusSnapshot>()
                        : parsed
                            .OrderBy(source => source.Priority)
                            .ToList();
                }
                catch
                {
                }
            }

            var snapshots = new List<EpgGuideSourceStatusSnapshot>();
            if (!string.IsNullOrWhiteSpace(log?.ActiveXmltvUrl))
            {
                snapshots.Add(new EpgGuideSourceStatusSnapshot
                {
                    Label = "Active XMLTV",
                    Url = log.ActiveXmltvUrl,
                    Kind = credential?.EpgMode == EpgActiveMode.Manual ? EpgGuideSourceKind.Manual : EpgGuideSourceKind.Provider,
                    Status = log.IsSuccess ? EpgGuideSourceStatus.Ready : EpgGuideSourceStatus.Failed,
                    Message = string.IsNullOrWhiteSpace(log.FailureReason) ? log.Status.ToString() : log.FailureReason,
                    CheckedAtUtc = log.SyncedAtUtc == default ? null : log.SyncedAtUtc
                });
            }

            foreach (var url in SplitGuideUrls(credential?.FallbackEpgUrls))
            {
                var kind = EpgPublicGuideCatalog.ClassifyFallbackUrl(url);
                snapshots.Add(new EpgGuideSourceStatusSnapshot
                {
                    Label = EpgPublicGuideCatalog.BuildGuideSourceLabel(url, kind, "Fallback XMLTV"),
                    Url = url,
                    Kind = kind,
                    Status = EpgGuideSourceStatus.Pending,
                    IsOptional = true,
                    Message = "Configured. Sync to test this source."
                });
            }

            return snapshots;
        }

        private static IEnumerable<string> SplitGuideUrls(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var item in value.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = item.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    yield return trimmed;
                }
            }
        }
    }
}
