#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public sealed class HomeRecommendationItem
    {
        public int ContentId { get; init; }
        public PlaybackContentType ContentType { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string StreamUrl { get; init; } = string.Empty;
        public string PosterUrl { get; init; } = string.Empty;
        public string BackdropUrl { get; init; } = string.Empty;
        public string Overview { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public double VoteAverage { get; init; }
        public double Popularity { get; init; }
        public int ArtworkScore { get; init; }
        public string Genres { get; init; } = string.Empty;
        public string PrimaryActionLabel { get; init; } = "Open";
    }

    public sealed class HomeRecommendationSnapshot
    {
        public HomeRecommendationItem? Featured { get; init; }
        public IReadOnlyList<HomeRecommendationItem> Recommended { get; init; } = Array.Empty<HomeRecommendationItem>();
        public IReadOnlyList<HomeRecommendationItem> RecentlyAdded { get; init; } = Array.Empty<HomeRecommendationItem>();
        public IReadOnlyList<HomeRecommendationItem> TopRated { get; init; } = Array.Empty<HomeRecommendationItem>();
    }

    public interface IHomeRecommendationService
    {
        Task<HomeRecommendationSnapshot> BuildAsync(AppDbContext db, ProfileAccessSnapshot access);
    }

    public sealed class HomeRecommendationService : IHomeRecommendationService
    {
        private enum RecommendationBuildStep
        {
            LoadMovieGroups,
            FilterMovieGroups,
            LoadSeriesGroups,
            FilterSeriesGroups,
            LoadFavoriteMovieIds,
            LoadFavoriteSeriesIds,
            LoadMovieSnapshots,
            LoadEpisodeSnapshots,
            BuildMovieStates,
            BuildSeriesStates,
            BuildAffinity,
            BuildFeatured,
            BuildRecommended,
            BuildRecentlyAdded,
            BuildTopRated
        }

        private sealed class RecommendationBuildContext
        {
            public required AppDbContext Db { get; init; }
            public required ProfileAccessSnapshot Access { get; init; }
            public IReadOnlyList<CatalogMovieGroup> MovieGroups { get; set; } = Array.Empty<CatalogMovieGroup>();
            public IReadOnlyList<CatalogSeriesGroup> SeriesGroups { get; set; } = Array.Empty<CatalogSeriesGroup>();
            public HashSet<int> FavoriteMovieIds { get; set; } = new();
            public HashSet<int> FavoriteSeriesIds { get; set; } = new();
            public Dictionary<int, WatchProgressSnapshot> MovieSnapshotMap { get; set; } = new();
            public Dictionary<int, WatchProgressSnapshot> EpisodeSnapshotMap { get; set; } = new();
            public List<MovieState> MovieStates { get; set; } = new();
            public List<SeriesState> SeriesStates { get; set; } = new();
            public Dictionary<string, double> Affinity { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public HomeRecommendationItem? Featured { get; set; }
            public IReadOnlyList<HomeRecommendationItem> Recommended { get; set; } = Array.Empty<HomeRecommendationItem>();
            public IReadOnlyList<HomeRecommendationItem> RecentlyAdded { get; set; } = Array.Empty<HomeRecommendationItem>();
            public IReadOnlyList<HomeRecommendationItem> TopRated { get; set; } = Array.Empty<HomeRecommendationItem>();
        }

        private sealed record ScoredRecommendationItem(HomeRecommendationItem Item, double Score);

        private static readonly string StartupLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-log.txt");

        private readonly ICatalogDeduplicationService _catalogDeduplicationService;
        private readonly ILibraryWatchStateService _watchStateService;
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 3);

        public HomeRecommendationService(
            ICatalogDeduplicationService catalogDeduplicationService,
            ILibraryWatchStateService watchStateService)
        {
            _catalogDeduplicationService = catalogDeduplicationService;
            _watchStateService = watchStateService;
        }

        public async Task<HomeRecommendationSnapshot> BuildAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            LogCheckpoint("HOMEREC 01: BuildAsync entered");
            var enabledSteps = GetEnabledBuildSteps();

            var context = new RecommendationBuildContext
            {
                Db = db,
                Access = access
            };

            await RunBuildStepAsync(RecommendationBuildStep.LoadMovieGroups, enabledSteps, async () =>
            {
                context.MovieGroups = await _catalogDeduplicationService.LoadMovieGroupsAsync(db);
            });

            await RunBuildStepAsync(RecommendationBuildStep.FilterMovieGroups, enabledSteps, () =>
            {
                context.MovieGroups = context.MovieGroups
                    .Where(group => access.IsMovieAllowed(group.PreferredMovie))
                    .Where(group => IsBrowsableMovie(group.PreferredMovie))
                    .ToList();
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.LoadSeriesGroups, enabledSteps, async () =>
            {
                context.SeriesGroups = await _catalogDeduplicationService.LoadSeriesGroupsAsync(db);
            });

            await RunBuildStepAsync(RecommendationBuildStep.FilterSeriesGroups, enabledSteps, () =>
            {
                context.SeriesGroups = context.SeriesGroups
                    .Where(group => access.IsSeriesAllowed(group.PreferredSeries))
                    .Where(group => IsBrowsableSeries(group.PreferredSeries))
                    .ToList();
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.LoadFavoriteMovieIds, enabledSteps, async () =>
            {
                context.FavoriteMovieIds = (await db.Favorites
                    .Where(favorite => favorite.ProfileId == access.ProfileId && favorite.ContentType == FavoriteType.Movie)
                    .Select(favorite => favorite.ContentId)
                    .ToListAsync())
                    .ToHashSet();
            });

            await RunBuildStepAsync(RecommendationBuildStep.LoadFavoriteSeriesIds, enabledSteps, async () =>
            {
                context.FavoriteSeriesIds = (await db.Favorites
                    .Where(favorite => favorite.ProfileId == access.ProfileId && favorite.ContentType == FavoriteType.Series)
                    .Select(favorite => favorite.ContentId)
                    .ToListAsync())
                    .ToHashSet();
            });

            await RunBuildStepAsync(RecommendationBuildStep.LoadMovieSnapshots, enabledSteps, async () =>
            {
                context.MovieSnapshotMap = await _watchStateService.LoadSnapshotsAsync(
                    db,
                    access.ProfileId,
                    PlaybackContentType.Movie,
                    context.MovieGroups.SelectMany(group => group.Variants).Select(variant => variant.Movie.Id));
            });

            await RunBuildStepAsync(RecommendationBuildStep.LoadEpisodeSnapshots, enabledSteps, async () =>
            {
                context.EpisodeSnapshotMap = await _watchStateService.LoadSnapshotsAsync(
                    db,
                    access.ProfileId,
                    PlaybackContentType.Episode,
                    context.SeriesGroups.SelectMany(group => group.Variants)
                        .SelectMany(variant => variant.Series.Seasons ?? Array.Empty<Season>())
                        .SelectMany(season => season.Episodes ?? Array.Empty<Episode>())
                        .Select(episode => episode.Id));
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildMovieStates, enabledSteps, () =>
            {
                context.MovieStates = context.MovieGroups
                    .Select(group => BuildMovieState(group, context.FavoriteMovieIds, context.MovieSnapshotMap))
                    .ToList();
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildSeriesStates, enabledSteps, () =>
            {
                context.SeriesStates = context.SeriesGroups
                    .Select(group => BuildSeriesState(group, context.FavoriteSeriesIds, context.EpisodeSnapshotMap))
                    .ToList();
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildAffinity, enabledSteps, () =>
            {
                context.Affinity = BuildAffinity(context.MovieStates, context.SeriesStates);
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildFeatured, enabledSteps, () =>
            {
                context.Featured = BuildFeaturedItem(context.MovieStates, context.SeriesStates, context.Affinity);
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildRecommended, enabledSteps, () =>
            {
                context.Recommended = BuildRecommendedItems(context.MovieStates, context.SeriesStates, context.Affinity);
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildRecentlyAdded, enabledSteps, () =>
            {
                context.RecentlyAdded = BuildRecentlyAddedItems(context.MovieStates, context.SeriesStates);
                return Task.CompletedTask;
            });

            await RunBuildStepAsync(RecommendationBuildStep.BuildTopRated, enabledSteps, () =>
            {
                context.TopRated = BuildTopRatedItems(context.MovieStates, context.SeriesStates);
                return Task.CompletedTask;
            });

            LogCheckpoint("HOMEREC 99: BuildAsync completed");
            return new HomeRecommendationSnapshot
            {
                Featured = context.Featured,
                Recommended = context.Recommended,
                RecentlyAdded = context.RecentlyAdded,
                TopRated = context.TopRated
            };
        }

        private static async Task RunBuildStepAsync(
            RecommendationBuildStep step,
            ISet<RecommendationBuildStep> enabledSteps,
            Func<Task> action)
        {
            if (!enabledSteps.Contains(step))
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            if (ShouldLogStep(step))
            {
                LogCheckpoint($"HOMEREC STEP {step}: start");
            }

            try
            {
                await action();
                stopwatch.Stop();
                if (ShouldLogStep(step))
                {
                    LogCheckpoint($"HOMEREC STEP {step}: end ({stopwatch.ElapsedMilliseconds} ms)");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogCheckpoint($"HOMEREC STEP {step}: error after {stopwatch.ElapsedMilliseconds} ms - {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static HashSet<RecommendationBuildStep> GetEnabledBuildSteps()
        {
            var raw = Environment.GetEnvironmentVariable("KROIRA_HOME_RECOMMENDATION_STEPS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Enum.GetValues<RecommendationBuildStep>().ToHashSet();
            }

            var enabled = new HashSet<RecommendationBuildStep>();
            foreach (var token in raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.GetValues<RecommendationBuildStep>().ToHashSet();
                }

                if (Enum.TryParse<RecommendationBuildStep>(token, true, out var step))
                {
                    enabled.Add(step);
                }
            }

            return enabled;
        }

        private static bool ShouldLogStep(RecommendationBuildStep step)
        {
            return step == RecommendationBuildStep.BuildRecommended;
        }

        private static void LogCheckpoint(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                Debug.WriteLine(line);
                File.AppendAllText(StartupLogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private HomeRecommendationItem? BuildFeaturedItem(
            IReadOnlyList<MovieState> movieStates,
            IReadOnlyList<SeriesState> seriesStates,
            IReadOnlyDictionary<string, double> affinity)
        {
            var candidates = new List<(HomeRecommendationItem Item, double Score)>();

            candidates.AddRange(movieStates
                .Where(state => IsFeaturedSafe(state.Group))
                .Select(state =>
                {
                    var score = ScoreFeaturedMovie(state, affinity);
                    return (BuildMovieItem(state, BuildRecommendationDetail(state.Preferred, state.LastAffinityTag), "Movies", "Play movie"), score);
                }));

            candidates.AddRange(seriesStates
                .Where(state => IsFeaturedSafe(state.Group))
                .Select(state =>
                {
                    var score = ScoreFeaturedSeries(state, affinity);
                    return (BuildSeriesItem(state, BuildRecommendationDetail(state.Preferred, state.LastAffinityTag), "Series", "View series"), score);
                }));

            var ranked = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Item.ArtworkScore)
                .ThenByDescending(candidate => candidate.Item.Popularity)
                .Take(3)
                .ToList();

            if (ranked.Count == 0)
            {
                return null;
            }

            return ranked[_sessionRotationIndex % ranked.Count].Item;
        }

        private IReadOnlyList<HomeRecommendationItem> BuildRecommendedItems(
            IReadOnlyList<MovieState> movieStates,
            IReadOnlyList<SeriesState> seriesStates,
            IReadOnlyDictionary<string, double> affinity)
        {
            var movieItems = movieStates
                .Select(state =>
                {
                    var score = ScoreRecommendedMovie(state, affinity);
                    return new ScoredRecommendationItem(
                        BuildMovieItem(state, BuildRecommendationReason(state.Preferred, state.HasResume, state.IsFavorite, state.LastAffinityTag), "Movies", "Open"),
                        score);
                })
                .OrderByDescending(item => item.Score)
                .Take(8)
                .ToList();

            var seriesItems = seriesStates
                .Select(state =>
                {
                    var score = ScoreRecommendedSeries(state, affinity);
                    return new ScoredRecommendationItem(
                        BuildSeriesItem(state, BuildRecommendationReason(state.Preferred, state.HasResume, state.IsFavorite, state.LastAffinityTag), "Series", "Open"),
                        score);
                })
                .OrderByDescending(item => item.Score)
                .Take(8)
                .ToList();

            var interleaved = InterleaveRecommendedItems(movieItems, seriesItems, limit: 10);

            return interleaved
                .DistinctBy(item => $"{item.ContentType}:{item.ContentId}")
                .Take(10)
                .ToList();
        }

        private static List<HomeRecommendationItem> InterleaveRecommendedItems(
            IReadOnlyList<ScoredRecommendationItem> movieItems,
            IReadOnlyList<ScoredRecommendationItem> seriesItems,
            int limit)
        {
            var results = new List<HomeRecommendationItem>();
            var movieIndex = 0;
            var seriesIndex = 0;
            var preferMovie = ShouldStartWithMovie(movieItems, seriesItems);

            while (results.Count < limit && (movieIndex < movieItems.Count || seriesIndex < seriesItems.Count))
            {
                var added = TryAddRecommendedItem(results, movieItems, ref movieIndex, seriesItems, ref seriesIndex, preferMovie);
                if (!added)
                {
                    added = TryAddRecommendedItem(results, movieItems, ref movieIndex, seriesItems, ref seriesIndex, !preferMovie);
                }

                if (!added)
                {
                    LogCheckpoint("HOMEREC BUILDRECOMMENDED: interleave stopped without progress");
                    break;
                }

                preferMovie = !preferMovie;
            }

            return results;
        }

        private static bool ShouldStartWithMovie(
            IReadOnlyList<ScoredRecommendationItem> movieItems,
            IReadOnlyList<ScoredRecommendationItem> seriesItems)
        {
            if (movieItems.Count == 0)
            {
                return false;
            }

            if (seriesItems.Count == 0)
            {
                return true;
            }

            return movieItems[0].Score >= seriesItems[0].Score;
        }

        private static bool TryAddRecommendedItem(
            ICollection<HomeRecommendationItem> results,
            IReadOnlyList<ScoredRecommendationItem> movieItems,
            ref int movieIndex,
            IReadOnlyList<ScoredRecommendationItem> seriesItems,
            ref int seriesIndex,
            bool preferMovie)
        {
            if (preferMovie)
            {
                if (movieIndex >= movieItems.Count)
                {
                    return false;
                }

                results.Add(movieItems[movieIndex++].Item);
                return true;
            }

            if (seriesIndex >= seriesItems.Count)
            {
                return false;
            }

            results.Add(seriesItems[seriesIndex++].Item);
            return true;
        }

        private static IReadOnlyList<HomeRecommendationItem> BuildRecentlyAddedItems(
            IReadOnlyList<MovieState> movieStates,
            IReadOnlyList<SeriesState> seriesStates)
        {
            return movieStates
                .Select(state => new { Item = BuildMovieItem(state, BuildMetadataLine(state.Preferred), "Movies", "Open"), SortKey = state.Group.Variants.Max(variant => variant.Movie.Id) })
                .Concat(seriesStates.Select(state => new { Item = BuildSeriesItem(state, BuildMetadataLine(state.Preferred), "Series", "Open"), SortKey = state.Group.Variants.Max(variant => variant.Series.Id) }))
                .OrderByDescending(item => item.SortKey)
                .Select(item => item.Item)
                .Take(10)
                .ToList();
        }

        private static IReadOnlyList<HomeRecommendationItem> BuildTopRatedItems(
            IReadOnlyList<MovieState> movieStates,
            IReadOnlyList<SeriesState> seriesStates)
        {
            return movieStates
                .Select(state => new { Item = BuildMovieItem(state, BuildMetadataLine(state.Preferred), "Movies", "Open"), Score = state.Preferred.VoteAverage * 10 + state.Preferred.Popularity })
                .Concat(seriesStates.Select(state => new { Item = BuildSeriesItem(state, BuildMetadataLine(state.Preferred), "Series", "Open"), Score = state.Preferred.VoteAverage * 10 + state.Preferred.Popularity }))
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Item.ArtworkScore)
                .Select(item => item.Item)
                .Take(10)
                .ToList();
        }

        private static MovieState BuildMovieState(
            CatalogMovieGroup group,
            IReadOnlySet<int> favoriteMovieIds,
            IReadOnlyDictionary<int, WatchProgressSnapshot> movieSnapshotMap)
        {
            var snapshots = group.Variants
                .Select(variant => movieSnapshotMap.TryGetValue(variant.Movie.Id, out var snapshot) ? snapshot : null)
                .Where(snapshot => snapshot != null)
                .Cast<WatchProgressSnapshot>()
                .ToList();

            return new MovieState
            {
                Group = group,
                IsFavorite = group.Variants.Any(variant => favoriteMovieIds.Contains(variant.Movie.Id)),
                IsWatched = snapshots.Any(snapshot => snapshot.IsWatched),
                HasResume = snapshots.Any(snapshot => snapshot.HasResumePoint),
                LastInteractionUtc = snapshots.Select(snapshot => snapshot.CompletedAtUtc ?? snapshot.LastWatched).DefaultIfEmpty(DateTime.MinValue).Max()
            };
        }

        private SeriesState BuildSeriesState(
            CatalogSeriesGroup group,
            IReadOnlySet<int> favoriteSeriesIds,
            IReadOnlyDictionary<int, WatchProgressSnapshot> episodeSnapshotMap)
        {
            var queueSelections = group.Variants
                .Select(variant => _watchStateService.BuildSeriesQueueSelection(variant.Series, episodeSnapshotMap, includeWatched: true))
                .Where(selection => selection != null)
                .Cast<SeriesQueueSelection>()
                .ToList();

            var preferredSelection = queueSelections
                .OrderByDescending(selection => selection.IsResumeCandidate)
                .ThenBy(selection => selection.IsWatched)
                .ThenByDescending(selection => selection.SortAtUtc)
                .FirstOrDefault();

            return new SeriesState
            {
                Group = group,
                IsFavorite = group.Variants.Any(variant => favoriteSeriesIds.Contains(variant.Series.Id)),
                IsWatched = queueSelections.Count > 0 && queueSelections.All(selection => selection.IsWatched),
                HasResume = queueSelections.Any(selection => selection.IsResumeCandidate),
                LastInteractionUtc = queueSelections.Select(selection => selection.SortAtUtc).DefaultIfEmpty(DateTime.MinValue).Max(),
                Selection = preferredSelection
            };
        }

        private static Dictionary<string, double> BuildAffinity(
            IReadOnlyList<MovieState> movieStates,
            IReadOnlyList<SeriesState> seriesStates)
        {
            var affinity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var state in movieStates)
            {
                var weight = 0d;
                if (state.IsFavorite) weight += 4.5;
                if (state.HasResume) weight += 5.5;
                if (state.LastInteractionUtc > DateTime.MinValue) weight += ComputeRecencyWeight(state.LastInteractionUtc);
                if (weight <= 0) continue;
                AddTags(affinity, ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), weight);
            }

            foreach (var state in seriesStates)
            {
                var weight = 0d;
                if (state.IsFavorite) weight += 4.5;
                if (state.HasResume) weight += 6;
                if (state.LastInteractionUtc > DateTime.MinValue) weight += ComputeRecencyWeight(state.LastInteractionUtc);
                if (weight <= 0) continue;
                AddTags(affinity, ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), weight);
            }

            return affinity;
        }

        private static double ComputeRecencyWeight(DateTime lastInteractionUtc)
        {
            var days = Math.Max((DateTime.UtcNow - lastInteractionUtc).TotalDays, 0);
            return days switch
            {
                <= 3 => 4.5,
                <= 14 => 3,
                <= 45 => 2,
                _ => 1
            };
        }

        private static void AddTags(Dictionary<string, double> affinity, IEnumerable<string> tags, double weight)
        {
            foreach (var tag in tags)
            {
                affinity[tag] = affinity.TryGetValue(tag, out var current) ? current + weight : weight;
            }
        }

        private static IEnumerable<string> ExtractTags(string genres, string categoryName, string originalLanguage)
        {
            foreach (var value in SplitTags(genres))
            {
                if (!ContentClassifier.IsGarbageCategoryName(value) && !ContentClassifier.IsM3uBucketOrAdultLabel(value))
                {
                    yield return value;
                }
            }

            if (!string.IsNullOrWhiteSpace(categoryName) &&
                !ContentClassifier.IsGarbageCategoryName(categoryName) &&
                !ContentClassifier.IsM3uBucketOrAdultLabel(categoryName))
            {
                yield return categoryName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(originalLanguage))
            {
                yield return originalLanguage.Trim().ToUpperInvariant();
            }
        }

        private static IEnumerable<string> SplitTags(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', '/', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length > 1);
        }

        private static double ScoreRecommendedMovie(MovieState state, IReadOnlyDictionary<string, double> affinity)
        {
            var affinityScore = EvaluateAffinity(ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), affinity, out var topTag);
            state.LastAffinityTag = topTag;
            return BaseQualityScore(state.Preferred.VoteAverage, state.Preferred.Popularity, GetArtworkScore(state.Preferred), !string.IsNullOrWhiteSpace(state.Preferred.Overview), !string.IsNullOrWhiteSpace(state.Preferred.TmdbId))
                   + affinityScore * 4
                   + InteractionScore(state.IsFavorite, state.HasResume, state.IsWatched)
                   + FreshnessScore(state.Preferred.ReleaseDate?.Year);
        }

        private static double ScoreRecommendedSeries(SeriesState state, IReadOnlyDictionary<string, double> affinity)
        {
            var affinityScore = EvaluateAffinity(ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), affinity, out var topTag);
            state.LastAffinityTag = topTag;
            return BaseQualityScore(state.Preferred.VoteAverage, state.Preferred.Popularity, GetArtworkScore(state.Preferred), !string.IsNullOrWhiteSpace(state.Preferred.Overview), !string.IsNullOrWhiteSpace(state.Preferred.TmdbId))
                   + affinityScore * 4
                   + InteractionScore(state.IsFavorite, state.HasResume, state.IsWatched)
                   + FreshnessScore(state.Preferred.FirstAirDate?.Year)
                   + Math.Min(state.Group.Variants.Max(variant => variant.EpisodeCount), 24) * 0.6;
        }

        private static double ScoreFeaturedMovie(MovieState state, IReadOnlyDictionary<string, double> affinity)
        {
            var affinityScore = EvaluateAffinity(ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), affinity, out var topTag);
            state.LastAffinityTag = topTag;
            return BaseQualityScore(state.Preferred.VoteAverage, state.Preferred.Popularity, GetArtworkScore(state.Preferred), !string.IsNullOrWhiteSpace(state.Preferred.Overview), !string.IsNullOrWhiteSpace(state.Preferred.TmdbId))
                   + affinityScore * 5
                   + InteractionScore(state.IsFavorite, state.HasResume, state.IsWatched)
                   + FreshnessScore(state.Preferred.ReleaseDate?.Year)
                   + (GetArtworkScore(state.Preferred) >= 3 ? 18 : 0)
                   + (!string.IsNullOrWhiteSpace(state.Preferred.DisplayBackdropUrl) ? 12 : 0);
        }

        private static double ScoreFeaturedSeries(SeriesState state, IReadOnlyDictionary<string, double> affinity)
        {
            var affinityScore = EvaluateAffinity(ExtractTags(state.Preferred.Genres, state.Preferred.CategoryName, state.Preferred.OriginalLanguage), affinity, out var topTag);
            state.LastAffinityTag = topTag;
            return BaseQualityScore(state.Preferred.VoteAverage, state.Preferred.Popularity, GetArtworkScore(state.Preferred), !string.IsNullOrWhiteSpace(state.Preferred.Overview), !string.IsNullOrWhiteSpace(state.Preferred.TmdbId))
                   + affinityScore * 5
                   + InteractionScore(state.IsFavorite, state.HasResume, state.IsWatched)
                   + FreshnessScore(state.Preferred.FirstAirDate?.Year)
                   + (GetArtworkScore(state.Preferred) >= 3 ? 18 : 0)
                   + (!string.IsNullOrWhiteSpace(state.Preferred.DisplayBackdropUrl) ? 12 : 0)
                   + Math.Min(state.Group.Variants.Max(variant => variant.EpisodeCount), 18) * 0.5;
        }

        private static double EvaluateAffinity(IEnumerable<string> tags, IReadOnlyDictionary<string, double> affinity, out string topTag)
        {
            topTag = string.Empty;
            var total = 0d;
            var topScore = 0d;
            foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!affinity.TryGetValue(tag, out var score))
                {
                    continue;
                }

                total += score;
                if (score > topScore)
                {
                    topScore = score;
                    topTag = tag;
                }
            }

            return total;
        }

        private static double BaseQualityScore(double voteAverage, double popularity, int artworkScore, bool hasOverview, bool hasTmdb)
        {
            return voteAverage * 7
                   + Math.Min(popularity, 100) * 0.45
                   + artworkScore * 18
                   + (hasOverview ? 10 : 0)
                   + (hasTmdb ? 14 : 0);
        }

        private static double InteractionScore(bool isFavorite, bool hasResume, bool isWatched)
        {
            var score = 0d;
            if (isFavorite) score += 10;
            if (hasResume) score += 24;
            if (!isWatched) score += 8;
            if (isWatched && !hasResume) score -= 12;
            return score;
        }

        private static double FreshnessScore(int? year)
        {
            if (!year.HasValue)
            {
                return 0;
            }

            var age = DateTime.UtcNow.Year - year.Value;
            return age switch
            {
                <= 1 => 10,
                <= 3 => 8,
                <= 7 => 5,
                <= 15 => 2,
                _ => 0
            };
        }

        private static HomeRecommendationItem BuildMovieItem(MovieState state, string detail, string target, string actionLabel)
        {
            return new HomeRecommendationItem
            {
                ContentId = state.Preferred.Id,
                ContentType = PlaybackContentType.Movie,
                Title = state.Preferred.Title,
                Detail = detail,
                StreamUrl = state.Preferred.StreamUrl,
                PosterUrl = state.Preferred.DisplayPosterUrl,
                BackdropUrl = state.Preferred.DisplayHeroArtworkUrl,
                Overview = state.Preferred.Overview,
                Target = target,
                VoteAverage = state.Preferred.VoteAverage,
                Popularity = state.Preferred.Popularity,
                ArtworkScore = GetArtworkScore(state.Preferred),
                Genres = state.Preferred.Genres,
                PrimaryActionLabel = actionLabel
            };
        }

        private static HomeRecommendationItem BuildSeriesItem(SeriesState state, string detail, string target, string actionLabel)
        {
            return new HomeRecommendationItem
            {
                ContentId = state.Preferred.Id,
                ContentType = PlaybackContentType.Episode,
                Title = state.Preferred.Title,
                Detail = detail,
                PosterUrl = state.Preferred.DisplayPosterUrl,
                BackdropUrl = state.Preferred.DisplayHeroArtworkUrl,
                Overview = state.Preferred.Overview,
                Target = target,
                VoteAverage = state.Preferred.VoteAverage,
                Popularity = state.Preferred.Popularity,
                ArtworkScore = GetArtworkScore(state.Preferred),
                Genres = state.Preferred.Genres,
                PrimaryActionLabel = actionLabel
            };
        }

        private static string BuildRecommendationReason(Movie movie, bool hasResume, bool isFavorite, string affinityTag)
        {
            if (hasResume)
            {
                return "Continue from where you left off";
            }

            if (!string.IsNullOrWhiteSpace(affinityTag))
            {
                return $"Because you keep returning to {affinityTag}";
            }

            if (isFavorite)
            {
                return "Saved from your library picks";
            }

            return BuildMetadataLine(movie);
        }

        private static string BuildRecommendationReason(Series series, bool hasResume, bool isFavorite, string affinityTag)
        {
            if (hasResume)
            {
                return "Resume your next episode";
            }

            if (!string.IsNullOrWhiteSpace(affinityTag))
            {
                return $"Because you keep returning to {affinityTag}";
            }

            if (isFavorite)
            {
                return "Saved from your library picks";
            }

            return BuildMetadataLine(series);
        }

        private static string BuildRecommendationDetail(Movie movie, string affinityTag)
        {
            if (!string.IsNullOrWhiteSpace(affinityTag))
            {
                return $"Built around your {affinityTag} streak";
            }

            return BuildMetadataLine(movie);
        }

        private static string BuildRecommendationDetail(Series series, string affinityTag)
        {
            if (!string.IsNullOrWhiteSpace(affinityTag))
            {
                return $"Built around your {affinityTag} streak";
            }

            return BuildMetadataLine(series);
        }

        private static string BuildMetadataLine(Movie movie)
        {
            var parts = new List<string>();
            if (movie.ReleaseDate.HasValue)
            {
                parts.Add(movie.ReleaseDate.Value.Year.ToString());
            }

            if (!string.IsNullOrWhiteSpace(movie.Genres))
            {
                parts.Add(movie.Genres);
            }
            else if (!string.IsNullOrWhiteSpace(movie.CategoryName))
            {
                parts.Add(movie.CategoryName);
            }

            if (!string.IsNullOrWhiteSpace(movie.OriginalLanguage))
            {
                parts.Add(movie.OriginalLanguage.ToUpperInvariant());
            }

            return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildMetadataLine(Series series)
        {
            var parts = new List<string>();
            if (series.FirstAirDate.HasValue)
            {
                parts.Add(series.FirstAirDate.Value.Year.ToString());
            }

            if (!string.IsNullOrWhiteSpace(series.Genres))
            {
                parts.Add(series.Genres);
            }
            else if (!string.IsNullOrWhiteSpace(series.CategoryName))
            {
                parts.Add(series.CategoryName);
            }

            if (!string.IsNullOrWhiteSpace(series.OriginalLanguage))
            {
                parts.Add(series.OriginalLanguage.ToUpperInvariant());
            }

            return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static bool IsBrowsableMovie(Movie movie)
        {
            return string.Equals(movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase) &&
                   !ContentClassifier.IsGarbageTitle(movie.Title) &&
                   !ContentClassifier.IsGarbageCategoryName(movie.CategoryName) &&
                   !ContentClassifier.IsPromotionalCatalogLabel(movie.Title) &&
                   !ContentClassifier.IsPromotionalCatalogLabel(movie.CategoryName) &&
                   !ContentClassifier.IsM3uBucketOrAdultLabel(movie.Title);
        }

        private static bool IsBrowsableSeries(Series series)
        {
            return string.Equals(series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase) &&
                   !ContentClassifier.IsGarbageTitle(series.Title) &&
                   !ContentClassifier.IsGarbageCategoryName(series.CategoryName) &&
                   !ContentClassifier.IsPromotionalCatalogLabel(series.Title) &&
                   !ContentClassifier.IsPromotionalCatalogLabel(series.CategoryName) &&
                   !ContentClassifier.IsM3uBucketOrAdultLabel(series.Title);
        }

        private static bool IsFeaturedSafe(CatalogMovieGroup group)
        {
            var preferred = group.PreferredMovie;
            return ContentClassifier.IsFeaturedSafeMovie(group.Variants[0].SourceProfile.Type, preferred.Title, preferred.CategoryName, preferred.StreamUrl) &&
                   GetArtworkScore(preferred) >= 2;
        }

        private static bool IsFeaturedSafe(CatalogSeriesGroup group)
        {
            var preferred = group.PreferredSeries;
            return ContentClassifier.IsFeaturedSafeSeries(group.Variants[0].SourceProfile.Type, preferred.Title, preferred.CategoryName) &&
                   GetArtworkScore(preferred) >= 2;
        }

        private static int GetArtworkScore(Movie movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.BackdropUrl)) return 4;
            if (!string.IsNullOrWhiteSpace(movie.TmdbBackdropPath)) return 3;
            if (!string.IsNullOrWhiteSpace(movie.PosterUrl)) return 2;
            return string.IsNullOrWhiteSpace(movie.TmdbPosterPath) ? 0 : 1;
        }

        private static int GetArtworkScore(Series series)
        {
            if (!string.IsNullOrWhiteSpace(series.BackdropUrl)) return 4;
            if (!string.IsNullOrWhiteSpace(series.TmdbBackdropPath)) return 3;
            if (!string.IsNullOrWhiteSpace(series.PosterUrl)) return 2;
            return string.IsNullOrWhiteSpace(series.TmdbPosterPath) ? 0 : 1;
        }

        private sealed class MovieState
        {
            public required CatalogMovieGroup Group { get; init; }
            public Movie Preferred => Group.PreferredMovie;
            public bool IsFavorite { get; init; }
            public bool IsWatched { get; init; }
            public bool HasResume { get; init; }
            public DateTime LastInteractionUtc { get; init; }
            public string LastAffinityTag { get; set; } = string.Empty;
        }

        private sealed class SeriesState
        {
            public required CatalogSeriesGroup Group { get; init; }
            public Series Preferred => Group.PreferredSeries;
            public bool IsFavorite { get; init; }
            public bool IsWatched { get; init; }
            public bool HasResume { get; init; }
            public DateTime LastInteractionUtc { get; init; }
            public SeriesQueueSelection? Selection { get; init; }
            public string LastAffinityTag { get; set; } = string.Empty;
        }
    }
}
