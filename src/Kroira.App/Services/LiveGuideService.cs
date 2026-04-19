#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
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

                result[channelId] = new ChannelGuideSummary(hasGuideData, current, next);
            }

            return result;
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
}
