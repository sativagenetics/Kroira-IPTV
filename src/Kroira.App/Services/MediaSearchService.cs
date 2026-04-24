#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface IMediaSearchService
    {
        Task<MediaSearchResponse> SearchAsync(
            AppDbContext db,
            string? query,
            ProfileAccessSnapshot? access = null,
            int limitPerGroup = 12,
            CancellationToken cancellationToken = default);
    }

    public sealed class MediaSearchService : IMediaSearchService
    {
        private const int DefaultLimitPerGroup = 12;

        public async Task<MediaSearchResponse> SearchAsync(
            AppDbContext db,
            string? query,
            ProfileAccessSnapshot? access = null,
            int limitPerGroup = DefaultLimitPerGroup,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawQuery = query?.Trim() ?? string.Empty;
            var normalizedQuery = NormalizeQuery(rawQuery);
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new MediaSearchResponse(rawQuery, normalizedQuery, CreateGroups(), isEmptyQuery: true);
            }

            limitPerGroup = Math.Clamp(limitPerGroup, 1, 50);
            var tokens = SplitTokens(normalizedQuery);
            var primaryToken = tokens.OrderByDescending(token => token.Length).FirstOrDefault() ?? normalizedQuery;

            var liveResults = await SearchLiveAsync(db, normalizedQuery, tokens, primaryToken, access, limitPerGroup, cancellationToken);
            var movieResults = await SearchMoviesAsync(db, normalizedQuery, tokens, primaryToken, access, limitPerGroup, cancellationToken);
            var seriesResults = await SearchSeriesAsync(db, normalizedQuery, tokens, primaryToken, access, limitPerGroup, cancellationToken);
            var episodeResults = await SearchEpisodesAsync(db, normalizedQuery, tokens, primaryToken, access, limitPerGroup, cancellationToken);

            var groups = CreateGroups(liveResults, movieResults, seriesResults, episodeResults);
            return new MediaSearchResponse(rawQuery, normalizedQuery, groups, isEmptyQuery: false);
        }

        public static string NormalizeQuery(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var compacted = ContentClassifier.NormalizeLabel(value);
            var decomposed = compacted.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character));
            }

            return ContentClassifier.NormalizeLabel(builder.ToString()).Trim();
        }

        private static IReadOnlyList<string> SplitTokens(string normalizedQuery)
        {
            return normalizedQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyList<MediaSearchResultGroup> CreateGroups(
            IReadOnlyList<MediaSearchResult>? live = null,
            IReadOnlyList<MediaSearchResult>? movies = null,
            IReadOnlyList<MediaSearchResult>? series = null,
            IReadOnlyList<MediaSearchResult>? episodes = null)
        {
            return new[]
            {
                new MediaSearchResultGroup(MediaSearchResultType.Live, "Live", live ?? Array.Empty<MediaSearchResult>()),
                new MediaSearchResultGroup(MediaSearchResultType.Movie, "Movies", movies ?? Array.Empty<MediaSearchResult>()),
                new MediaSearchResultGroup(MediaSearchResultType.Series, "Series", series ?? Array.Empty<MediaSearchResult>()),
                new MediaSearchResultGroup(MediaSearchResultType.Episode, "Episodes", episodes ?? Array.Empty<MediaSearchResult>())
            };
        }

        private static async Task<IReadOnlyList<MediaSearchResult>> SearchLiveAsync(
            AppDbContext db,
            string normalizedQuery,
            IReadOnlyList<string> tokens,
            string primaryToken,
            ProfileAccessSnapshot? access,
            int limit,
            CancellationToken cancellationToken)
        {
            var candidates = await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new { channel, category })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.category.SourceProfileId,
                    source => source.Id,
                    (item, source) => new { item.channel, item.category, source })
                .Where(item =>
                    item.channel.Name.ToLower().Contains(primaryToken) ||
                    item.category.Name.ToLower().Contains(primaryToken) ||
                    item.source.Name.ToLower().Contains(primaryToken))
                .Select(item => new LiveCandidate(
                        item.channel.Id,
                        item.channel.Name,
                        item.channel.StreamUrl,
                        item.channel.LogoUrl,
                        item.channel.NormalizedIdentityKey,
                        item.channel.ProviderEpgChannelId,
                        item.channel.EpgChannelId,
                        item.category.Id,
                        item.category.Name,
                        item.category.SourceProfileId,
                        item.source.Name))
                .Take(500)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return candidates
                .Where(candidate => IsLiveAllowed(candidate, access))
                .Select(candidate =>
                {
                    var score = Score(normalizedQuery, tokens, candidate.Title, candidate.CategoryName, candidate.SourceName);
                    return new { candidate, score };
                })
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.candidate.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(item => new MediaSearchResult
                {
                    Type = MediaSearchResultType.Live,
                    PlaybackContentType = PlaybackContentType.Channel,
                    ContentId = item.candidate.Id,
                    SourceProfileId = item.candidate.SourceProfileId,
                    Title = item.candidate.Title,
                    Subtitle = FormatBadgeLine(item.candidate.CategoryName, item.candidate.SourceName),
                    SourceName = item.candidate.SourceName,
                    SourceBadge = ResolveSourceBadge(item.candidate.SourceName, item.candidate.SourceProfileId),
                    CategoryName = item.candidate.CategoryName,
                    CategoryBadge = ResolveCategoryBadge(item.candidate.CategoryName),
                    ArtworkUrl = item.candidate.LogoUrl,
                    StreamUrl = item.candidate.StreamUrl,
                    LogicalContentKey = ResolveChannelLogicalKey(item.candidate),
                    RelevanceScore = item.score
                })
                .ToList();
        }

        private static async Task<IReadOnlyList<MediaSearchResult>> SearchMoviesAsync(
            AppDbContext db,
            string normalizedQuery,
            IReadOnlyList<string> tokens,
            string primaryToken,
            ProfileAccessSnapshot? access,
            int limit,
            CancellationToken cancellationToken)
        {
            var candidates = await db.Movies
                .AsNoTracking()
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    movie => movie.SourceProfileId,
                    source => source.Id,
                    (movie, source) => new { movie, source })
                .Where(item =>
                    item.movie.Title.ToLower().Contains(primaryToken) ||
                    item.movie.CategoryName.ToLower().Contains(primaryToken) ||
                    item.movie.Genres.ToLower().Contains(primaryToken) ||
                    item.source.Name.ToLower().Contains(primaryToken))
                .Select(item => new MovieCandidate(
                        item.movie.Id,
                        item.movie.SourceProfileId,
                        item.movie.Title,
                        item.movie.StreamUrl,
                        item.movie.PosterUrl,
                        item.movie.Overview,
                        item.movie.Genres,
                        item.movie.CategoryName,
                        item.movie.RawSourceCategoryName,
                        item.movie.ContentKind,
                        item.movie.ReleaseDate,
                        item.movie.OriginalLanguage,
                        item.movie.DedupFingerprint,
                        item.movie.CanonicalTitleKey,
                        item.movie.ExternalId,
                        item.source.Name))
                .Take(500)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var resumeByMovieId = await LoadResumePositionsAsync(db, access?.ProfileId, PlaybackContentType.Movie, candidates.Select(item => item.Id), cancellationToken);

            return candidates
                .Where(candidate => IsMovieAllowed(candidate, access))
                .Select(candidate =>
                {
                    var score = Score(normalizedQuery, tokens, candidate.Title, candidate.CategoryName, candidate.Genres, candidate.SourceName, candidate.Overview);
                    return new { candidate, score };
                })
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.candidate.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(item => new MediaSearchResult
                {
                    Type = MediaSearchResultType.Movie,
                    PlaybackContentType = PlaybackContentType.Movie,
                    ContentId = item.candidate.Id,
                    SourceProfileId = item.candidate.SourceProfileId,
                    Title = item.candidate.Title,
                    Subtitle = BuildMovieSubtitle(item.candidate),
                    Overview = item.candidate.Overview,
                    SourceName = item.candidate.SourceName,
                    SourceBadge = ResolveSourceBadge(item.candidate.SourceName, item.candidate.SourceProfileId),
                    CategoryName = item.candidate.CategoryName,
                    CategoryBadge = ResolveCategoryBadge(item.candidate.CategoryName),
                    ArtworkUrl = item.candidate.ArtworkUrl,
                    StreamUrl = item.candidate.StreamUrl,
                    LogicalContentKey = ResolveMovieLogicalKey(item.candidate),
                    ResumePositionMs = resumeByMovieId.TryGetValue(item.candidate.Id, out var position) ? position : 0,
                    RelevanceScore = item.score
                })
                .ToList();
        }

        private static async Task<IReadOnlyList<MediaSearchResult>> SearchSeriesAsync(
            AppDbContext db,
            string normalizedQuery,
            IReadOnlyList<string> tokens,
            string primaryToken,
            ProfileAccessSnapshot? access,
            int limit,
            CancellationToken cancellationToken)
        {
            var candidates = await db.Series
                .AsNoTracking()
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    series => series.SourceProfileId,
                    source => source.Id,
                    (series, source) => new { series, source })
                .Where(item =>
                    item.series.Title.ToLower().Contains(primaryToken) ||
                    item.series.CategoryName.ToLower().Contains(primaryToken) ||
                    item.series.Genres.ToLower().Contains(primaryToken) ||
                    item.source.Name.ToLower().Contains(primaryToken))
                .Select(item => new SeriesCandidate(
                    item.series.Id,
                    item.series.SourceProfileId,
                    item.series.Title,
                    item.series.PosterUrl,
                    item.series.Overview,
                    item.series.Genres,
                    item.series.CategoryName,
                    item.series.RawSourceCategoryName,
                    item.series.ContentKind,
                    item.series.FirstAirDate,
                    item.series.OriginalLanguage,
                    item.series.DedupFingerprint,
                    item.series.CanonicalTitleKey,
                    item.series.ExternalId,
                    item.source.Name))
                .Take(500)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return candidates
                .Where(candidate => IsSeriesAllowed(candidate, access))
                .Select(candidate =>
                {
                    var score = Score(normalizedQuery, tokens, candidate.Title, candidate.CategoryName, candidate.Genres, candidate.SourceName, candidate.Overview);
                    return new { candidate, score };
                })
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.candidate.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(item => new MediaSearchResult
                {
                    Type = MediaSearchResultType.Series,
                    ContentId = item.candidate.Id,
                    SourceProfileId = item.candidate.SourceProfileId,
                    SeriesId = item.candidate.Id,
                    Title = item.candidate.Title,
                    Subtitle = BuildSeriesSubtitle(item.candidate),
                    Overview = item.candidate.Overview,
                    SourceName = item.candidate.SourceName,
                    SourceBadge = ResolveSourceBadge(item.candidate.SourceName, item.candidate.SourceProfileId),
                    CategoryName = item.candidate.CategoryName,
                    CategoryBadge = ResolveCategoryBadge(item.candidate.CategoryName),
                    ArtworkUrl = item.candidate.ArtworkUrl,
                    LogicalContentKey = ResolveSeriesLogicalKey(item.candidate),
                    RelevanceScore = item.score
                })
                .ToList();
        }

        private static async Task<IReadOnlyList<MediaSearchResult>> SearchEpisodesAsync(
            AppDbContext db,
            string normalizedQuery,
            IReadOnlyList<string> tokens,
            string primaryToken,
            ProfileAccessSnapshot? access,
            int limit,
            CancellationToken cancellationToken)
        {
            var candidates = await db.Episodes
                .AsNoTracking()
                .Join(
                    db.Seasons.AsNoTracking(),
                    episode => episode.SeasonId,
                    season => season.Id,
                    (episode, season) => new { episode, season })
                .Join(
                    db.Series.AsNoTracking(),
                    item => item.season.SeriesId,
                    series => series.Id,
                    (item, series) => new { item.episode, item.season, series })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.series.SourceProfileId,
                    source => source.Id,
                    (item, source) => new { item.episode, item.season, item.series, source })
                .Where(item =>
                    item.episode.Title.ToLower().Contains(primaryToken) ||
                    item.series.Title.ToLower().Contains(primaryToken) ||
                    item.series.CategoryName.ToLower().Contains(primaryToken) ||
                    item.series.Genres.ToLower().Contains(primaryToken) ||
                    item.source.Name.ToLower().Contains(primaryToken))
                .Select(item => new EpisodeCandidate(
                    item.episode.Id,
                    item.episode.SeasonId,
                    item.season.SeasonNumber,
                    item.episode.EpisodeNumber,
                    item.series.Id,
                    item.series.SourceProfileId,
                    item.episode.Title,
                    item.episode.StreamUrl,
                    item.series.Title,
                    item.series.CategoryName,
                    item.series.RawSourceCategoryName,
                    item.series.ContentKind,
                    item.series.Genres,
                    item.series.PosterUrl,
                    item.series.Overview,
                    item.series.DedupFingerprint,
                    item.series.CanonicalTitleKey,
                    item.series.ExternalId,
                    item.source.Name))
                .Take(500)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var resumeByEpisodeId = await LoadResumePositionsAsync(db, access?.ProfileId, PlaybackContentType.Episode, candidates.Select(item => item.Id), cancellationToken);

            return candidates
                .Where(candidate => IsEpisodeAllowed(candidate, access))
                .Select(candidate =>
                {
                    var score = Score(
                        normalizedQuery,
                        tokens,
                        candidate.Title,
                        candidate.SeriesTitle,
                        candidate.CategoryName,
                        candidate.Genres,
                        candidate.SourceName);
                    return new { candidate, score };
                })
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.candidate.SeriesTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.candidate.SeasonNumber)
                .ThenBy(item => item.candidate.EpisodeNumber)
                .Take(limit)
                .Select(item => new MediaSearchResult
                {
                    Type = MediaSearchResultType.Episode,
                    PlaybackContentType = PlaybackContentType.Episode,
                    ContentId = item.candidate.Id,
                    SourceProfileId = item.candidate.SourceProfileId,
                    SeriesId = item.candidate.SeriesId,
                    SeasonId = item.candidate.SeasonId,
                    SeasonNumber = item.candidate.SeasonNumber,
                    EpisodeNumber = item.candidate.EpisodeNumber,
                    Title = item.candidate.Title,
                    Subtitle = BuildEpisodeSubtitle(item.candidate),
                    Overview = item.candidate.SeriesOverview,
                    SourceName = item.candidate.SourceName,
                    SourceBadge = ResolveSourceBadge(item.candidate.SourceName, item.candidate.SourceProfileId),
                    CategoryName = item.candidate.CategoryName,
                    CategoryBadge = ResolveCategoryBadge(item.candidate.CategoryName),
                    ArtworkUrl = item.candidate.ArtworkUrl,
                    StreamUrl = item.candidate.StreamUrl,
                    LogicalContentKey = ResolveEpisodeLogicalKey(item.candidate),
                    ResumePositionMs = resumeByEpisodeId.TryGetValue(item.candidate.Id, out var position) ? position : 0,
                    RelevanceScore = item.score
                })
                .ToList();
        }

        private static async Task<Dictionary<int, long>> LoadResumePositionsAsync(
            AppDbContext db,
            int? profileId,
            PlaybackContentType contentType,
            IEnumerable<int> contentIds,
            CancellationToken cancellationToken)
        {
            var resolvedProfileId = profileId.GetValueOrDefault();
            if (resolvedProfileId <= 0)
            {
                return new Dictionary<int, long>();
            }

            var ids = contentIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, long>();
            }

            var rows = await db.PlaybackProgresses
                .AsNoTracking()
                .Where(progress =>
                    progress.ProfileId == resolvedProfileId &&
                    progress.ContentType == contentType &&
                    ids.Contains(progress.ContentId))
                .OrderByDescending(progress => progress.LastWatched)
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(progress => progress.ContentId)
                .ToDictionary(group => group.Key, group => group.First().PositionMs);
        }

        private static int Score(
            string normalizedQuery,
            IReadOnlyList<string> tokens,
            string title,
            params string[] secondaryFields)
        {
            var normalizedTitle = NormalizeQuery(title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return 0;
            }

            var score = 0;
            if (string.Equals(normalizedTitle, normalizedQuery, StringComparison.Ordinal))
            {
                score = Math.Max(score, 10000);
            }
            else if (normalizedTitle.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                score = Math.Max(score, 8500);
            }
            else if (ContainsPhrase(normalizedTitle, normalizedQuery))
            {
                score = Math.Max(score, 7000);
            }

            var titleTokenHits = tokens.Count(token => normalizedTitle.Contains(token, StringComparison.Ordinal));
            if (titleTokenHits == tokens.Count && tokens.Count > 0)
            {
                score = Math.Max(score, 5400 + titleTokenHits * 320);
            }
            else if (titleTokenHits > 0)
            {
                score = Math.Max(score, 2800 + titleTokenHits * 260);
            }

            var secondaryScore = 0;
            foreach (var secondary in secondaryFields)
            {
                var normalizedSecondary = NormalizeQuery(secondary);
                if (string.IsNullOrWhiteSpace(normalizedSecondary))
                {
                    continue;
                }

                if (ContainsPhrase(normalizedSecondary, normalizedQuery))
                {
                    secondaryScore = Math.Max(secondaryScore, 1900);
                    continue;
                }

                var secondaryHits = tokens.Count(token => normalizedSecondary.Contains(token, StringComparison.Ordinal));
                if (secondaryHits == tokens.Count && tokens.Count > 0)
                {
                    secondaryScore = Math.Max(secondaryScore, 1300 + secondaryHits * 140);
                }
                else if (secondaryHits > 0)
                {
                    secondaryScore = Math.Max(secondaryScore, 600 + secondaryHits * 100);
                }
            }

            return Math.Max(score, secondaryScore);
        }

        private static bool ContainsPhrase(string normalizedValue, string normalizedQuery)
        {
            return normalizedValue.Contains(normalizedQuery, StringComparison.Ordinal);
        }

        private static bool IsLiveAllowed(LiveCandidate candidate, ProfileAccessSnapshot? access)
        {
            if (access == null)
            {
                return true;
            }

            return access.IsLiveChannelAllowed(
                new Channel
                {
                    Id = candidate.Id,
                    Name = candidate.Title,
                    StreamUrl = candidate.StreamUrl,
                    NormalizedIdentityKey = candidate.NormalizedIdentityKey,
                    ProviderEpgChannelId = candidate.ProviderEpgChannelId,
                    EpgChannelId = candidate.EpgChannelId
                },
                new ChannelCategory
                {
                    Id = candidate.CategoryId,
                    SourceProfileId = candidate.SourceProfileId,
                    Name = candidate.CategoryName
                });
        }

        private static bool IsMovieAllowed(MovieCandidate candidate, ProfileAccessSnapshot? access)
        {
            if (access == null)
            {
                return true;
            }

            return access.IsMovieAllowed(new Movie
            {
                Id = candidate.Id,
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                CategoryName = candidate.CategoryName,
                RawSourceCategoryName = candidate.RawSourceCategoryName,
                ContentKind = candidate.ContentKind
            });
        }

        private static bool IsSeriesAllowed(SeriesCandidate candidate, ProfileAccessSnapshot? access)
        {
            if (access == null)
            {
                return true;
            }

            return access.IsSeriesAllowed(new Series
            {
                Id = candidate.Id,
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.Title,
                CategoryName = candidate.CategoryName,
                RawSourceCategoryName = candidate.RawSourceCategoryName,
                ContentKind = candidate.ContentKind
            });
        }

        private static bool IsEpisodeAllowed(EpisodeCandidate candidate, ProfileAccessSnapshot? access)
        {
            if (access == null)
            {
                return true;
            }

            return access.IsSeriesAllowed(new Series
            {
                Id = candidate.SeriesId,
                SourceProfileId = candidate.SourceProfileId,
                Title = candidate.SeriesTitle,
                CategoryName = candidate.CategoryName,
                RawSourceCategoryName = candidate.RawSourceCategoryName,
                ContentKind = candidate.ContentKind
            });
        }

        private static string ResolveLogoUrl(Channel channel)
        {
            if (!string.IsNullOrWhiteSpace(channel.LogoUrl))
            {
                return channel.LogoUrl;
            }

            return !string.IsNullOrWhiteSpace(channel.ProviderLogoUrl)
                ? channel.ProviderLogoUrl
                : string.Empty;
        }

        private static string ResolveSourceBadge(string sourceName, int sourceProfileId)
        {
            return string.IsNullOrWhiteSpace(sourceName)
                ? $"Source {sourceProfileId}"
                : sourceName.Trim();
        }

        private static string ResolveCategoryBadge(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName)
                ? "Uncategorized"
                : categoryName.Trim();
        }

        private static string FormatBadgeLine(string categoryName, string sourceName)
        {
            var parts = new[] { ResolveCategoryBadge(categoryName), ResolveSourceBadge(sourceName, 0) }
                .Where(part => !string.IsNullOrWhiteSpace(part) && !string.Equals(part, "Source 0", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(" / ", parts);
        }

        private static string BuildMovieSubtitle(MovieCandidate movie)
        {
            var parts = new[]
            {
                movie.ReleaseDate?.Year.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                string.IsNullOrWhiteSpace(movie.Genres) ? ResolveCategoryBadge(movie.CategoryName) : movie.Genres,
                string.IsNullOrWhiteSpace(movie.OriginalLanguage) ? string.Empty : movie.OriginalLanguage.ToUpperInvariant(),
                ResolveSourceBadge(movie.SourceName, movie.SourceProfileId)
            };

            return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildSeriesSubtitle(SeriesCandidate series)
        {
            var parts = new[]
            {
                series.FirstAirDate?.Year.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                string.IsNullOrWhiteSpace(series.Genres) ? ResolveCategoryBadge(series.CategoryName) : series.Genres,
                string.IsNullOrWhiteSpace(series.OriginalLanguage) ? string.Empty : series.OriginalLanguage.ToUpperInvariant(),
                ResolveSourceBadge(series.SourceName, series.SourceProfileId)
            };

            return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildEpisodeSubtitle(EpisodeCandidate episode)
        {
            var episodeCode = $"S{episode.SeasonNumber:00} E{episode.EpisodeNumber:00}";
            return $"{episode.SeriesTitle} / {episodeCode} / {ResolveSourceBadge(episode.SourceName, episode.SourceProfileId)}";
        }

        private static string ResolveChannelLogicalKey(LiveCandidate candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate.NormalizedIdentityKey))
            {
                return candidate.NormalizedIdentityKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(candidate.ProviderEpgChannelId))
            {
                return $"epg:{NormalizeQuery(candidate.ProviderEpgChannelId)}";
            }

            if (!string.IsNullOrWhiteSpace(candidate.EpgChannelId))
            {
                return $"epg:{NormalizeQuery(candidate.EpgChannelId)}";
            }

            return $"channel:raw:{NormalizeQuery(candidate.Title).Replace(' ', '_')}";
        }

        private static string ResolveMovieLogicalKey(MovieCandidate movie)
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

            return $"movie:raw:{movie.SourceProfileId}:{NormalizeQuery(movie.Title).Replace(' ', '_')}";
        }

        private static string ResolveSeriesLogicalKey(SeriesCandidate series)
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

            return $"series:raw:{series.SourceProfileId}:{NormalizeQuery(series.Title).Replace(' ', '_')}";
        }

        private static string ResolveEpisodeLogicalKey(EpisodeCandidate episode)
        {
            var seriesKey = !string.IsNullOrWhiteSpace(episode.SeriesDedupFingerprint)
                ? episode.SeriesDedupFingerprint.Trim()
                : !string.IsNullOrWhiteSpace(episode.SeriesCanonicalTitleKey)
                    ? $"series:title:{episode.SeriesCanonicalTitleKey.Trim()}"
                    : !string.IsNullOrWhiteSpace(episode.SeriesExternalId)
                        ? $"series:external:{episode.SourceProfileId}:{episode.SeriesExternalId.Trim()}"
                        : $"series:raw:{episode.SourceProfileId}:{NormalizeQuery(episode.SeriesTitle).Replace(' ', '_')}";

            return $"{seriesKey}:s{episode.SeasonNumber}:e{episode.EpisodeNumber}";
        }

        private sealed record LiveCandidate(
            int Id,
            string Title,
            string StreamUrl,
            string LogoUrl,
            string NormalizedIdentityKey,
            string ProviderEpgChannelId,
            string EpgChannelId,
            int CategoryId,
            string CategoryName,
            int SourceProfileId,
            string SourceName);

        private sealed record MovieCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string StreamUrl,
            string ArtworkUrl,
            string Overview,
            string Genres,
            string CategoryName,
            string RawSourceCategoryName,
            string ContentKind,
            DateTime? ReleaseDate,
            string OriginalLanguage,
            string DedupFingerprint,
            string CanonicalTitleKey,
            string ExternalId,
            string SourceName);

        private sealed record SeriesCandidate(
            int Id,
            int SourceProfileId,
            string Title,
            string ArtworkUrl,
            string Overview,
            string Genres,
            string CategoryName,
            string RawSourceCategoryName,
            string ContentKind,
            DateTime? FirstAirDate,
            string OriginalLanguage,
            string DedupFingerprint,
            string CanonicalTitleKey,
            string ExternalId,
            string SourceName);

        private sealed record EpisodeCandidate(
            int Id,
            int SeasonId,
            int SeasonNumber,
            int EpisodeNumber,
            int SeriesId,
            int SourceProfileId,
            string Title,
            string StreamUrl,
            string SeriesTitle,
            string CategoryName,
            string RawSourceCategoryName,
            string ContentKind,
            string Genres,
            string ArtworkUrl,
            string SeriesOverview,
            string SeriesDedupFingerprint,
            string SeriesCanonicalTitleKey,
            string SeriesExternalId,
            string SourceName);
    }
}
