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
    public interface ILogicalCatalogStateService
    {
        string BuildChannelLogicalKey(Channel channel);
        string BuildMovieLogicalKey(Movie movie);
        string BuildSeriesLogicalKey(Series series);
        Task EnsureLaunchContextLogicalStateAsync(AppDbContext db, PlaybackLaunchContext context);
        Task<bool> IsFavoritedAsync(AppDbContext db, int profileId, FavoriteType contentType, int contentId);
        Task<bool> ToggleFavoriteAsync(AppDbContext db, int profileId, FavoriteType contentType, int contentId);
        Task<HashSet<string>> GetFavoriteLogicalKeysAsync(AppDbContext db, int profileId, FavoriteType contentType);
        Task ReconcileFavoritesAsync(AppDbContext db, int? profileId = null);
        Task ReconcilePlaybackProgressAsync(AppDbContext db, int? profileId = null);
        Task ReconcilePersistentStateAsync(AppDbContext db, int? profileId = null);
        Task RecordLiveChannelLaunchAsync(AppDbContext db, int profileId, int channelId);
    }

    public sealed class LogicalCatalogStateService : ILogicalCatalogStateService
    {
        private readonly ILiveChannelIdentityService _liveChannelIdentityService;
        private readonly IBrowsePreferencesService _browsePreferencesService;

        public LogicalCatalogStateService(
            ILiveChannelIdentityService liveChannelIdentityService,
            IBrowsePreferencesService browsePreferencesService)
        {
            _liveChannelIdentityService = liveChannelIdentityService;
            _browsePreferencesService = browsePreferencesService;
        }

        public string BuildChannelLogicalKey(Channel channel)
        {
            if (!string.IsNullOrWhiteSpace(channel.NormalizedIdentityKey))
            {
                return channel.NormalizedIdentityKey.Trim();
            }

            var identity = _liveChannelIdentityService.Build(
                channel.Name,
                string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId) ? channel.EpgChannelId : channel.ProviderEpgChannelId);
            return identity.IdentityKey;
        }

        public string BuildMovieLogicalKey(Movie movie)
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

        public string BuildSeriesLogicalKey(Series series)
        {
            if (!string.IsNullOrWhiteSpace(series.DedupFingerprint))
            {
                return series.DedupFingerprint.Trim();
            }

            if (!string.IsNullOrWhiteSpace(series.CanonicalTitleKey))
            {
                return $"series:title:{series.CanonicalTitleKey.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(series.ExternalId))
            {
                return $"series:external:{series.SourceProfileId}:{series.ExternalId.Trim()}";
            }

            return $"series:raw:{series.SourceProfileId}:{NormalizeToken(series.Title)}";
        }

        public async Task EnsureLaunchContextLogicalStateAsync(AppDbContext db, PlaybackLaunchContext context)
        {
            if (context == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(context.LogicalContentKey) && context.PreferredSourceProfileId >= 0)
            {
                return;
            }

            switch (context.ContentType)
            {
                case PlaybackContentType.Channel:
                {
                    var channel = await db.Channels
                        .AsNoTracking()
                        .Join(
                            db.ChannelCategories.AsNoTracking(),
                            item => item.ChannelCategoryId,
                            category => category.Id,
                            (item, category) => new { Channel = item, category.SourceProfileId })
                        .Where(item => item.Channel.Id == context.ContentId)
                        .FirstOrDefaultAsync();
                    if (channel != null)
                    {
                        context.LogicalContentKey = BuildChannelLogicalKey(channel.Channel);
                        context.PreferredSourceProfileId = channel.SourceProfileId;
                    }
                    break;
                }
                case PlaybackContentType.Movie:
                {
                    var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == context.ContentId);
                    if (movie != null)
                    {
                        context.LogicalContentKey = BuildMovieLogicalKey(movie);
                        context.PreferredSourceProfileId = movie.SourceProfileId;
                    }
                    break;
                }
                case PlaybackContentType.Episode:
                {
                    var episode = await db.Episodes
                        .AsNoTracking()
                        .Join(
                            db.Seasons.AsNoTracking(),
                            item => item.SeasonId,
                            season => season.Id,
                            (item, season) => new { Episode = item, Season = season })
                        .Join(
                            db.Series.AsNoTracking(),
                            item => item.Season.SeriesId,
                            series => series.Id,
                            (item, series) => new { item.Episode, item.Season, Series = series })
                        .Where(item => item.Episode.Id == context.ContentId)
                        .FirstOrDefaultAsync();
                    if (episode != null)
                    {
                        context.LogicalContentKey = BuildEpisodeLogicalKey(episode.Series, episode.Season, episode.Episode);
                        context.PreferredSourceProfileId = episode.Series.SourceProfileId;
                    }
                    break;
                }
            }
        }

        public async Task<bool> IsFavoritedAsync(AppDbContext db, int profileId, FavoriteType contentType, int contentId)
        {
            var target = await ResolveFavoriteTargetAsync(db, contentType, contentId, null, 0, contentId);
            if (target == null || string.IsNullOrWhiteSpace(target.LogicalContentKey))
            {
                return false;
            }

            return await db.Favorites.AnyAsync(favorite =>
                favorite.ProfileId == profileId &&
                favorite.ContentType == contentType &&
                (favorite.LogicalContentKey == target.LogicalContentKey ||
                 string.IsNullOrWhiteSpace(favorite.LogicalContentKey) && favorite.ContentId == contentId));
        }

        public async Task<bool> ToggleFavoriteAsync(AppDbContext db, int profileId, FavoriteType contentType, int contentId)
        {
            var target = await ResolveFavoriteTargetAsync(db, contentType, contentId, null, 0, contentId);
            if (target == null || string.IsNullOrWhiteSpace(target.LogicalContentKey))
            {
                return false;
            }

            var existing = await db.Favorites
                .Where(favorite => favorite.ProfileId == profileId && favorite.ContentType == contentType)
                .Where(favorite =>
                    favorite.LogicalContentKey == target.LogicalContentKey ||
                    (string.IsNullOrWhiteSpace(favorite.LogicalContentKey) && favorite.ContentId == contentId))
                .ToListAsync();

            if (existing.Count > 0)
            {
                db.Favorites.RemoveRange(existing);
                await db.SaveChangesAsync();
                return false;
            }

            db.Favorites.Add(new Favorite
            {
                ProfileId = profileId,
                ContentType = contentType,
                ContentId = target.ContentId,
                LogicalContentKey = target.LogicalContentKey,
                PreferredSourceProfileId = target.PreferredSourceProfileId,
                ResolvedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<HashSet<string>> GetFavoriteLogicalKeysAsync(AppDbContext db, int profileId, FavoriteType contentType)
        {
            await ReconcileFavoritesAsync(db, profileId);
            var keys = await db.Favorites
                .AsNoTracking()
                .Where(favorite => favorite.ProfileId == profileId && favorite.ContentType == contentType)
                .Select(favorite => favorite.LogicalContentKey)
                .Where(value => value != null && value != string.Empty)
                .ToListAsync();

            return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public async Task ReconcileFavoritesAsync(AppDbContext db, int? profileId = null)
        {
            await BackfillChannelLogicalStateAsync(db);

            var favorites = await db.Favorites
                .Where(favorite => !profileId.HasValue || favorite.ProfileId == profileId.Value)
                .OrderBy(favorite => favorite.ProfileId)
                .ThenBy(favorite => favorite.ContentType)
                .ThenBy(favorite => favorite.Id)
                .ToListAsync();
            if (favorites.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var favorite in favorites)
            {
                var target = await ResolveFavoriteTargetAsync(
                    db,
                    favorite.ContentType,
                    favorite.ContentId,
                    favorite.LogicalContentKey,
                    favorite.PreferredSourceProfileId,
                    favorite.ContentId);

                if (target == null)
                {
                    continue;
                }

                if (!string.Equals(favorite.LogicalContentKey, target.LogicalContentKey, StringComparison.Ordinal))
                {
                    favorite.LogicalContentKey = target.LogicalContentKey;
                    changed = true;
                }

                if (favorite.PreferredSourceProfileId != target.PreferredSourceProfileId)
                {
                    favorite.PreferredSourceProfileId = target.PreferredSourceProfileId;
                    changed = true;
                }

                if (favorite.ContentId != target.ContentId)
                {
                    favorite.ContentId = target.ContentId;
                    changed = true;
                }

                favorite.ResolvedAtUtc = DateTime.UtcNow;
            }

            foreach (var group in favorites
                         .GroupBy(BuildFavoriteGroupKey, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                var keeper = group
                    .OrderByDescending(item => item.ContentId > 0)
                    .ThenByDescending(item => item.ResolvedAtUtc ?? DateTime.MinValue)
                    .ThenBy(item => item.Id)
                    .First();
                var stale = group.Where(item => item.Id != keeper.Id).ToList();
                if (stale.Count > 0)
                {
                    db.Favorites.RemoveRange(stale);
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }

        public async Task ReconcilePlaybackProgressAsync(AppDbContext db, int? profileId = null)
        {
            await BackfillChannelLogicalStateAsync(db);

            var progressRows = await db.PlaybackProgresses
                .Where(progress => !profileId.HasValue || progress.ProfileId == profileId.Value)
                .OrderBy(progress => progress.ProfileId)
                .ThenBy(progress => progress.ContentType)
                .ThenBy(progress => progress.Id)
                .ToListAsync();
            if (progressRows.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var progress in progressRows)
            {
                ResolvedLogicalTarget? target;
                try
                {
                    target = await ResolvePlaybackTargetAsync(
                        db,
                        progress.ContentType,
                        progress.ContentId,
                        progress.LogicalContentKey,
                        progress.PreferredSourceProfileId,
                        progress.ContentId);
                }
                catch
                {
                    continue;
                }

                if (target == null)
                {
                    continue;
                }

                if (!string.Equals(progress.LogicalContentKey, target.LogicalContentKey, StringComparison.Ordinal))
                {
                    progress.LogicalContentKey = target.LogicalContentKey;
                    changed = true;
                }

                if (progress.PreferredSourceProfileId != target.PreferredSourceProfileId)
                {
                    progress.PreferredSourceProfileId = target.PreferredSourceProfileId;
                    changed = true;
                }

                if (progress.ContentId != target.ContentId)
                {
                    progress.ContentId = target.ContentId;
                    changed = true;
                }

                progress.ResolvedAtUtc = DateTime.UtcNow;
            }

            foreach (var group in progressRows
                         .GroupBy(BuildProgressGroupKey, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                var ordered = group
                    .OrderByDescending(progress => progress.LastWatched)
                    .ThenByDescending(progress => progress.IsCompleted)
                    .ThenByDescending(progress => progress.PositionMs)
                    .ThenBy(progress => progress.Id)
                    .ToList();
                var keeper = ordered[0];
                foreach (var duplicate in ordered.Skip(1))
                {
                    MergeProgressInto(keeper, duplicate);
                }

                var stale = ordered.Skip(1).ToList();
                if (stale.Count > 0)
                {
                    db.PlaybackProgresses.RemoveRange(stale);
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }

        public async Task ReconcilePersistentStateAsync(AppDbContext db, int? profileId = null)
        {
            await BackfillChannelLogicalStateAsync(db);
            await ReconcileFavoritesAsync(db, profileId);
            await ReconcilePlaybackProgressAsync(db, profileId);
        }

        public async Task RecordLiveChannelLaunchAsync(AppDbContext db, int profileId, int channelId)
        {
            var channel = await db.Channels
                .Join(
                    db.ChannelCategories,
                    item => item.ChannelCategoryId,
                    category => category.Id,
                    (item, category) => new
                    {
                        Channel = item,
                        category.SourceProfileId
                    })
                .FirstOrDefaultAsync(item => item.Channel.Id == channelId);
            if (channel == null)
            {
                return;
            }

            var logicalKey = BuildChannelLogicalKey(channel.Channel);
            if (string.IsNullOrWhiteSpace(logicalKey))
            {
                return;
            }

            var preferences = await _browsePreferencesService.GetAsync(db, ProfileDomains.Live, profileId);
            var reference = new BrowseChannelReference
            {
                LogicalKey = logicalKey,
                PreferredSourceProfileId = channel.SourceProfileId
            };

            preferences.LastChannel = reference;
            preferences.LastChannelId = channelId;
            preferences.RecentChannels.RemoveAll(item => string.Equals(item.LogicalKey, logicalKey, StringComparison.OrdinalIgnoreCase));
            preferences.RecentChannels.Insert(0, reference);
            if (preferences.RecentChannels.Count > 10)
            {
                preferences.RecentChannels = preferences.RecentChannels.Take(10).ToList();
            }

            preferences.RecentChannelIds.RemoveAll(id => id == channelId);
            preferences.RecentChannelIds.Insert(0, channelId);
            if (preferences.RecentChannelIds.Count > 10)
            {
                preferences.RecentChannelIds = preferences.RecentChannelIds.Take(10).ToList();
            }

            preferences.LiveChannelWatchCountsByKey[logicalKey] =
                (preferences.LiveChannelWatchCountsByKey.TryGetValue(logicalKey, out var watchCountByKey) ? watchCountByKey : 0) + 1;
            preferences.LiveChannelWatchCounts[channelId] =
                (preferences.LiveChannelWatchCounts.TryGetValue(channelId, out var watchCountById) ? watchCountById : 0) + 1;

            await _browsePreferencesService.SaveAsync(db, ProfileDomains.Live, profileId, preferences);

            var progress = await db.PlaybackProgresses.FirstOrDefaultAsync(item =>
                item.ProfileId == profileId &&
                item.ContentType == PlaybackContentType.Channel &&
                item.LogicalContentKey == logicalKey);

            if (progress == null)
            {
                progress = new PlaybackProgress
                {
                    ProfileId = profileId,
                    ContentType = PlaybackContentType.Channel,
                    ContentId = channelId,
                    LogicalContentKey = logicalKey,
                    PreferredSourceProfileId = channel.SourceProfileId,
                    PositionMs = 0,
                    DurationMs = 0,
                    IsCompleted = false,
                    WatchStateOverride = WatchStateOverride.None,
                    LastWatched = DateTime.UtcNow,
                    ResolvedAtUtc = DateTime.UtcNow
                };
                db.PlaybackProgresses.Add(progress);
            }
            else
            {
                progress.ContentId = channelId;
                progress.PreferredSourceProfileId = channel.SourceProfileId;
                progress.LastWatched = DateTime.UtcNow;
                progress.IsCompleted = false;
                progress.WatchStateOverride = WatchStateOverride.None;
                progress.CompletedAtUtc = null;
                progress.ResolvedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        private async Task<ResolvedLogicalTarget?> ResolveFavoriteTargetAsync(
            AppDbContext db,
            FavoriteType contentType,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            return contentType switch
            {
                FavoriteType.Channel => await ResolveChannelTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                FavoriteType.Movie => await ResolveMovieTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                FavoriteType.Series => await ResolveSeriesTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                _ => null
            };
        }

        private async Task<ResolvedLogicalTarget?> ResolvePlaybackTargetAsync(
            AppDbContext db,
            PlaybackContentType contentType,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            return contentType switch
            {
                PlaybackContentType.Channel => await ResolveChannelTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                PlaybackContentType.Movie => await ResolveMovieTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                PlaybackContentType.Episode => await ResolveEpisodeTargetAsync(db, contentId, logicalKey, preferredSourceProfileId, currentContentId),
                _ => null
            };
        }

        private async Task<ResolvedLogicalTarget?> ResolveChannelTargetAsync(
            AppDbContext db,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            var currentRow = contentId > 0
                ? await db.Channels
                    .AsNoTracking()
                    .Join(
                        db.ChannelCategories.AsNoTracking(),
                        item => item.ChannelCategoryId,
                        category => category.Id,
                        (item, category) => new
                        {
                            Channel = item,
                            category.SourceProfileId
                        })
                    .FirstOrDefaultAsync(item => item.Channel.Id == contentId)
                : null;

            var current = currentRow == null
                ? null
                : BuildChannelCandidate(currentRow.Channel, currentRow.SourceProfileId);

            var resolvedKey = !string.IsNullOrWhiteSpace(logicalKey)
                ? logicalKey.Trim()
                : current?.LogicalKey ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                return null;
            }

            if (current != null &&
                current.Id > 0 &&
                string.Equals(current.LogicalKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedLogicalTarget(current.Id, resolvedKey, current.SourceProfileId);
            }

            var candidateRows = await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    item => item.ChannelCategoryId,
                    category => category.Id,
                    (item, category) => new
                    {
                        Channel = item,
                        category.SourceProfileId
                    })
                .Where(item => item.Channel.NormalizedIdentityKey == resolvedKey)
                .ToListAsync();

            var candidates = candidateRows
                .Select(item => BuildChannelCandidate(item.Channel, item.SourceProfileId))
                .Where(item => string.Equals(item.LogicalKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var best = candidates
                .OrderByDescending(item => item.Id == currentContentId)
                .ThenByDescending(item => item.SourceProfileId == preferredSourceProfileId)
                .ThenByDescending(item => item.HasGuideMatch)
                .ThenByDescending(item => item.HasLogo)
                .ThenByDescending(item => item.SupportsCatchup)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            return best == null ? null : new ResolvedLogicalTarget(best.Id, resolvedKey, best.SourceProfileId);
        }

        private async Task<ResolvedLogicalTarget?> ResolveMovieTargetAsync(
            AppDbContext db,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            var movie = contentId > 0
                ? await db.Movies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == contentId)
                : null;
            var resolvedKey = !string.IsNullOrWhiteSpace(logicalKey)
                ? logicalKey.Trim()
                : movie == null ? string.Empty : BuildMovieLogicalKey(movie);
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                return null;
            }

            if (movie != null &&
                string.Equals(BuildMovieLogicalKey(movie), resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedLogicalTarget(movie.Id, resolvedKey, movie.SourceProfileId);
            }

            var candidates = await QueryMovieCandidatesAsync(db, resolvedKey);
            var best = candidates
                .OrderByDescending(item => item.Id == currentContentId)
                .ThenByDescending(item => item.SourceProfileId == preferredSourceProfileId)
                .ThenByDescending(item => item.HasPoster)
                .ThenByDescending(item => item.HasOverview)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            return best == null ? null : new ResolvedLogicalTarget(best.Id, resolvedKey, best.SourceProfileId);
        }

        private async Task<ResolvedLogicalTarget?> ResolveSeriesTargetAsync(
            AppDbContext db,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            var series = contentId > 0
                ? await db.Series.AsNoTracking().FirstOrDefaultAsync(item => item.Id == contentId)
                : null;
            var resolvedKey = !string.IsNullOrWhiteSpace(logicalKey)
                ? logicalKey.Trim()
                : series == null ? string.Empty : BuildSeriesLogicalKey(series);
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                return null;
            }

            if (series != null &&
                string.Equals(BuildSeriesLogicalKey(series), resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedLogicalTarget(series.Id, resolvedKey, series.SourceProfileId);
            }

            var candidates = await QuerySeriesCandidatesAsync(db, resolvedKey);
            var best = candidates
                .OrderByDescending(item => item.Id == currentContentId)
                .ThenByDescending(item => item.SourceProfileId == preferredSourceProfileId)
                .ThenByDescending(item => item.HasPoster)
                .ThenByDescending(item => item.HasOverview)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            return best == null ? null : new ResolvedLogicalTarget(best.Id, resolvedKey, best.SourceProfileId);
        }

        private async Task<ResolvedLogicalTarget?> ResolveEpisodeTargetAsync(
            AppDbContext db,
            int contentId,
            string? logicalKey,
            int preferredSourceProfileId,
            int currentContentId)
        {
            var current = contentId > 0
                ? await db.Episodes
                    .AsNoTracking()
                    .Join(
                        db.Seasons.AsNoTracking(),
                        item => item.SeasonId,
                        season => season.Id,
                        (item, season) => new { Episode = item, Season = season })
                    .Join(
                        db.Series.AsNoTracking(),
                        item => item.Season.SeriesId,
                        series => series.Id,
                        (item, series) => new { item.Episode, item.Season, Series = series })
                    .FirstOrDefaultAsync(item => item.Episode.Id == contentId)
                : null;

            var resolvedKey = !string.IsNullOrWhiteSpace(logicalKey)
                ? logicalKey.Trim()
                : current == null ? string.Empty : BuildEpisodeLogicalKey(current.Series, current.Season, current.Episode);
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                return null;
            }

            if (current != null &&
                string.Equals(BuildEpisodeLogicalKey(current.Series, current.Season, current.Episode), resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedLogicalTarget(current.Episode.Id, resolvedKey, current.Series.SourceProfileId);
            }

            var candidates = await QueryEpisodeCandidatesAsync(db, resolvedKey);
            var best = candidates
                .OrderByDescending(item => item.Id == currentContentId)
                .ThenByDescending(item => item.SourceProfileId == preferredSourceProfileId)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            return best == null ? null : new ResolvedLogicalTarget(best.Id, resolvedKey, best.SourceProfileId);
        }

        private async Task<List<MovieCandidate>> QueryMovieCandidatesAsync(AppDbContext db, string logicalKey)
        {
            if (logicalKey.StartsWith("movie:title:", StringComparison.OrdinalIgnoreCase))
            {
                var canonicalKey = logicalKey["movie:title:".Length..];
                return await db.Movies
                    .AsNoTracking()
                    .Where(item => item.CanonicalTitleKey == canonicalKey)
                    .Select(item => new MovieCandidate(
                        item.Id,
                        item.SourceProfileId,
                        !string.IsNullOrWhiteSpace(item.DisplayPosterUrl),
                        !string.IsNullOrWhiteSpace(item.Overview)))
                    .ToListAsync();
            }

            return await db.Movies
                .AsNoTracking()
                .Where(item => item.DedupFingerprint == logicalKey)
                .Select(item => new MovieCandidate(
                    item.Id,
                    item.SourceProfileId,
                    !string.IsNullOrWhiteSpace(item.DisplayPosterUrl),
                    !string.IsNullOrWhiteSpace(item.Overview)))
                .ToListAsync();
        }

        private async Task<List<SeriesCandidate>> QuerySeriesCandidatesAsync(AppDbContext db, string logicalKey)
        {
            if (logicalKey.StartsWith("series:title:", StringComparison.OrdinalIgnoreCase))
            {
                var canonicalKey = logicalKey["series:title:".Length..];
                return await db.Series
                    .AsNoTracking()
                    .Where(item => item.CanonicalTitleKey == canonicalKey)
                    .Select(item => new SeriesCandidate(
                        item.Id,
                        item.SourceProfileId,
                        !string.IsNullOrWhiteSpace(item.DisplayPosterUrl),
                        !string.IsNullOrWhiteSpace(item.Overview)))
                    .ToListAsync();
            }

            return await db.Series
                .AsNoTracking()
                .Where(item => item.DedupFingerprint == logicalKey)
                .Select(item => new SeriesCandidate(
                    item.Id,
                    item.SourceProfileId,
                    !string.IsNullOrWhiteSpace(item.DisplayPosterUrl),
                    !string.IsNullOrWhiteSpace(item.Overview)))
                .ToListAsync();
        }

        private async Task<List<EpisodeCandidate>> QueryEpisodeCandidatesAsync(AppDbContext db, string logicalKey)
        {
            const string externalPrefix = "episode:external:";
            if (logicalKey.StartsWith(externalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var externalId = logicalKey[externalPrefix.Length..];
                return await db.Episodes
                    .AsNoTracking()
                    .Where(item => item.ExternalId == externalId)
                    .Join(
                        db.Seasons.AsNoTracking(),
                        item => item.SeasonId,
                        season => season.Id,
                        (item, season) => new { Episode = item, Season = season })
                    .Join(
                        db.Series.AsNoTracking(),
                        item => item.Season.SeriesId,
                        series => series.Id,
                        (item, series) => new EpisodeCandidate(
                            item.Episode.Id,
                            series.SourceProfileId,
                            logicalKey))
                    .ToListAsync();
            }

            if (!TryParseEpisodeLogicalKey(logicalKey, out var seriesKey, out var seasonNumber, out var episodeNumber, out var episodeTitleKey))
            {
                return new List<EpisodeCandidate>();
            }

            var candidateSeries = await QuerySeriesCandidatesAsync(db, seriesKey);
            if (candidateSeries.Count == 0)
            {
                return new List<EpisodeCandidate>();
            }

            var seriesIds = candidateSeries.Select(item => item.Id).ToHashSet();
            var rows = await db.Episodes
                .AsNoTracking()
                .Join(
                    db.Seasons.AsNoTracking(),
                    item => item.SeasonId,
                    season => season.Id,
                    (item, season) => new { Episode = item, Season = season })
                .Join(
                    db.Series.AsNoTracking(),
                    item => item.Season.SeriesId,
                    series => series.Id,
                    (item, series) => new
                    {
                        item.Episode,
                        item.Season,
                        Series = series
                    })
                .Where(item => seriesIds.Contains(item.Series.Id))
                .ToListAsync();

            return rows
                .Where(item =>
                    seasonNumber.HasValue && episodeNumber.HasValue
                        ? item.Season.SeasonNumber == seasonNumber.Value && item.Episode.EpisodeNumber == episodeNumber.Value
                        : string.Equals(NormalizeToken(item.Episode.Title), episodeTitleKey, StringComparison.OrdinalIgnoreCase))
                .Select(item => new EpisodeCandidate(
                    item.Episode.Id,
                    item.Series.SourceProfileId,
                    BuildEpisodeLogicalKey(item.Series, item.Season, item.Episode)))
                .Where(item => string.Equals(item.LogicalKey, logicalKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private async Task BackfillChannelLogicalStateAsync(AppDbContext db)
        {
            var channelsNeedingBackfill = await db.Channels
                .Where(item =>
                    string.IsNullOrWhiteSpace(item.NormalizedIdentityKey) ||
                    string.IsNullOrWhiteSpace(item.NormalizedName) ||
                    string.IsNullOrWhiteSpace(item.AliasKeys))
                .ToListAsync();

            if (channelsNeedingBackfill.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var channel in channelsNeedingBackfill)
            {
                var identity = _liveChannelIdentityService.Build(
                    channel.Name,
                    string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId) ? channel.EpgChannelId : channel.ProviderEpgChannelId);

                if (string.IsNullOrWhiteSpace(channel.NormalizedIdentityKey) && !string.IsNullOrWhiteSpace(identity.IdentityKey))
                {
                    channel.NormalizedIdentityKey = identity.IdentityKey;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(channel.NormalizedName) && !string.IsNullOrWhiteSpace(identity.NormalizedName))
                {
                    channel.NormalizedName = identity.NormalizedName;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(channel.AliasKeys) && identity.AliasKeys.Count > 0)
                {
                    channel.AliasKeys = string.Join("\n", identity.AliasKeys);
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }

        private string BuildEpisodeLogicalKey(Series series, Season season, Episode episode)
        {
            if (!string.IsNullOrWhiteSpace(episode.ExternalId))
            {
                return $"episode:external:{episode.ExternalId.Trim()}";
            }

            var seriesKey = BuildSeriesLogicalKey(series);
            if (season.SeasonNumber > 0 && episode.EpisodeNumber > 0)
            {
                return $"episode:{seriesKey}:s{season.SeasonNumber}:e{episode.EpisodeNumber}";
            }

            return $"episode:{seriesKey}:t:{NormalizeToken(episode.Title)}";
        }

        private static void MergeProgressInto(PlaybackProgress keeper, PlaybackProgress duplicate)
        {
            if (duplicate.LastWatched > keeper.LastWatched)
            {
                keeper.ContentId = duplicate.ContentId;
                keeper.PreferredSourceProfileId = duplicate.PreferredSourceProfileId;
                keeper.PositionMs = duplicate.PositionMs;
                keeper.DurationMs = duplicate.DurationMs;
                keeper.IsCompleted = duplicate.IsCompleted;
                keeper.WatchStateOverride = duplicate.WatchStateOverride;
                keeper.LastWatched = duplicate.LastWatched;
                keeper.CompletedAtUtc = duplicate.CompletedAtUtc;
                keeper.ResolvedAtUtc = duplicate.ResolvedAtUtc;
                return;
            }

            keeper.PositionMs = Math.Max(keeper.PositionMs, duplicate.PositionMs);
            keeper.DurationMs = Math.Max(keeper.DurationMs, duplicate.DurationMs);
            keeper.IsCompleted = keeper.IsCompleted || duplicate.IsCompleted;
            keeper.LastWatched = keeper.LastWatched >= duplicate.LastWatched ? keeper.LastWatched : duplicate.LastWatched;
            keeper.CompletedAtUtc = MaxDateTime(keeper.CompletedAtUtc, duplicate.CompletedAtUtc);
            keeper.ResolvedAtUtc = MaxDateTime(keeper.ResolvedAtUtc, duplicate.ResolvedAtUtc);
        }

        private static DateTime? MaxDateTime(DateTime? left, DateTime? right)
        {
            if (!left.HasValue)
            {
                return right;
            }

            if (!right.HasValue)
            {
                return left;
            }

            return left >= right ? left : right;
        }

        private static string BuildFavoriteGroupKey(Favorite favorite)
        {
            if (!string.IsNullOrWhiteSpace(favorite.LogicalContentKey))
            {
                return $"{favorite.ProfileId}:{favorite.ContentType}:{favorite.LogicalContentKey}";
            }

            return $"{favorite.ProfileId}:{favorite.ContentType}:row:{favorite.ContentId}";
        }

        private static string BuildProgressGroupKey(PlaybackProgress progress)
        {
            if (!string.IsNullOrWhiteSpace(progress.LogicalContentKey))
            {
                return $"{progress.ProfileId}:{progress.ContentType}:{progress.LogicalContentKey}";
            }

            return $"{progress.ProfileId}:{progress.ContentType}:row:{progress.ContentId}";
        }

        private static string NormalizeToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "unknown"
                : ContentClassifier.NormalizeLabel(value).Trim().ToLowerInvariant().Replace(' ', '_');
        }

        private ChannelCandidate BuildChannelCandidate(Channel channel, int sourceProfileId)
        {
            return new ChannelCandidate(
                channel.Id,
                sourceProfileId,
                BuildChannelLogicalKey(channel),
                !string.IsNullOrWhiteSpace(channel.LogoUrl),
                !string.IsNullOrWhiteSpace(channel.EpgChannelId),
                channel.SupportsCatchup);
        }

        private static bool TryParseEpisodeLogicalKey(
            string logicalKey,
            out string seriesKey,
            out int? seasonNumber,
            out int? episodeNumber,
            out string episodeTitleKey)
        {
            const string prefix = "episode:";
            seriesKey = string.Empty;
            seasonNumber = null;
            episodeNumber = null;
            episodeTitleKey = string.Empty;

            if (!logicalKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = logicalKey[prefix.Length..];
            var titleMarker = remainder.LastIndexOf(":t:", StringComparison.OrdinalIgnoreCase);
            if (titleMarker >= 0)
            {
                seriesKey = remainder[..titleMarker];
                episodeTitleKey = remainder[(titleMarker + 3)..];
                return !string.IsNullOrWhiteSpace(seriesKey) && !string.IsNullOrWhiteSpace(episodeTitleKey);
            }

            var seasonMarker = remainder.LastIndexOf(":s", StringComparison.OrdinalIgnoreCase);
            if (seasonMarker < 0)
            {
                return false;
            }

            var episodeMarker = remainder.IndexOf(":e", seasonMarker, StringComparison.OrdinalIgnoreCase);
            if (episodeMarker < 0)
            {
                return false;
            }

            seriesKey = remainder[..seasonMarker];
            var seasonValue = remainder[(seasonMarker + 2)..episodeMarker];
            var episodeValue = remainder[(episodeMarker + 2)..];
            if (!int.TryParse(seasonValue, out var parsedSeason) || !int.TryParse(episodeValue, out var parsedEpisode))
            {
                return false;
            }

            seasonNumber = parsedSeason;
            episodeNumber = parsedEpisode;
            return !string.IsNullOrWhiteSpace(seriesKey);
        }

        private sealed record ResolvedLogicalTarget(
            int ContentId,
            string LogicalContentKey,
            int PreferredSourceProfileId);

        private sealed record ChannelCandidate(
            int Id,
            int SourceProfileId,
            string LogicalKey,
            bool HasLogo,
            bool HasGuideMatch,
            bool SupportsCatchup);

        private sealed record MovieCandidate(
            int Id,
            int SourceProfileId,
            bool HasPoster,
            bool HasOverview);

        private sealed record SeriesCandidate(
            int Id,
            int SourceProfileId,
            bool HasPoster,
            bool HasOverview);

        private sealed record EpisodeCandidate(
            int Id,
            int SourceProfileId,
            string LogicalKey);
    }
}
