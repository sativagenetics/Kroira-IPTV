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
    public sealed class WatchProgressSnapshot
    {
        public int ProgressId { get; init; }
        public int ContentId { get; init; }
        public PlaybackContentType ContentType { get; init; }
        public long PositionMs { get; init; }
        public long DurationMs { get; init; }
        public string LogicalContentKey { get; init; } = string.Empty;
        public int PreferredSourceProfileId { get; init; }
        public bool IsCompleted { get; init; }
        public WatchStateOverride WatchStateOverride { get; init; }
        public DateTime LastWatched { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public bool IsWatched { get; init; }
        public long ResumePositionMs { get; init; }
        public bool HasResumePoint { get; init; }
        public double ProgressPercent { get; init; }
    }

    public sealed class SeriesQueueSelection
    {
        public Series Series { get; init; } = new();
        public Season Season { get; init; } = new();
        public Episode Episode { get; init; } = new();
        public WatchProgressSnapshot? EpisodeSnapshot { get; init; }
        public int TotalEpisodeCount { get; init; }
        public int WatchedEpisodeCount { get; init; }
        public bool IsWatched { get; init; }
        public bool IsResumeCandidate { get; init; }
        public long ResumePositionMs { get; init; }
        public DateTime SortAtUtc { get; init; }
    }

    internal static class WatchStateRules
    {
        public const long MinimumSavedPositionMs = 5_000;
        public const long MinimumResumePositionMs = 15_000;
        public const long CompletionRemainingThresholdMs = 120_000;
        public const double CompletionPercent = 0.92;

        public static bool ComputeCompleted(long positionMs, long durationMs)
        {
            if (durationMs <= 0)
            {
                return false;
            }

            if (positionMs >= durationMs)
            {
                return true;
            }

            var remainingMs = Math.Max(durationMs - positionMs, 0);
            return positionMs >= durationMs * CompletionPercent || remainingMs <= CompletionRemainingThresholdMs;
        }

        public static bool IsWatched(PlaybackProgress progress)
        {
            return progress.WatchStateOverride switch
            {
                WatchStateOverride.Watched => true,
                WatchStateOverride.Unwatched => false,
                _ => progress.IsCompleted
            };
        }

        public static long NormalizeResumePosition(long positionMs, long durationMs, bool isWatched)
        {
            if (isWatched)
            {
                return 0;
            }

            var normalizedPositionMs = Math.Max(positionMs, 0);
            var normalizedDurationMs = Math.Max(durationMs, 0);
            if (normalizedDurationMs > 0 && normalizedPositionMs > normalizedDurationMs)
            {
                normalizedPositionMs = normalizedDurationMs;
            }

            if (normalizedPositionMs < MinimumResumePositionMs)
            {
                return 0;
            }

            if (normalizedDurationMs > 0)
            {
                var remainingMs = normalizedDurationMs - normalizedPositionMs;
                if (remainingMs <= CompletionRemainingThresholdMs)
                {
                    return 0;
                }
            }

            return normalizedPositionMs;
        }

        public static double ComputeProgressPercent(long positionMs, long durationMs, bool isWatched)
        {
            if (isWatched)
            {
                return 100;
            }

            if (durationMs <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, positionMs * 100d / durationMs));
        }
    }

    public interface ILibraryWatchStateService
    {
        Task<Dictionary<int, WatchProgressSnapshot>> LoadSnapshotsAsync(AppDbContext db, int profileId, PlaybackContentType contentType, IEnumerable<int> contentIds);
        Task UpsertProgressAsync(
            AppDbContext db,
            int profileId,
            PlaybackContentType contentType,
            int contentId,
            long positionMs,
            long durationMs,
            string? logicalContentKey = null,
            int preferredSourceProfileId = 0,
            DateTime? watchedAtUtc = null);
        Task MarkWatchedAsync(AppDbContext db, int profileId, PlaybackContentType contentType, int contentId);
        Task MarkUnwatchedAsync(AppDbContext db, int profileId, PlaybackContentType contentType, int contentId);
        Task<bool> GetHideWatchedInContinueAsync(AppDbContext db, int profileId);
        Task SetHideWatchedInContinueAsync(AppDbContext db, int profileId, bool value);
        Task<bool> GetHideWatchedEpisodesAsync(AppDbContext db, int profileId);
        Task SetHideWatchedEpisodesAsync(AppDbContext db, int profileId, bool value);
        SeriesQueueSelection? BuildSeriesQueueSelection(Series series, IReadOnlyDictionary<int, WatchProgressSnapshot> episodeSnapshots, bool includeWatched);
    }

    public sealed class LibraryWatchStateService : ILibraryWatchStateService
    {
        private const string ContinueHideWatchedKeyPrefix = "ContinueWatching.HideWatched.Profile.";
        private const string SeriesHideWatchedKeyPrefix = "Series.HideWatched.Profile.";

        public async Task<Dictionary<int, WatchProgressSnapshot>> LoadSnapshotsAsync(
            AppDbContext db,
            int profileId,
            PlaybackContentType contentType,
            IEnumerable<int> contentIds)
        {
            var ids = contentIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, WatchProgressSnapshot>();
            }

            var progressRows = await db.PlaybackProgresses
                .AsNoTracking()
                .Where(progress => progress.ProfileId == profileId &&
                                   progress.ContentType == contentType &&
                                   ids.Contains(progress.ContentId))
                .ToListAsync();

            return progressRows.ToDictionary(progress => progress.ContentId, CreateSnapshot);
        }

        public async Task UpsertProgressAsync(
            AppDbContext db,
            int profileId,
            PlaybackContentType contentType,
            int contentId,
            long positionMs,
            long durationMs,
            string? logicalContentKey = null,
            int preferredSourceProfileId = 0,
            DateTime? watchedAtUtc = null)
        {
            if (profileId <= 0 || contentId <= 0)
            {
                return;
            }

            var normalizedPositionMs = Math.Max(positionMs, 0);
            var normalizedDurationMs = Math.Max(durationMs, 0);
            if (normalizedDurationMs > 0 && normalizedPositionMs > normalizedDurationMs)
            {
                normalizedPositionMs = normalizedDurationMs;
            }

            if (contentType == PlaybackContentType.Channel)
            {
                normalizedPositionMs = 0;
                normalizedDurationMs = 0;
            }

            var isCompleted = WatchStateRules.ComputeCompleted(normalizedPositionMs, normalizedDurationMs);
            if (contentType != PlaybackContentType.Channel &&
                !isCompleted &&
                normalizedPositionMs < WatchStateRules.MinimumSavedPositionMs)
            {
                return;
            }

            var normalizedLogicalKey = string.IsNullOrWhiteSpace(logicalContentKey)
                ? string.Empty
                : logicalContentKey.Trim();

            PlaybackProgress? progress = null;
            if (!string.IsNullOrWhiteSpace(normalizedLogicalKey))
            {
                progress = await db.PlaybackProgresses.FirstOrDefaultAsync(existing =>
                    existing.ProfileId == profileId &&
                    existing.ContentType == contentType &&
                    existing.LogicalContentKey == normalizedLogicalKey);
            }

            progress ??= await db.PlaybackProgresses.FirstOrDefaultAsync(existing =>
                existing.ProfileId == profileId &&
                existing.ContentType == contentType &&
                existing.ContentId == contentId);

            var timestampUtc = watchedAtUtc ?? DateTime.UtcNow;
            if (progress == null)
            {
                progress = new PlaybackProgress
                {
                    ProfileId = profileId,
                    ContentType = contentType
                };
                db.PlaybackProgresses.Add(progress);
            }

            progress.ContentId = contentId;
            if (!string.IsNullOrWhiteSpace(normalizedLogicalKey))
            {
                progress.LogicalContentKey = normalizedLogicalKey;
            }

            if (preferredSourceProfileId > 0)
            {
                progress.PreferredSourceProfileId = preferredSourceProfileId;
            }

            progress.PositionMs = normalizedPositionMs;
            progress.DurationMs = normalizedDurationMs;
            progress.IsCompleted = isCompleted;
            progress.WatchStateOverride = WatchStateOverride.None;
            progress.LastWatched = timestampUtc;
            progress.CompletedAtUtc = isCompleted ? timestampUtc : null;
            progress.ResolvedAtUtc = timestampUtc;
            await db.SaveChangesAsync();
        }

        public async Task MarkWatchedAsync(AppDbContext db, int profileId, PlaybackContentType contentType, int contentId)
        {
            if (contentType == PlaybackContentType.Channel || contentId <= 0)
            {
                return;
            }

            var progress = await db.PlaybackProgresses.FirstOrDefaultAsync(existing =>
                existing.ProfileId == profileId &&
                existing.ContentType == contentType &&
                existing.ContentId == contentId);

            if (progress == null)
            {
                progress = new PlaybackProgress
                {
                    ProfileId = profileId,
                    ContentType = contentType,
                    ContentId = contentId
                };
                db.PlaybackProgresses.Add(progress);
            }

            progress.PositionMs = 0;
            progress.IsCompleted = true;
            progress.WatchStateOverride = WatchStateOverride.Watched;
            progress.LastWatched = DateTime.UtcNow;
            progress.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task MarkUnwatchedAsync(AppDbContext db, int profileId, PlaybackContentType contentType, int contentId)
        {
            if (contentType == PlaybackContentType.Channel || contentId <= 0)
            {
                return;
            }

            var progress = await db.PlaybackProgresses.FirstOrDefaultAsync(existing =>
                existing.ProfileId == profileId &&
                existing.ContentType == contentType &&
                existing.ContentId == contentId);

            if (progress == null)
            {
                progress = new PlaybackProgress
                {
                    ProfileId = profileId,
                    ContentType = contentType,
                    ContentId = contentId
                };
                db.PlaybackProgresses.Add(progress);
            }

            progress.PositionMs = 0;
            progress.IsCompleted = false;
            progress.WatchStateOverride = WatchStateOverride.Unwatched;
            progress.LastWatched = DateTime.UtcNow;
            progress.CompletedAtUtc = null;
            await db.SaveChangesAsync();
        }

        public Task<bool> GetHideWatchedInContinueAsync(AppDbContext db, int profileId)
        {
            return GetBooleanSettingAsync(db, ContinueHideWatchedKeyPrefix, profileId, defaultValue: true);
        }

        public Task SetHideWatchedInContinueAsync(AppDbContext db, int profileId, bool value)
        {
            return SetBooleanSettingAsync(db, ContinueHideWatchedKeyPrefix, profileId, value);
        }

        public Task<bool> GetHideWatchedEpisodesAsync(AppDbContext db, int profileId)
        {
            return GetBooleanSettingAsync(db, SeriesHideWatchedKeyPrefix, profileId, defaultValue: false);
        }

        public Task SetHideWatchedEpisodesAsync(AppDbContext db, int profileId, bool value)
        {
            return SetBooleanSettingAsync(db, SeriesHideWatchedKeyPrefix, profileId, value);
        }

        public SeriesQueueSelection? BuildSeriesQueueSelection(
            Series series,
            IReadOnlyDictionary<int, WatchProgressSnapshot> episodeSnapshots,
            bool includeWatched)
        {
            var orderedEpisodes = (series.Seasons ?? Array.Empty<Season>())
                .OrderBy(season => season.SeasonNumber)
                .SelectMany(season => (season.Episodes ?? Array.Empty<Episode>())
                    .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                    .OrderBy(episode => episode.EpisodeNumber)
                    .Select(episode => new { Season = season, Episode = episode }))
                .ToList();

            if (orderedEpisodes.Count == 0)
            {
                return null;
            }

            var episodesWithSnapshots = orderedEpisodes
                .Select(item => new
                {
                    item.Season,
                    item.Episode,
                    Snapshot = episodeSnapshots.TryGetValue(item.Episode.Id, out var snapshot) ? snapshot : null
                })
                .ToList();

            if (!episodesWithSnapshots.Any(item => item.Snapshot != null))
            {
                return null;
            }

            var watchedCount = episodesWithSnapshots.Count(item => item.Snapshot?.IsWatched == true);
            var partialResume = episodesWithSnapshots
                .Where(item => item.Snapshot?.HasResumePoint == true && item.Snapshot.IsWatched == false)
                .OrderByDescending(item => item.Snapshot!.LastWatched)
                .ThenBy(item => item.Season.SeasonNumber)
                .ThenBy(item => item.Episode.EpisodeNumber)
                .FirstOrDefault();

            if (partialResume != null)
            {
                return new SeriesQueueSelection
                {
                    Series = series,
                    Season = partialResume.Season,
                    Episode = partialResume.Episode,
                    EpisodeSnapshot = partialResume.Snapshot,
                    TotalEpisodeCount = orderedEpisodes.Count,
                    WatchedEpisodeCount = watchedCount,
                    IsWatched = false,
                    IsResumeCandidate = true,
                    ResumePositionMs = partialResume.Snapshot!.ResumePositionMs,
                    SortAtUtc = partialResume.Snapshot.LastWatched
                };
            }

            var nextEpisode = episodesWithSnapshots.FirstOrDefault(item => item.Snapshot?.IsWatched != true);
            if (nextEpisode != null)
            {
                var sortAtUtc = episodesWithSnapshots
                    .Where(item => item.Snapshot != null)
                    .Select(item => item.Snapshot!.LastWatched)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

                return new SeriesQueueSelection
                {
                    Series = series,
                    Season = nextEpisode.Season,
                    Episode = nextEpisode.Episode,
                    EpisodeSnapshot = nextEpisode.Snapshot,
                    TotalEpisodeCount = orderedEpisodes.Count,
                    WatchedEpisodeCount = watchedCount,
                    IsWatched = false,
                    IsResumeCandidate = nextEpisode.Snapshot?.HasResumePoint == true,
                    ResumePositionMs = nextEpisode.Snapshot?.ResumePositionMs ?? 0,
                    SortAtUtc = sortAtUtc
                };
            }

            if (!includeWatched)
            {
                return null;
            }

            var latestWatched = episodesWithSnapshots
                .Where(item => item.Snapshot != null)
                .OrderByDescending(item => item.Snapshot!.CompletedAtUtc ?? item.Snapshot.LastWatched)
                .FirstOrDefault();

            if (latestWatched == null)
            {
                return null;
            }

            return new SeriesQueueSelection
            {
                Series = series,
                Season = latestWatched.Season,
                Episode = latestWatched.Episode,
                EpisodeSnapshot = latestWatched.Snapshot,
                TotalEpisodeCount = orderedEpisodes.Count,
                WatchedEpisodeCount = watchedCount,
                IsWatched = true,
                IsResumeCandidate = false,
                ResumePositionMs = 0,
                SortAtUtc = latestWatched.Snapshot?.CompletedAtUtc ?? latestWatched.Snapshot?.LastWatched ?? DateTime.MinValue
            };
        }

        private static WatchProgressSnapshot CreateSnapshot(PlaybackProgress progress)
        {
            var isWatched = WatchStateRules.IsWatched(progress);
            var resumePositionMs = WatchStateRules.NormalizeResumePosition(progress.PositionMs, progress.DurationMs, isWatched);
            return new WatchProgressSnapshot
            {
                ProgressId = progress.Id,
                ContentId = progress.ContentId,
                ContentType = progress.ContentType,
                PositionMs = progress.PositionMs,
                DurationMs = progress.DurationMs,
                LogicalContentKey = progress.LogicalContentKey,
                PreferredSourceProfileId = progress.PreferredSourceProfileId,
                IsCompleted = progress.IsCompleted,
                WatchStateOverride = progress.WatchStateOverride,
                LastWatched = progress.LastWatched,
                CompletedAtUtc = progress.CompletedAtUtc,
                IsWatched = isWatched,
                ResumePositionMs = resumePositionMs,
                HasResumePoint = resumePositionMs >= WatchStateRules.MinimumResumePositionMs,
                ProgressPercent = WatchStateRules.ComputeProgressPercent(progress.PositionMs, progress.DurationMs, isWatched)
            };
        }

        private static async Task<bool> GetBooleanSettingAsync(AppDbContext db, string keyPrefix, int profileId, bool defaultValue)
        {
            var key = keyPrefix + profileId;
            var value = await db.AppSettings
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static async Task SetBooleanSettingAsync(AppDbContext db, string keyPrefix, int profileId, bool value)
        {
            var key = keyPrefix + profileId;
            var setting = await db.AppSettings.FirstOrDefaultAsync(existing => existing.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value.ToString()
                });
            }
            else
            {
                setting.Value = value.ToString();
            }

            await db.SaveChangesAsync();
        }
    }
}
