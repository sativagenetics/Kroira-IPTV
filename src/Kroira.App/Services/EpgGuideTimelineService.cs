#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface IEpgGuideTimelineService
    {
        Task<EpgGuideTimelineResult> BuildTimelineAsync(
            AppDbContext db,
            EpgGuideTimelineRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class EpgGuideTimelineService : IEpgGuideTimelineService
    {
        private const int DefaultMaxChannels = 120;
        private static readonly TimeSpan StaleSuccessAge = TimeSpan.FromHours(36);

        public async Task<EpgGuideTimelineResult> BuildTimelineAsync(
            AppDbContext db,
            EpgGuideTimelineRequest request,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = NormalizeUtc(request.NowUtc == default ? DateTime.UtcNow : request.NowUtc);
            var rangeStartUtc = NormalizeUtc(request.RangeStartUtc == default ? AlignToSlot(nowUtc, request.SlotDuration) : request.RangeStartUtc);
            var rangeDuration = request.RangeDuration <= TimeSpan.Zero ? TimeSpan.FromHours(4) : request.RangeDuration;
            var slotDuration = request.SlotDuration <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : request.SlotDuration;
            var rangeEndUtc = rangeStartUtc.Add(rangeDuration);
            var maxChannels = request.MaxChannels <= 0 ? DefaultMaxChannels : request.MaxChannels;
            var searchText = request.SearchText?.Trim() ?? string.Empty;

            var allSources = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(source => source.Name)
                .Select(source => new SourceOptionProjection
                {
                    Id = source.Id,
                    Name = source.Name,
                    Type = source.Type
                })
                .ToListAsync(cancellationToken);

            var categoryOptionsQuery =
                from category in db.ChannelCategories.AsNoTracking()
                join source in db.SourceProfiles.AsNoTracking()
                    on category.SourceProfileId equals source.Id
                where !request.SourceProfileId.HasValue || category.SourceProfileId == request.SourceProfileId.Value
                orderby category.Name
                select new CategoryOptionProjection
                {
                    Id = category.Id,
                    Name = category.Name,
                    SourceName = source.Name
                };
            var categories = await categoryOptionsQuery.ToListAsync(cancellationToken);

            var channelQuery =
                from channel in db.Channels.AsNoTracking()
                join category in db.ChannelCategories.AsNoTracking()
                    on channel.ChannelCategoryId equals category.Id
                join source in db.SourceProfiles.AsNoTracking()
                    on category.SourceProfileId equals source.Id
                select new TimelineChannelProjection
                {
                    ChannelId = channel.Id,
                    SourceProfileId = source.Id,
                    SourceName = source.Name,
                    SourceType = source.Type,
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryOrderIndex = category.OrderIndex,
                    ChannelName = channel.Name,
                    LogoUrl = channel.LogoUrl,
                    StreamUrl = channel.StreamUrl,
                    EpgChannelId = channel.EpgChannelId,
                    MatchSource = channel.EpgMatchSource,
                    MatchConfidence = channel.EpgMatchConfidence
                };

            if (request.SourceProfileId.HasValue)
            {
                channelQuery = channelQuery.Where(channel => channel.SourceProfileId == request.SourceProfileId.Value);
            }

            if (request.CategoryId.HasValue)
            {
                channelQuery = channelQuery.Where(channel => channel.CategoryId == request.CategoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                channelQuery = channelQuery.Where(channel =>
                    channel.ChannelName.Contains(searchText) ||
                    channel.CategoryName.Contains(searchText) ||
                    channel.SourceName.Contains(searchText));
            }

            var channelRows = await channelQuery
                .OrderBy(channel => channel.SourceName)
                .ThenBy(channel => channel.CategoryOrderIndex)
                .ThenBy(channel => channel.CategoryName)
                .ThenBy(channel => channel.ChannelName)
                .Take(maxChannels)
                .ToListAsync(cancellationToken);

            var channelIds = channelRows.Select(channel => channel.ChannelId).ToList();
            var sourceIds = channelRows.Select(channel => channel.SourceProfileId).Distinct().ToList();
            var sourceLogs = sourceIds.Count == 0
                ? new Dictionary<int, EpgSyncLog>()
                : await db.EpgSyncLogs
                    .AsNoTracking()
                    .Where(log => sourceIds.Contains(log.SourceProfileId))
                    .ToDictionaryAsync(log => log.SourceProfileId, cancellationToken);

            var programQueryStartUtc = rangeStartUtc <= nowUtc ? rangeStartUtc : nowUtc;
            var programQueryEndUtc = new[] { rangeEndUtc, nowUtc.AddHours(24) }.Max();
            var programs = channelIds.Count == 0
                ? new List<EpgProgram>()
                : await db.EpgPrograms
                    .AsNoTracking()
                    .Where(program => channelIds.Contains(program.ChannelId) &&
                                      program.EndTimeUtc > programQueryStartUtc &&
                                      program.StartTimeUtc < programQueryEndUtc)
                    .OrderBy(program => program.ChannelId)
                    .ThenBy(program => program.StartTimeUtc)
                    .ToListAsync(cancellationToken);

            var programsByChannel = programs
                .GroupBy(program => program.ChannelId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var rows = new List<EpgGuideTimelineChannel>(channelRows.Count);
            foreach (var channel in channelRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var channelPrograms = programsByChannel.TryGetValue(channel.ChannelId, out var items)
                    ? items
                    : new List<EpgProgram>();
                var timelinePrograms = channelPrograms
                    .Where(program => program.EndTimeUtc > rangeStartUtc && program.StartTimeUtc < rangeEndUtc)
                    .Select(program => BuildTimelineProgram(program, rangeStartUtc, rangeEndUtc, nowUtc))
                    .Where(program => program.WidthPercent > 0)
                    .ToList();

                rows.Add(new EpgGuideTimelineChannel
                {
                    ChannelId = channel.ChannelId,
                    SourceProfileId = channel.SourceProfileId,
                    SourceName = channel.SourceName,
                    SourceType = channel.SourceType,
                    CategoryId = channel.CategoryId,
                    CategoryName = channel.CategoryName,
                    ChannelName = channel.ChannelName,
                    LogoUrl = channel.LogoUrl ?? string.Empty,
                    StreamUrl = channel.StreamUrl ?? string.Empty,
                    EpgChannelId = channel.EpgChannelId ?? string.Empty,
                    MatchSource = channel.MatchSource,
                    MatchConfidence = channel.MatchConfidence,
                    CurrentProgram = channelPrograms
                        .Where(program => program.StartTimeUtc <= nowUtc && program.EndTimeUtc > nowUtc)
                        .OrderByDescending(program => program.StartTimeUtc)
                        .Select(program => BuildTimelineProgram(program, rangeStartUtc, rangeEndUtc, nowUtc))
                        .FirstOrDefault(),
                    NextProgram = channelPrograms
                        .Where(program => program.StartTimeUtc > nowUtc)
                        .OrderBy(program => program.StartTimeUtc)
                        .Select(program => BuildTimelineProgram(program, rangeStartUtc, rangeEndUtc, nowUtc))
                        .FirstOrDefault(),
                    Programs = timelinePrograms
                });
            }

            var staleSourceNames = ResolveStaleSources(channelRows, programs, sourceLogs, nowUtc);
            return new EpgGuideTimelineResult
            {
                RangeStartUtc = rangeStartUtc,
                RangeEndUtc = rangeEndUtc,
                NowUtc = nowUtc,
                Slots = GenerateSlots(rangeStartUtc, rangeEndUtc, slotDuration),
                Channels = rows,
                SourceOptions = BuildSourceOptions(allSources),
                CategoryOptions = BuildCategoryOptions(categories),
                HasGuideData = rows.Any(row => row.Programs.Count > 0 || row.CurrentProgram != null || row.NextProgram != null),
                IsStale = staleSourceNames.Count > 0,
                StaleWarningText = staleSourceNames.Count == 0
                    ? string.Empty
                    : $"Guide data may be stale for {string.Join(", ", staleSourceNames.Take(4))}{(staleSourceNames.Count > 4 ? "..." : string.Empty)}."
            };
        }

        internal static IReadOnlyList<EpgGuideTimelineSlot> GenerateSlots(
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            TimeSpan slotDuration)
        {
            if (slotDuration <= TimeSpan.Zero || rangeEndUtc <= rangeStartUtc)
            {
                return Array.Empty<EpgGuideTimelineSlot>();
            }

            var slots = new List<EpgGuideTimelineSlot>();
            var cursor = NormalizeUtc(rangeStartUtc);
            var end = NormalizeUtc(rangeEndUtc);
            while (cursor < end && slots.Count < 96)
            {
                var next = cursor.Add(slotDuration);
                slots.Add(new EpgGuideTimelineSlot(cursor, next > end ? end : next, cursor.ToLocalTime().ToString("HH:mm")));
                cursor = next;
            }

            return slots;
        }

        internal static DateTime AlignToSlot(DateTime valueUtc, TimeSpan slotDuration)
        {
            var utc = NormalizeUtc(valueUtc);
            if (slotDuration <= TimeSpan.Zero)
            {
                return utc;
            }

            var ticks = utc.Ticks - utc.Ticks % slotDuration.Ticks;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static EpgGuideTimelineProgram BuildTimelineProgram(
            EpgProgram program,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            DateTime nowUtc)
        {
            var start = NormalizeUtc(program.StartTimeUtc);
            var end = NormalizeUtc(program.EndTimeUtc);
            var rangeTicks = Math.Max(1, (rangeEndUtc - rangeStartUtc).Ticks);
            var clippedStart = start < rangeStartUtc ? rangeStartUtc : start;
            var clippedEnd = end > rangeEndUtc ? rangeEndUtc : end;
            var widthPercent = clippedEnd <= clippedStart
                ? 0
                : (clippedEnd - clippedStart).Ticks * 100d / rangeTicks;

            return new EpgGuideTimelineProgram
            {
                ProgramId = program.Id,
                ChannelId = program.ChannelId,
                Title = program.Title,
                Description = program.Description,
                Subtitle = program.Subtitle,
                Category = program.Category,
                StartTimeUtc = start,
                EndTimeUtc = end,
                IsCurrent = start <= nowUtc && end > nowUtc,
                OffsetPercent = Math.Max(0, (clippedStart - rangeStartUtc).Ticks * 100d / rangeTicks),
                WidthPercent = Math.Min(100, widthPercent)
            };
        }

        private static IReadOnlyList<string> ResolveStaleSources(
            IReadOnlyCollection<TimelineChannelProjection> channels,
            IReadOnlyCollection<EpgProgram> programs,
            IReadOnlyDictionary<int, EpgSyncLog> sourceLogs,
            DateTime nowUtc)
        {
            var latestProgramEndBySource = channels
                .Join(
                    programs,
                    channel => channel.ChannelId,
                    program => program.ChannelId,
                    (channel, program) => new { channel.SourceProfileId, channel.SourceName, End = NormalizeUtc(program.EndTimeUtc) })
                .GroupBy(item => item.SourceProfileId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Max(item => item.End));

            var stale = new List<string>();
            foreach (var source in channels
                         .GroupBy(channel => new { channel.SourceProfileId, channel.SourceName })
                         .Select(group => group.Key)
                         .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase))
            {
                sourceLogs.TryGetValue(source.SourceProfileId, out var log);
                var logIsStale = log?.Status == EpgStatus.Stale ||
                                 log?.Status == EpgStatus.FailedFetchOrParse && log.LastSuccessAtUtc.HasValue ||
                                 log?.LastSuccessAtUtc < nowUtc.Subtract(StaleSuccessAge);
                var programmesEnded = latestProgramEndBySource.TryGetValue(source.SourceProfileId, out var latestEnd) &&
                                      latestEnd <= nowUtc;
                if (logIsStale || programmesEnded)
                {
                    stale.Add(source.SourceName);
                }
            }

            return stale;
        }

        private static IReadOnlyList<EpgGuideTimelineSourceOption> BuildSourceOptions(IEnumerable<SourceOptionProjection> sources)
        {
            var result = new List<EpgGuideTimelineSourceOption>
            {
                new()
                {
                    Label = "All sources",
                    SourceName = "All sources"
                }
            };

            foreach (var source in sources)
            {
                result.Add(new EpgGuideTimelineSourceOption
                {
                    SourceProfileId = source.Id,
                    SourceName = source.Name,
                    SourceType = source.Type,
                    Label = $"{source.Name} ({source.Type})"
                });
            }

            return result;
        }

        private static IReadOnlyList<EpgGuideTimelineCategoryOption> BuildCategoryOptions(IEnumerable<CategoryOptionProjection> categories)
        {
            var result = new List<EpgGuideTimelineCategoryOption>
            {
                new()
                {
                    Label = "All categories",
                    CategoryName = "All categories"
                }
            };

            foreach (var category in categories)
            {
                result.Add(new EpgGuideTimelineCategoryOption
                {
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    Label = category.Name
                });
            }

            return result
                .GroupBy(item => item.CategoryId)
                .Select(group => group.First())
                .ToList();
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private sealed class TimelineChannelProjection
        {
            public int ChannelId { get; set; }
            public int SourceProfileId { get; set; }
            public string SourceName { get; set; } = string.Empty;
            public SourceType SourceType { get; set; }
            public int CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public int CategoryOrderIndex { get; set; }
            public string ChannelName { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string EpgChannelId { get; set; } = string.Empty;
            public ChannelEpgMatchSource MatchSource { get; set; }
            public int MatchConfidence { get; set; }
        }

        private sealed class SourceOptionProjection
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public SourceType Type { get; set; }
        }

        private sealed class CategoryOptionProjection
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string SourceName { get; set; } = string.Empty;
        }
    }
}
