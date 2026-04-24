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
                var weak = sourceChannels
                    .Select(row => row.Channel)
                    .Count(channel => channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy);

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
                    ExactMatches = log?.ExactMatchCount > 0 ? log.ExactMatchCount : exact,
                    NormalizedMatches = log?.NormalizedMatchCount > 0 ? log.NormalizedMatchCount : normalized,
                    WeakMatches = log?.WeakMatchCount > 0 ? log.WeakMatchCount : weak,
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
                .Select(row => BuildChannelItem(row.Source, row.Category, row.Channel, programmeCounts))
                .ToList();

            var weakMatches = channelRows
                .Where(row => row.Channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy)
                .OrderBy(row => row.Channel.EpgMatchConfidence)
                .ThenBy(row => row.Source.Name)
                .ThenBy(row => row.Channel.Name)
                .Take(100)
                .Select(row => BuildChannelItem(row.Source, row.Category, row.Channel, programmeCounts))
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
                ExactMatches = reportSources.Sum(source => source.ExactMatches),
                NormalizedMatches = reportSources.Sum(source => source.NormalizedMatches),
                WeakMatches = reportSources.Sum(source => source.WeakMatches),
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

        private static EpgChannelCoverageReportItem BuildChannelItem(
            SourceProfile source,
            ChannelCategory category,
            Channel channel,
            IReadOnlyDictionary<int, int> programmeCounts)
        {
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
                ReviewStatus = BuildReviewStatus(channel.EpgMatchSource, count)
            };
        }

        private static bool IsTrustedMatchType(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized;
        }

        private static string BuildReviewStatus(ChannelEpgMatchSource source, int programmeCount)
        {
            return source switch
            {
                ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized => programmeCount > 0
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
                ChannelEpgMatchSource.Previous => "Reused",
                ChannelEpgMatchSource.Alias => "Weak alias",
                ChannelEpgMatchSource.Regex => "Weak regex",
                ChannelEpgMatchSource.Fuzzy => "Fuzzy",
                _ => "Unmatched"
            };
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
