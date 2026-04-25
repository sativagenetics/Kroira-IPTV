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
    public interface ILiveGuideService
    {
        Task<IReadOnlyDictionary<int, ChannelGuideSummary>> GetGuideSummariesAsync(
            AppDbContext db,
            IReadOnlyCollection<int> channelIds,
            DateTime nowUtc);
    }

    public sealed class LiveGuideService : ILiveGuideService
    {
        private readonly ICatchupPlaybackService _catchupPlaybackService;

        public LiveGuideService(ICatchupPlaybackService catchupPlaybackService)
        {
            _catchupPlaybackService = catchupPlaybackService;
        }

        public async Task<IReadOnlyDictionary<int, ChannelGuideSummary>> GetGuideSummariesAsync(
            AppDbContext db,
            IReadOnlyCollection<int> channelIds,
            DateTime nowUtc)
        {
            if (channelIds.Count == 0)
            {
                return new Dictionary<int, ChannelGuideSummary>();
            }

            var ids = channelIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, ChannelGuideSummary>();
            }

            var normalizedNowUtc = NormalizeUtc(nowUtc);
            var windowStartUtc = normalizedNowUtc.AddHours(-ResolveCatchupLookbackHours());
            var windowEndUtc = normalizedNowUtc.AddHours(24);

            var channelContexts = await db.Channels
                .AsNoTracking()
                .Where(channel => ids.Contains(channel.Id))
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new
                    {
                        Channel = channel,
                        ChannelId = channel.Id,
                        category.SourceProfileId
                    })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.SourceProfileId,
                    profile => profile.Id,
                    (item, profile) => new ChannelGuideContext(
                        item.Channel,
                        item.SourceProfileId,
                        profile.Type))
                .ToListAsync();
            var channelContextMap = channelContexts.ToDictionary(item => item.ChannelId);
            var sourceIds = channelContexts
                .Select(item => item.SourceProfileId)
                .Distinct()
                .ToList();

            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(credential => sourceIds.Contains(credential.SourceProfileId))
                .Select(credential => new GuideCredentialSnapshot(
                    credential.SourceProfileId,
                    credential.EpgMode,
                    credential.DetectedEpgUrl,
                    credential.EpgUrl))
                .ToDictionaryAsync(item => item.SourceProfileId);

            var epgLogs = new Dictionary<int, EpgSyncLog>();
            try
            {
                epgLogs = await db.EpgSyncLogs
                    .AsNoTracking()
                    .Where(log => sourceIds.Contains(log.SourceProfileId))
                    .ToDictionaryAsync(log => log.SourceProfileId);
            }
            catch
            {
            }

            var matchedIds = await db.EpgPrograms
                .AsNoTracking()
                .Where(program => ids.Contains(program.ChannelId))
                .Select(program => program.ChannelId)
                .Distinct()
                .ToListAsync();

            var candidatePrograms = await db.EpgPrograms
                .AsNoTracking()
                .Where(program => ids.Contains(program.ChannelId)
                               && program.EndTimeUtc > windowStartUtc
                               && program.StartTimeUtc < windowEndUtc)
                .OrderBy(program => program.ChannelId)
                .ThenBy(program => program.StartTimeUtc)
                .Select(program => new ChannelGuideProgram(
                    program.ChannelId,
                    program.Title,
                    program.Description,
                    program.Subtitle,
                    program.Category,
                    program.StartTimeUtc,
                    program.EndTimeUtc,
                    false,
                    CatchupAvailabilityState.None,
                    string.Empty,
                    string.Empty,
                    CatchupRequestKind.None))
                .ToListAsync();

            var programsByChannel = candidatePrograms
                .Select(NormalizeProgramUtc)
                .GroupBy(program => program.ChannelId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var matchedSet = matchedIds.ToHashSet();
            var result = new Dictionary<int, ChannelGuideSummary>(ids.Count);

            foreach (var channelId in ids)
            {
                channelContextMap.TryGetValue(channelId, out var channelContext);
                epgLogs.TryGetValue(channelContext?.SourceProfileId ?? 0, out var epgLog);
                credentials.TryGetValue(channelContext?.SourceProfileId ?? 0, out var credential);

                var sourceStatus = ResolveSourceStatus(channelContext?.SourceType ?? Models.SourceType.M3U, credential, epgLog);
                var sourceResultCode = epgLog?.ResultCode ?? EpgSyncResultCode.None;
                var sourceMode = epgLog?.ActiveMode ?? credential?.Mode ?? EpgActiveMode.Detected;
                var sourceStatusSummary = BuildSourceStatusSummary(
                    channelContext?.SourceType ?? Models.SourceType.M3U,
                    sourceStatus,
                    sourceResultCode,
                    sourceMode,
                    credential,
                    epgLog);
                var hasGuideData = matchedSet.Contains(channelId);
                var channelPrograms = programsByChannel.TryGetValue(channelId, out var programs)
                    ? programs
                    : new List<ChannelGuideProgram>();

                var currentBase = channelPrograms
                    .Where(program => program.StartTimeUtc <= normalizedNowUtc && program.EndTimeUtc > normalizedNowUtc)
                    .OrderByDescending(program => program.StartTimeUtc)
                    .FirstOrDefault();

                var nextBase = channelPrograms
                    .Where(program => program.StartTimeUtc > normalizedNowUtc)
                    .OrderBy(program => program.StartTimeUtc)
                    .FirstOrDefault();

                var evaluatedPrograms = channelPrograms
                    .OrderByDescending(program => program.StartTimeUtc <= normalizedNowUtc && program.EndTimeUtc > normalizedNowUtc)
                    .ThenByDescending(program => program.StartTimeUtc <= normalizedNowUtc)
                    .ThenByDescending(program => program.StartTimeUtc)
                    .Take(6)
                    .Select(program =>
                    {
                        var availability = channelContext != null
                            ? _catchupPlaybackService.EvaluateProgramAvailability(
                                channelContext.Channel,
                                channelContext.SourceType,
                                program,
                                normalizedNowUtc)
                            : new CatchupProgramAvailability();
                        var isCurrent = program.StartTimeUtc <= normalizedNowUtc && program.EndTimeUtc > normalizedNowUtc;
                        return program with
                        {
                            IsCurrent = isCurrent,
                            CatchupAvailability = availability.State,
                            CatchupStatusText = availability.Message,
                            CatchupActionLabel = availability.ActionLabel,
                            CatchupRequestKind = availability.RequestKind
                        };
                    })
                    .ToList();

                var current = evaluatedPrograms.FirstOrDefault(program => program.IsCurrent) ?? currentBase;
                var next = evaluatedPrograms
                    .Where(program => program.StartTimeUtc > normalizedNowUtc)
                    .OrderBy(program => program.StartTimeUtc)
                    .FirstOrDefault() ?? nextBase;
                var catchupStatusSummary = channelContext == null
                    ? string.Empty
                    : BuildCatchupStatusSummary(channelContext.Channel, current, evaluatedPrograms, normalizedNowUtc);

                result[channelId] = new ChannelGuideSummary(
                    hasGuideData,
                    sourceStatus,
                    sourceResultCode,
                    sourceMode,
                    sourceStatusSummary,
                    channelContext?.Channel.SupportsCatchup ?? false,
                    channelContext?.Channel.CatchupWindowHours ?? 0,
                    catchupStatusSummary,
                    current,
                    next,
                    evaluatedPrograms);
            }

            return result;
        }

        private static int ResolveCatchupLookbackHours()
        {
            return 6;
        }

        private static EpgStatus ResolveSourceStatus(Models.SourceType sourceType, GuideCredentialSnapshot? credential, EpgSyncLog? epgLog)
        {
            if (epgLog != null && epgLog.Status != EpgStatus.Unknown)
            {
                return epgLog.Status;
            }

            if (credential == null)
            {
                return EpgStatus.Unknown;
            }

            if (credential.Mode == EpgActiveMode.None)
            {
                return EpgStatus.Unknown;
            }

            if (credential.Mode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                return EpgStatus.UnavailableNoXmltv;
            }

            if (sourceType == Models.SourceType.Stalker &&
                string.IsNullOrWhiteSpace(credential.DetectedEpgUrl) &&
                string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                return EpgStatus.Unknown;
            }

            if (sourceType == Models.SourceType.M3U &&
                string.IsNullOrWhiteSpace(credential.DetectedEpgUrl) &&
                string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                return EpgStatus.UnavailableNoXmltv;
            }

            return EpgStatus.Unknown;
        }

        private static string BuildSourceStatusSummary(
            Models.SourceType sourceType,
            EpgStatus status,
            EpgSyncResultCode resultCode,
            EpgActiveMode mode,
            GuideCredentialSnapshot? credential,
            EpgSyncLog? epgLog)
        {
            if (mode == EpgActiveMode.None)
            {
                return sourceType == Models.SourceType.Stalker
                    ? "Guide is optional for this Stalker source until you add a manual XMLTV feed."
                    : "Guide is disabled for this source.";
            }

            if (sourceType == Models.SourceType.Stalker &&
                string.IsNullOrWhiteSpace(credential?.DetectedEpgUrl) &&
                string.IsNullOrWhiteSpace(credential?.ManualEpgUrl) &&
                status == EpgStatus.Unknown)
            {
                return "Guide is optional for this Stalker source until you add a manual XMLTV feed.";
            }

            return status switch
            {
                EpgStatus.Syncing => "Guide sync is currently running.",
                EpgStatus.UnavailableNoXmltv when mode == EpgActiveMode.Manual => "Manual guide mode is selected, but no manual XMLTV URL is saved.",
                EpgStatus.UnavailableNoXmltv => "This provider does not advertise XMLTV guide data.",
                EpgStatus.FailedFetchOrParse when resultCode == EpgSyncResultCode.ParseFailed => "XMLTV was fetched, but parsing failed.",
                EpgStatus.FailedFetchOrParse when resultCode == EpgSyncResultCode.PersistFailed => "XMLTV was parsed, but persistence failed.",
                EpgStatus.FailedFetchOrParse => "XMLTV guide fetch failed.",
                EpgStatus.Stale => string.IsNullOrWhiteSpace(epgLog?.FailureReason)
                    ? "Latest guide refresh failed. Older guide data may still be shown."
                    : $"Latest guide refresh failed. {epgLog.FailureReason}",
                _ when resultCode == EpgSyncResultCode.PartialMatch => "Guide coverage is partial for this source.",
                _ when resultCode == EpgSyncResultCode.ZeroCoverage => "Guide parsed, but the source has zero matched channel coverage.",
                _ when mode == EpgActiveMode.Manual => "Manual XMLTV override is active.",
                _ => "Guide is ready for this source."
            };
        }

        private static ChannelGuideProgram NormalizeProgramUtc(ChannelGuideProgram program)
        {
            return program with
            {
                StartTimeUtc = NormalizeUtc(program.StartTimeUtc),
                EndTimeUtc = NormalizeUtc(program.EndTimeUtc)
            };
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string BuildCatchupStatusSummary(
            Channel channel,
            ChannelGuideProgram? currentProgram,
            IReadOnlyCollection<ChannelGuideProgram> timelinePrograms,
            DateTime nowUtc)
        {
            if (!channel.SupportsCatchup)
            {
                return string.IsNullOrWhiteSpace(channel.CatchupSummary)
                    ? "Catchup is not available on this channel."
                    : channel.CatchupSummary;
            }

            if (currentProgram != null && currentProgram.CatchupAvailability == CatchupAvailabilityState.Available)
            {
                return currentProgram.CatchupStatusText;
            }

            var normalizedNowUtc = NormalizeUtc(nowUtc);
            var replayablePastProgram = timelinePrograms.FirstOrDefault(program =>
                !program.IsCurrent &&
                program.StartTimeUtc <= normalizedNowUtc &&
                program.CatchupAvailability == CatchupAvailabilityState.Available);
            if (replayablePastProgram != null)
            {
                return replayablePastProgram.CatchupStatusText;
            }

            if (channel.CatchupWindowHours > 0)
            {
                return $"Catchup window: {FormatWindow(channel.CatchupWindowHours)}.";
            }

            return string.IsNullOrWhiteSpace(channel.CatchupSummary)
                ? "Catchup is advertised, but the provider did not expose a valid archive window."
                : channel.CatchupSummary;
        }

        private static string FormatWindow(int hours)
        {
            if (hours >= 48 && hours % 24 == 0)
            {
                var days = hours / 24;
                return $"{days} day{(days == 1 ? string.Empty : "s")}";
            }

            return $"{hours} hour{(hours == 1 ? string.Empty : "s")}";
        }
    }

    public sealed record ChannelGuideSummary(
        bool HasGuideData,
        EpgStatus SourceStatus,
        EpgSyncResultCode SourceResultCode,
        EpgActiveMode SourceMode,
        string SourceStatusSummary,
        bool SupportsCatchup,
        int CatchupWindowHours,
        string CatchupStatusSummary,
        ChannelGuideProgram? CurrentProgram,
        ChannelGuideProgram? NextProgram,
        IReadOnlyList<ChannelGuideProgram> TimelinePrograms);

    public sealed record ChannelGuideProgram(
        int ChannelId,
        string Title,
        string Description,
        string? Subtitle,
        string? Category,
        DateTime StartTimeUtc,
        DateTime EndTimeUtc,
        bool IsCurrent,
        CatchupAvailabilityState CatchupAvailability,
        string CatchupStatusText,
        string CatchupActionLabel,
        CatchupRequestKind CatchupRequestKind);

    internal sealed record GuideCredentialSnapshot(
        int SourceProfileId,
        EpgActiveMode Mode,
        string DetectedEpgUrl,
        string ManualEpgUrl);

    internal sealed record ChannelGuideContext(
        Channel Channel,
        int SourceProfileId,
        Models.SourceType SourceType)
    {
        public int ChannelId => Channel.Id;
    }
}
