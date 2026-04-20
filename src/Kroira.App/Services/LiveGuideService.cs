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
                        ChannelId = channel.Id,
                        category.SourceProfileId
                    })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.SourceProfileId,
                    profile => profile.Id,
                    (item, profile) => new ChannelGuideContext(
                        item.ChannelId,
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
                               && program.EndTimeUtc > normalizedNowUtc
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
                    program.EndTimeUtc))
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
                var sourceStatusSummary = BuildSourceStatusSummary(sourceStatus, sourceResultCode, sourceMode, epgLog);
                var hasGuideData = matchedSet.Contains(channelId);
                var channelPrograms = programsByChannel.TryGetValue(channelId, out var programs)
                    ? programs
                    : new List<ChannelGuideProgram>();

                var current = channelPrograms
                    .Where(program => program.StartTimeUtc <= normalizedNowUtc && program.EndTimeUtc > normalizedNowUtc)
                    .OrderByDescending(program => program.StartTimeUtc)
                    .FirstOrDefault();

                var next = channelPrograms
                    .Where(program => program.StartTimeUtc > normalizedNowUtc)
                    .OrderBy(program => program.StartTimeUtc)
                    .FirstOrDefault();

                result[channelId] = new ChannelGuideSummary(
                    hasGuideData,
                    sourceStatus,
                    sourceResultCode,
                    sourceMode,
                    sourceStatusSummary,
                    current,
                    next);
            }

            return result;
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

            if (sourceType == Models.SourceType.M3U &&
                string.IsNullOrWhiteSpace(credential.DetectedEpgUrl) &&
                string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                return EpgStatus.UnavailableNoXmltv;
            }

            return EpgStatus.Unknown;
        }

        private static string BuildSourceStatusSummary(EpgStatus status, EpgSyncResultCode resultCode, EpgActiveMode mode, EpgSyncLog? epgLog)
        {
            if (mode == EpgActiveMode.None)
            {
                return "Guide is disabled for this source.";
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
    }

    public sealed record ChannelGuideSummary(
        bool HasGuideData,
        EpgStatus SourceStatus,
        EpgSyncResultCode SourceResultCode,
        EpgActiveMode SourceMode,
        string SourceStatusSummary,
        ChannelGuideProgram? CurrentProgram,
        ChannelGuideProgram? NextProgram);

    public sealed record ChannelGuideProgram(
        int ChannelId,
        string Title,
        string Description,
        string? Subtitle,
        string? Category,
        DateTime StartTimeUtc,
        DateTime EndTimeUtc);

    internal sealed record GuideCredentialSnapshot(
        int SourceProfileId,
        EpgActiveMode Mode,
        string DetectedEpgUrl,
        string ManualEpgUrl);

    internal sealed record ChannelGuideContext(
        int ChannelId,
        int SourceProfileId,
        Models.SourceType SourceType);
}
