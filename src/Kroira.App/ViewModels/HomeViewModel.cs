using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public sealed class HomeSummaryItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = "0";
        public string Detail { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
    }

    public sealed class HomeActionItem
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    public sealed class HomeContinueItem
    {
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public double VoteAverage { get; set; }
        public double ProgressValue { get; set; } = 58;
        public long SavedPositionMs { get; set; }
    }

    public sealed class HomeLiveItem
    {
        public int ContentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string Detail { get; set; } = "Live channel";
    }

    public sealed class HomeFeaturedItem
    {
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string HeroArtworkUrl { get; set; } = string.Empty;
        public string HeroPosterUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Genres { get; set; } = string.Empty;
        public double VoteAverage { get; set; }
        public double Popularity { get; set; }
        public int ArtworkScore { get; set; }
        public string PrimaryActionLabel { get; set; } = "Play";
        public string Target { get; set; } = string.Empty;
        public string RatingText => VoteAverage > 0 ? $"{VoteAverage:0.0}" : string.Empty;
    }

    public sealed class HomeMediaItem
    {
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public double VoteAverage { get; set; }
        public double Popularity { get; set; }
        public int ArtworkScore { get; set; }
        public string RatingText => VoteAverage > 0 ? $"{VoteAverage:0.0}" : string.Empty;
    }

    public partial class HomeViewModel : ObservableObject
    {
        private enum HomeLoadSection
        {
            ResolveAccess,
            LoadSummary,
            ApplySummary,
            LoadLastSync,
            BuildRecommendations,
            ApplyRecommendations,
            LoadContinue,
            LoadLive
        }

        private sealed class HomeLoadContext
        {
            public required AppDbContext Db { get; init; }
            public required IProfileStateService ProfileService { get; init; }
            public required IHomeRecommendationService RecommendationService { get; init; }
            public ProfileAccessSnapshot? Access { get; set; }
            public HomeSummarySnapshot? Summary { get; set; }
            public HomeRecommendationSnapshot? Recommendations { get; set; }
        }

        private sealed class HomeSummarySnapshot
        {
            public int ChannelsCount { get; init; }
            public int MoviesCount { get; init; }
            public int SeriesCount { get; init; }
            public int FavoritesCount { get; init; }
            public int SourcesCount { get; init; }
            public int SourceIssuesCount { get; init; }
        }

        private static readonly string StartupLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-log.txt");
        private static readonly string LoadSectionsOverridePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "home-load-sections.txt");

        private readonly IServiceProvider _serviceProvider;
        private readonly IEntitlementService _entitlementService;
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);

        public ObservableCollection<HomeSummaryItem> SummaryItems { get; } = new();
        public ObservableCollection<HomeActionItem> QuickActions { get; } = new();
        public ObservableCollection<HomeContinueItem> ContinueItems { get; } = new();
        public ObservableCollection<HomeLiveItem> LiveItems { get; } = new();
        public ObservableCollection<HomeMediaItem> PopularItems { get; } = new();
        public ObservableCollection<HomeMediaItem> RecentlyAddedItems { get; } = new();
        public ObservableCollection<HomeMediaItem> TopRatedItems { get; } = new();

        [ObservableProperty]
        private string _licenseStatusMessage = string.Empty;

        [ObservableProperty]
        private string _libraryStatusMessage = "Loading library status...";

        [ObservableProperty]
        private string _sourceStatusMessage = "Checking sources...";

        [ObservableProperty]
        private string _lastSyncMessage = "No sync history yet";

        [ObservableProperty]
        private string _heroSubtitle = "Fast access to live TV, VOD, source health, and saved playback progress from one desktop-first hub.";

        [ObservableProperty]
        private HomeFeaturedItem _featuredItem;

        [ObservableProperty]
        private Visibility _continueItemsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _continueEmptyVisibility = Visibility.Visible;

        [ObservableProperty]
        private Visibility _sourceIssueVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _liveItemsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _popularItemsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _recentlyAddedItemsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _topRatedItemsVisibility = Visibility.Collapsed;

        public HomeViewModel(IServiceProvider serviceProvider, IEntitlementService entitlementService)
        {
            _serviceProvider = serviceProvider;
            _entitlementService = entitlementService;
            FeaturedItem = BuildFallbackFeaturedItem();

            LicenseStatusMessage = $"{_entitlementService.CurrentTierDisplayName} tier active";

            QuickActions.Add(new HomeActionItem { Title = "Live Channels", Detail = "Open live TV and guide-ready streams", Glyph = "\uE714", Target = "Channels" });
            QuickActions.Add(new HomeActionItem { Title = "Movies", Detail = "Browse VOD with fast playback resume", Glyph = "\uE8B2", Target = "Movies" });
            QuickActions.Add(new HomeActionItem { Title = "Series", Detail = "Pick up seasons and episodes", Glyph = "\uE8A9", Target = "Series" });
            QuickActions.Add(new HomeActionItem { Title = "Favorites", Detail = "Jump to saved channels and picks", Glyph = "\uE734", Target = "Favorites" });
            QuickActions.Add(new HomeActionItem { Title = "Library", Detail = "Monitor recordings and downloads", Glyph = "\uE7C3", Target = "MediaLibrary" });
            QuickActions.Add(new HomeActionItem { Title = "Sources", Detail = "Manage M3U, Xtream, and provider setup", Glyph = "\uE8F1", Target = "Sources" });
            QuickActions.Add(new HomeActionItem { Title = "Settings", Detail = "Playback, profiles, and family controls", Glyph = "\uE713", Target = "Settings" });
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            LogLoadCheckpoint("HOMEVM 01: LoadAsync entered");
            using var scope = _serviceProvider.CreateScope();
            var context = new HomeLoadContext
            {
                Db = scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                ProfileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>(),
                RecommendationService = scope.ServiceProvider.GetRequiredService<IHomeRecommendationService>()
            };

            var enabledSections = GetEnabledLoadSections();
            LogLoadCheckpoint($"HOMEVM 02: enabled sections = {(enabledSections.Count == 0 ? "<none>" : string.Join(", ", enabledSections.OrderBy(section => section.ToString())))}");

            await RunLoadSectionAsync(HomeLoadSection.ResolveAccess, enabledSections, async () =>
            {
                context.Access = await ResolveAccessAsync(context);
            });

            await RunLoadSectionAsync(HomeLoadSection.LoadSummary, enabledSections, async () =>
            {
                context.Summary = await LoadSummaryAsync(context.Db, RequireAccess(context.Access));
            });

            await RunLoadSectionAsync(HomeLoadSection.ApplySummary, enabledSections, () =>
            {
                ApplySummary(RequireSummary(context.Summary));
                return Task.CompletedTask;
            });

            await RunLoadSectionAsync(HomeLoadSection.LoadLastSync, enabledSections, async () =>
            {
                LastSyncMessage = await LoadLastSyncMessageAsync(context.Db);
            });

            await RunLoadSectionAsync(HomeLoadSection.BuildRecommendations, enabledSections, async () =>
            {
                context.Recommendations = await BuildRecommendationsAsync(context);
            });

            await RunLoadSectionAsync(HomeLoadSection.ApplyRecommendations, enabledSections, () =>
            {
                ApplyRecommendationSnapshot(context.Recommendations ?? EmptyRecommendationSnapshot);
                return Task.CompletedTask;
            });

            await RunLoadSectionAsync(HomeLoadSection.LoadContinue, enabledSections, () =>
                LoadContinueItemsAsync(context.Db, RequireAccess(context.Access)));

            await RunLoadSectionAsync(HomeLoadSection.LoadLive, enabledSections, () =>
                LoadLiveItemsAsync(context.Db, RequireAccess(context.Access)));

            LogLoadCheckpoint("HOMEVM 99: LoadAsync completed");
        }

        private static readonly HomeRecommendationSnapshot EmptyRecommendationSnapshot = new();

        private async Task<ProfileAccessSnapshot> ResolveAccessAsync(HomeLoadContext context)
        {
            return await context.ProfileService.GetAccessSnapshotAsync(context.Db);
        }

        private async Task<HomeSummarySnapshot> LoadSummaryAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            var channelCategories = await db.ChannelCategories.AsNoTracking().ToListAsync();
            var categoryById = channelCategories.ToDictionary(category => category.Id);
            var channelsCount = (await db.Channels.AsNoTracking().ToListAsync())
                .Count(channel => categoryById.TryGetValue(channel.ChannelCategoryId, out var category) &&
                                  access.IsLiveChannelAllowed(channel, category));
            var moviesCount = (await db.Movies.AsNoTracking().ToListAsync()).Count(access.IsMovieAllowed);
            var seriesCount = (await db.Series.AsNoTracking().ToListAsync()).Count(access.IsSeriesAllowed);
            var favoritesCount = await db.Favorites.CountAsync(favorite => favorite.ProfileId == access.ProfileId);
            var sourcesCount = await db.SourceProfiles.CountAsync();
            var sourceIssuesCount = await db.SourceSyncStates.CountAsync(s => s.ErrorLog != string.Empty || s.HttpStatusCode >= 400);

            return new HomeSummarySnapshot
            {
                ChannelsCount = channelsCount,
                MoviesCount = moviesCount,
                SeriesCount = seriesCount,
                FavoritesCount = favoritesCount,
                SourcesCount = sourcesCount,
                SourceIssuesCount = sourceIssuesCount
            };
        }

        private void ApplySummary(HomeSummarySnapshot summary)
        {
            SummaryItems.Clear();
            SummaryItems.Add(new HomeSummaryItem { Label = "Channels", Value = summary.ChannelsCount.ToString("N0"), Detail = "Live entries", Glyph = "\uE714" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Movies", Value = summary.MoviesCount.ToString("N0"), Detail = "VOD titles", Glyph = "\uE8B2" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Series", Value = summary.SeriesCount.ToString("N0"), Detail = "Shows imported", Glyph = "\uE8A9" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Favorites", Value = summary.FavoritesCount.ToString("N0"), Detail = "Saved items", Glyph = "\uE734" });

            var totalItems = summary.ChannelsCount + summary.MoviesCount + summary.SeriesCount;
            LibraryStatusMessage = totalItems > 0
                ? $"{totalItems:N0} library items available across live TV and VOD"
                : "No imported library items yet";

            SourceStatusMessage = summary.SourcesCount > 0
                ? $"{summary.SourcesCount:N0} source{(summary.SourcesCount == 1 ? string.Empty : "s")} configured"
                : "No sources configured";

            SourceIssueVisibility = summary.SourceIssuesCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            HeroSubtitle = summary.SourcesCount > 0
                ? "Your library is staged for fast live TV, VOD, source management, and saved progress."
                : "Add a source to unlock live channels, movies, series, and guide-ready playback.";
        }

        private async Task<string> LoadLastSyncMessageAsync(AppDbContext db)
        {
            var lastSync = await db.SourceProfiles
                .Where(s => s.LastSync != null)
                .OrderByDescending(s => s.LastSync)
                .Select(s => s.LastSync)
                .FirstOrDefaultAsync();

            return lastSync.HasValue
                ? $"Last source sync: {lastSync.Value:g}"
                : "No source sync has completed yet";
        }

        private async Task<HomeRecommendationSnapshot> BuildRecommendationsAsync(HomeLoadContext context)
        {
            return await context.RecommendationService.BuildAsync(context.Db, RequireAccess(context.Access));
        }

        private static ProfileAccessSnapshot RequireAccess(ProfileAccessSnapshot? access)
        {
            return access ?? throw new InvalidOperationException("Home load section requires profile access snapshot.");
        }

        private static HomeSummarySnapshot RequireSummary(HomeSummarySnapshot? summary)
        {
            return summary ?? throw new InvalidOperationException("Home load section requires summary snapshot.");
        }

        private async Task RunLoadSectionAsync(
            HomeLoadSection section,
            ISet<HomeLoadSection> enabledSections,
            Func<Task> action)
        {
            if (!enabledSections.Contains(section))
            {
                LogLoadCheckpoint($"HOMEVM STEP {section}: skipped");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            LogLoadCheckpoint($"HOMEVM STEP {section}: start");
            try
            {
                await action();
                stopwatch.Stop();
                LogLoadCheckpoint($"HOMEVM STEP {section}: end ({stopwatch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogLoadCheckpoint($"HOMEVM STEP {section}: error after {stopwatch.ElapsedMilliseconds} ms - {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static HashSet<HomeLoadSection> GetEnabledLoadSections()
        {
            var raw = Environment.GetEnvironmentVariable("KROIRA_HOME_LOAD_SECTIONS");
            if (string.IsNullOrWhiteSpace(raw) && File.Exists(LoadSectionsOverridePath))
            {
                raw = File.ReadAllText(LoadSectionsOverridePath);
            }

            var enabled = new HashSet<HomeLoadSection>();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return enabled;
            }

            foreach (var token in raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.GetValues<HomeLoadSection>().ToHashSet();
                }

                if (Enum.TryParse<HomeLoadSection>(token, true, out var section))
                {
                    enabled.Add(section);
                }
            }

            return enabled;
        }

        private static void LogLoadCheckpoint(string message)
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

        private void ApplyRecommendationSnapshot(HomeRecommendationSnapshot snapshot)
        {
            FeaturedItem = snapshot.Featured == null
                ? BuildFallbackFeaturedItem()
                : new HomeFeaturedItem
                {
                    ContentId = snapshot.Featured.ContentId,
                    ContentType = snapshot.Featured.ContentType,
                    Title = snapshot.Featured.Title,
                    Detail = snapshot.Featured.Detail,
                    StreamUrl = snapshot.Featured.StreamUrl,
                    PosterUrl = snapshot.Featured.PosterUrl,
                    BackdropUrl = snapshot.Featured.BackdropUrl,
                    HeroArtworkUrl = ResolveHeroArtworkUrl(snapshot.Featured.BackdropUrl, snapshot.Featured.PosterUrl),
                    HeroPosterUrl = snapshot.Featured.PosterUrl,
                    Overview = snapshot.Featured.Overview,
                    Genres = snapshot.Featured.Genres,
                    VoteAverage = snapshot.Featured.VoteAverage,
                    Popularity = snapshot.Featured.Popularity,
                    ArtworkScore = snapshot.Featured.ArtworkScore,
                    PrimaryActionLabel = snapshot.Featured.PrimaryActionLabel,
                    Target = snapshot.Featured.Target
                };

            PopularItems.Clear();
            foreach (var item in snapshot.Recommended.Select(MapRecommendationItem))
            {
                PopularItems.Add(item);
            }

            RecentlyAddedItems.Clear();
            foreach (var item in snapshot.RecentlyAdded.Select(MapRecommendationItem))
            {
                RecentlyAddedItems.Add(item);
            }

            TopRatedItems.Clear();
            foreach (var item in snapshot.TopRated.Select(MapRecommendationItem))
            {
                TopRatedItems.Add(item);
            }

            PopularItemsVisibility = PopularItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentlyAddedItemsVisibility = RecentlyAddedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TopRatedItemsVisibility = TopRatedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static HomeMediaItem MapRecommendationItem(HomeRecommendationItem item)
        {
            return new HomeMediaItem
            {
                ContentId = item.ContentId,
                ContentType = item.ContentType,
                Title = item.Title,
                Detail = item.Detail,
                StreamUrl = item.StreamUrl,
                PosterUrl = item.PosterUrl,
                BackdropUrl = item.BackdropUrl,
                Overview = item.Overview,
                Target = item.Target,
                VoteAverage = item.VoteAverage,
                Popularity = item.Popularity,
                ArtworkScore = item.ArtworkScore
            };
        }

        private async Task PrepareFeaturedMetadataAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            var movieCandidates = await db.Movies
                .OrderByDescending(m => m.BackdropUrl != string.Empty || m.TmdbBackdropPath != string.Empty)
                .ThenByDescending(m => m.PosterUrl != string.Empty || m.TmdbPosterPath != string.Empty)
                .ThenByDescending(m => m.Popularity)
                .ThenByDescending(m => m.VoteAverage)
                .ThenBy(m => m.Title)
                .Take(24)
                .ToListAsync();

            var seriesCandidates = await db.Series
                .OrderByDescending(s => s.BackdropUrl != string.Empty || s.TmdbBackdropPath != string.Empty)
                .ThenByDescending(s => s.PosterUrl != string.Empty || s.TmdbPosterPath != string.Empty)
                .ThenByDescending(s => s.Popularity)
                .ThenByDescending(s => s.VoteAverage)
                .ThenBy(s => s.Title)
                .Take(24)
                .ToListAsync();
            movieCandidates = movieCandidates.Where(access.IsMovieAllowed).ToList();
            seriesCandidates = seriesCandidates.Where(access.IsSeriesAllowed).ToList();

            // Featured safety: M3U bucket/adult/category items are stripped
            // before ranking. Xtream items are NEVER filtered by this gate —
            // the Xtream pipeline already yields structured data and its
            // "Movies" / "Series" category names are legitimate. We look up
            // each candidate's source type and let the Xtream branch pass
            // through untouched.
            var candidateSourceIds = movieCandidates.Select(m => m.SourceProfileId)
                .Concat(seriesCandidates.Select(s => s.SourceProfileId))
                .Distinct()
                .ToList();
            var sourceTypeById = await db.SourceProfiles
                .Where(p => candidateSourceIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Type);

            SourceType GetSourceType(int sourceProfileId) =>
                sourceTypeById.TryGetValue(sourceProfileId, out var t) ? t : SourceType.M3U;

            var safeFeaturedMovies = movieCandidates
                .Where(m => ContentClassifier.IsFeaturedSafeMovie(
                    GetSourceType(m.SourceProfileId), m.Title, m.CategoryName, m.StreamUrl))
                .ToList();

            var safeFeaturedSeries = seriesCandidates
                .Where(s => ContentClassifier.IsFeaturedSafeSeries(
                    GetSourceType(s.SourceProfileId), s.Title, s.CategoryName))
                .ToList();

            // No fallback: if every candidate fails the safety gate we
            // intentionally leave the pools empty and let
            // BuildFallbackFeaturedItem render a neutral hero instead of
            // promoting a bucket / adult / category label into the featured
            // slot. This is the only guarantee that "ALL SERIEN" and friends
            // can never reach the hero even if they somehow exist in the DB.

            var topMoviePool = safeFeaturedMovies
                .OrderByDescending(GetArtworkScore)
                .ThenByDescending(m => m.Popularity)
                .ThenByDescending(m => m.VoteAverage)
                .ThenBy(m => m.Title)
                .Take(5)
                .ToList();

            var topSeriesPool = safeFeaturedSeries
                .OrderByDescending(GetArtworkScore)
                .ThenByDescending(s => s.Popularity)
                .ThenByDescending(s => s.VoteAverage)
                .ThenBy(s => s.Title)
                .Take(5)
                .ToList();

            var featuredMovie = topMoviePool.Count > 0
                ? topMoviePool[_sessionRotationIndex % topMoviePool.Count]
                : null;

            var featuredSeries = topSeriesPool.Count > 0
                ? topSeriesPool[_sessionRotationIndex % topSeriesPool.Count]
                : null;

            if (featuredMovie == null && featuredSeries == null)
            {
                FeaturedItem = BuildFallbackFeaturedItem();
                return;
            }

            if (featuredMovie != null && ShouldUseMovieFeature(featuredMovie, featuredSeries))
            {
                var posterUrl = featuredMovie.DisplayPosterUrl;
                var backdropUrl = featuredMovie.DisplayBackdropUrl;
                FeaturedItem = new HomeFeaturedItem
                {
                    ContentId = featuredMovie.Id,
                    ContentType = PlaybackContentType.Movie,
                    Title = featuredMovie.Title,
                    Detail = BuildMediaDetail(featuredMovie.ReleaseDate, featuredMovie.Genres, featuredMovie.OriginalLanguage),
                    StreamUrl = featuredMovie.StreamUrl,
                    PosterUrl = posterUrl,
                    BackdropUrl = backdropUrl,
                    HeroArtworkUrl = ResolveHeroArtworkUrl(backdropUrl, posterUrl),
                    HeroPosterUrl = posterUrl,
                    Overview = featuredMovie.Overview,
                    Genres = featuredMovie.Genres,
                    VoteAverage = featuredMovie.VoteAverage,
                    Popularity = featuredMovie.Popularity,
                    ArtworkScore = GetArtworkScore(featuredMovie),
                    PrimaryActionLabel = "Play movie",
                    Target = "Movies"
                };
                return;
            }

            if (featuredSeries != null)
            {
                var posterUrl = featuredSeries.DisplayPosterUrl;
                var backdropUrl = featuredSeries.DisplayBackdropUrl;
                FeaturedItem = new HomeFeaturedItem
                {
                    ContentId = featuredSeries.Id,
                    ContentType = PlaybackContentType.Episode,
                    Title = featuredSeries.Title,
                    Detail = BuildMediaDetail(featuredSeries.FirstAirDate, featuredSeries.Genres, featuredSeries.OriginalLanguage),
                    PosterUrl = posterUrl,
                    BackdropUrl = backdropUrl,
                    HeroArtworkUrl = ResolveHeroArtworkUrl(backdropUrl, posterUrl),
                    HeroPosterUrl = posterUrl,
                    Overview = featuredSeries.Overview,
                    Genres = featuredSeries.Genres,
                    VoteAverage = featuredSeries.VoteAverage,
                    Popularity = featuredSeries.Popularity,
                    ArtworkScore = GetArtworkScore(featuredSeries),
                    PrimaryActionLabel = "View series",
                    Target = "Series"
                };
            }
        }

        private static HomeFeaturedItem BuildFallbackFeaturedItem()
        {
            return new HomeFeaturedItem
            {
                Title = "Kroira",
                Detail = "Media home",
                Overview = "Add or sync a source to build a visual home with featured movies, shows, live channels, and saved playback.",
                PrimaryActionLabel = "Add source",
                Target = "Sources"
            };
        }

        private async Task LoadContinueItemsAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            ContinueItems.Clear();
            var watchStateService = _serviceProvider.GetRequiredService<ILibraryWatchStateService>();
            var hideWatched = await watchStateService.GetHideWatchedInContinueAsync(db, access.ProfileId);

            var continueItems = new List<(HomeContinueItem Item, DateTime SortAtUtc)>();

            var liveRows = await db.PlaybackProgresses
                .Where(progress => progress.ProfileId == access.ProfileId &&
                                   progress.ContentType == PlaybackContentType.Channel &&
                                   !progress.IsCompleted)
                .OrderByDescending(progress => progress.LastWatched)
                .Take(8)
                .ToListAsync();
            var liveChannelIds = liveRows.Select(progress => progress.ContentId).Distinct().ToList();
            var liveChannels = await db.Channels.Where(channel => liveChannelIds.Contains(channel.Id)).ToDictionaryAsync(channel => channel.Id);
            var liveCategoryIds = liveChannels.Values.Select(channel => channel.ChannelCategoryId).Distinct().ToList();
            var liveCategories = await db.ChannelCategories.Where(category => liveCategoryIds.Contains(category.Id)).ToDictionaryAsync(category => category.Id);

            foreach (var progress in liveRows)
            {
                if (!liveChannels.TryGetValue(progress.ContentId, out var channel))
                {
                    continue;
                }

                if (!liveCategories.TryGetValue(channel.ChannelCategoryId, out var category) || !access.IsLiveChannelAllowed(channel, category))
                {
                    continue;
                }

                continueItems.Add((new HomeContinueItem
                {
                    ContentId = channel.Id,
                    ContentType = PlaybackContentType.Channel,
                    Title = channel.Name,
                    Detail = "Live channel",
                    StreamUrl = channel.StreamUrl,
                    PosterUrl = channel.LogoUrl,
                    SavedPositionMs = progress.PositionMs
                }, progress.LastWatched));
            }

            var movieRows = await db.PlaybackProgresses
                .Where(progress => progress.ProfileId == access.ProfileId &&
                                   progress.ContentType == PlaybackContentType.Movie)
                .OrderByDescending(progress => progress.LastWatched)
                .ToListAsync();
            var movieIds = movieRows.Select(progress => progress.ContentId).Distinct().ToList();
            var movies = await db.Movies.Where(movie => movieIds.Contains(movie.Id)).ToDictionaryAsync(movie => movie.Id);
            var movieSnapshots = await watchStateService.LoadSnapshotsAsync(db, access.ProfileId, PlaybackContentType.Movie, movieIds);

            foreach (var progress in movieRows)
            {
                if (!movies.TryGetValue(progress.ContentId, out var movie) || !access.IsMovieAllowed(movie))
                {
                    continue;
                }

                if (!movieSnapshots.TryGetValue(movie.Id, out var snapshot))
                {
                    continue;
                }

                if (hideWatched && snapshot.IsWatched)
                {
                    continue;
                }

                continueItems.Add((new HomeContinueItem
                {
                    ContentId = movie.Id,
                    ContentType = PlaybackContentType.Movie,
                    Title = movie.Title,
                    Detail = snapshot.IsWatched
                        ? "Watched"
                        : snapshot.HasResumePoint
                            ? $"Resume at {TimeSpan.FromMilliseconds(snapshot.ResumePositionMs):hh\\:mm\\:ss}"
                            : "Ready to start",
                    StreamUrl = movie.StreamUrl,
                    PosterUrl = movie.DisplayPosterUrl,
                    BackdropUrl = movie.DisplayHeroArtworkUrl,
                    Overview = movie.Overview,
                    VoteAverage = movie.VoteAverage,
                    ProgressValue = snapshot.ProgressPercent,
                    SavedPositionMs = snapshot.ResumePositionMs
                }, snapshot.LastWatched));
            }

            var episodeRows = await db.PlaybackProgresses
                .Where(progress => progress.ProfileId == access.ProfileId &&
                                   progress.ContentType == PlaybackContentType.Episode)
                .OrderByDescending(progress => progress.LastWatched)
                .ToListAsync();
            var episodeIds = episodeRows.Select(progress => progress.ContentId).Distinct().ToList();
            var episodes = await db.Episodes.Where(episode => episodeIds.Contains(episode.Id)).ToDictionaryAsync(episode => episode.Id);
            var seasonIds = episodes.Values.Select(episode => episode.SeasonId).Distinct().ToList();
            var seasons = await db.Seasons.Where(season => seasonIds.Contains(season.Id)).ToDictionaryAsync(season => season.Id);
            var seriesIds = seasons.Values.Select(season => season.SeriesId).Distinct().ToList();
            var seriesList = await db.Series
                .AsNoTracking()
                .Include(series => series.Seasons!)
                .ThenInclude(season => season.Episodes)
                .Where(series => seriesIds.Contains(series.Id))
                .ToListAsync();
            var episodeSnapshots = await watchStateService.LoadSnapshotsAsync(db, access.ProfileId, PlaybackContentType.Episode, episodeIds);

            foreach (var selection in seriesList
                         .Where(access.IsSeriesAllowed)
                         .Select(series => watchStateService.BuildSeriesQueueSelection(series, episodeSnapshots, includeWatched: !hideWatched))
                         .Where(selection => selection != null)
                         .Cast<SeriesQueueSelection>())
            {
                var episodeCode = $"S{selection.Season.SeasonNumber:00} E{selection.Episode.EpisodeNumber:00}";
                continueItems.Add((new HomeContinueItem
                {
                    ContentId = selection.Episode.Id,
                    ContentType = PlaybackContentType.Episode,
                    Title = selection.Series.Title,
                    Detail = selection.IsWatched
                        ? $"Watched through {episodeCode}"
                        : selection.IsResumeCandidate
                            ? $"Resume {episodeCode}"
                            : $"Next up {episodeCode}",
                    StreamUrl = selection.Episode.StreamUrl,
                    PosterUrl = selection.Series.DisplayPosterUrl,
                    BackdropUrl = selection.Series.DisplayHeroArtworkUrl,
                    Overview = selection.Series.Overview,
                    VoteAverage = selection.Series.VoteAverage,
                    ProgressValue = selection.EpisodeSnapshot?.ProgressPercent ?? (selection.IsWatched ? 100 : 0),
                    SavedPositionMs = selection.ResumePositionMs
                }, selection.SortAtUtc));
            }

            foreach (var entry in continueItems
                         .OrderByDescending(entry => entry.SortAtUtc)
                         .ThenByDescending(entry => IsTurkishHint(entry.Item.Title) || IsTurkishHint(entry.Item.Detail))
                         .ThenBy(entry => entry.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                         .Take(8))
            {
                ContinueItems.Add(entry.Item);
            }

            ContinueItemsVisibility = ContinueItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ContinueEmptyVisibility = ContinueItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task LoadLiveItemsAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            LiveItems.Clear();

            var categories = await db.ChannelCategories.AsNoTracking().ToDictionaryAsync(category => category.Id);
            var channels = (await db.Channels
                    .Where(c => c.StreamUrl != string.Empty)
                    .OrderByDescending(c => IsTurkishHint(c.Name) || IsTurkishHint(c.LogoUrl))
                    .ThenBy(c => c.Name)
                    .Take(24)
                    .ToListAsync())
                .Where(channel => categories.TryGetValue(channel.ChannelCategoryId, out var category) &&
                                  access.IsLiveChannelAllowed(channel, category))
                .Take(8)
                .ToList();

            foreach (var channel in channels)
            {
                LiveItems.Add(new HomeLiveItem
                {
                    ContentId = channel.Id,
                    Title = channel.Name,
                    LogoUrl = channel.LogoUrl,
                    StreamUrl = channel.StreamUrl
                });
            }

            LiveItemsVisibility = LiveItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadMediaRailsAsync(AppDbContext db, ProfileAccessSnapshot access)
        {
            PopularItems.Clear();
            RecentlyAddedItems.Clear();
            TopRatedItems.Clear();

            var popularMovies = await db.Movies
                .OrderByDescending(m => m.Popularity)
                .ThenByDescending(m => m.VoteAverage)
                .ThenByDescending(m => m.BackdropUrl != string.Empty || m.TmdbBackdropPath != string.Empty)
                .ThenByDescending(m => m.PosterUrl != string.Empty || m.TmdbPosterPath != string.Empty)
                .ThenBy(m => m.Title)
                .Take(10)
                .ToListAsync();
            popularMovies = popularMovies.Where(access.IsMovieAllowed).ToList();

            var popularSeries = await db.Series
                .OrderByDescending(s => s.Popularity)
                .ThenByDescending(s => s.VoteAverage)
                .ThenByDescending(s => s.BackdropUrl != string.Empty || s.TmdbBackdropPath != string.Empty)
                .ThenByDescending(s => s.PosterUrl != string.Empty || s.TmdbPosterPath != string.Empty)
                .ThenBy(s => s.Title)
                .Take(10)
                .ToListAsync();
            popularSeries = popularSeries.Where(access.IsSeriesAllowed).ToList();

            foreach (var item in popularMovies.Select(BuildMovieRailItem)
                         .Concat(popularSeries.Select(BuildSeriesRailItem))
                         .OrderByDescending(i => i.Popularity)
                         .ThenByDescending(i => i.VoteAverage)
                         .ThenByDescending(i => i.ArtworkScore)
                         .ThenBy(i => i.Title)
                         .Take(10))
            {
                PopularItems.Add(item);
            }

            var recentMovies = await db.Movies
                .OrderByDescending(m => m.Id)
                .Take(10)
                .ToListAsync();
            recentMovies = recentMovies.Where(access.IsMovieAllowed).ToList();

            var recentSeries = await db.Series
                .OrderByDescending(s => s.Id)
                .Take(10)
                .ToListAsync();
            recentSeries = recentSeries.Where(access.IsSeriesAllowed).ToList();

            foreach (var item in recentMovies.Select(BuildMovieRailItem)
                         .Concat(recentSeries.Select(BuildSeriesRailItem))
                         .OrderByDescending(i => i.ContentId)
                         .Take(10))
            {
                RecentlyAddedItems.Add(item);
            }

            var topRatedMovies = await db.Movies
                .OrderByDescending(m => m.VoteAverage)
                .ThenByDescending(m => m.Popularity)
                .ThenBy(m => m.Title)
                .Take(10)
                .ToListAsync();
            topRatedMovies = topRatedMovies.Where(access.IsMovieAllowed).ToList();

            var topRatedSeries = await db.Series
                .OrderByDescending(s => s.VoteAverage)
                .ThenByDescending(s => s.Popularity)
                .ThenBy(s => s.Title)
                .Take(10)
                .ToListAsync();
            topRatedSeries = topRatedSeries.Where(access.IsSeriesAllowed).ToList();

            foreach (var item in topRatedMovies.Select(BuildMovieRailItem)
                         .Concat(topRatedSeries.Select(BuildSeriesRailItem))
                         .OrderByDescending(i => i.VoteAverage)
                         .ThenBy(i => i.Title)
                         .Take(10))
            {
                TopRatedItems.Add(item);
            }

            PopularItemsVisibility = PopularItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentlyAddedItemsVisibility = RecentlyAddedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TopRatedItemsVisibility = TopRatedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static HomeMediaItem BuildMovieRailItem(Movie movie)
        {
            return new HomeMediaItem
            {
                ContentId = movie.Id,
                ContentType = PlaybackContentType.Movie,
                Title = movie.Title,
                Detail = BuildMediaDetail(movie.ReleaseDate, movie.Genres, movie.OriginalLanguage),
                StreamUrl = movie.StreamUrl,
                PosterUrl = movie.DisplayPosterUrl,
                BackdropUrl = movie.DisplayBackdropUrl,
                Overview = movie.Overview,
                Target = "Movies",
                VoteAverage = movie.VoteAverage,
                Popularity = movie.Popularity,
                ArtworkScore = GetArtworkScore(movie)
            };
        }

        private static HomeMediaItem BuildSeriesRailItem(Series series)
        {
            return new HomeMediaItem
            {
                ContentId = series.Id,
                ContentType = PlaybackContentType.Episode,
                Title = series.Title,
                Detail = BuildMediaDetail(series.FirstAirDate, series.Genres, series.OriginalLanguage),
                PosterUrl = series.DisplayPosterUrl,
                BackdropUrl = series.DisplayBackdropUrl,
                Overview = series.Overview,
                Target = "Series",
                VoteAverage = series.VoteAverage,
                Popularity = series.Popularity,
                ArtworkScore = GetArtworkScore(series)
            };
        }

        private static bool ShouldUseMovieFeature(Movie movie, Series series)
        {
            if (series == null)
            {
                return true;
            }

            var movieArtworkScore = GetArtworkScore(movie);
            var seriesArtworkScore = GetArtworkScore(series);
            if (movieArtworkScore != seriesArtworkScore)
            {
                return movieArtworkScore > seriesArtworkScore;
            }

            if (Math.Abs(movie.Popularity - series.Popularity) > 0.01)
            {
                return movie.Popularity > series.Popularity;
            }

            return movie.VoteAverage >= series.VoteAverage;
        }

        private static int GetArtworkScore(Movie movie)
        {
            return GetArtworkScore(movie.BackdropUrl, movie.TmdbBackdropPath, movie.PosterUrl, movie.TmdbPosterPath);
        }

        private static int GetArtworkScore(Series series)
        {
            return GetArtworkScore(series.BackdropUrl, series.TmdbBackdropPath, series.PosterUrl, series.TmdbPosterPath);
        }

        private static int GetArtworkScore(string backdropUrl, string tmdbBackdropPath, string posterUrl, string tmdbPosterPath)
        {
            if (!string.IsNullOrWhiteSpace(backdropUrl))
            {
                return IsTmdbImageUrl(backdropUrl) ? 3 : 4;
            }

            if (!string.IsNullOrWhiteSpace(tmdbBackdropPath))
            {
                return 3;
            }

            if (!string.IsNullOrWhiteSpace(posterUrl))
            {
                return IsTmdbImageUrl(posterUrl) ? 1 : 2;
            }

            return string.IsNullOrWhiteSpace(tmdbPosterPath) ? 0 : 1;
        }

        private static string ResolveHeroArtworkUrl(string backdropUrl, string posterUrl)
        {
            return string.IsNullOrWhiteSpace(backdropUrl) ? posterUrl : backdropUrl;
        }

        private static string ResolveBackdropUrl(string backdropUrl, string tmdbBackdropPath)
        {
            if (!string.IsNullOrWhiteSpace(backdropUrl))
            {
                return backdropUrl;
            }

            return BuildTmdbImageUrl(tmdbBackdropPath, "w1280");
        }

        private static string ResolvePosterUrl(string posterUrl, string tmdbPosterPath)
        {
            if (!string.IsNullOrWhiteSpace(posterUrl))
            {
                return posterUrl;
            }

            return BuildTmdbImageUrl(tmdbPosterPath, "w500");
        }

        private static string BuildTmdbImageUrl(string path, string size)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : $"https://image.tmdb.org/t/p/{size}{path}";
        }

        private static bool IsTmdbImageUrl(string url)
        {
            return url.Contains("image.tmdb.org/t/p/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTurkishHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return normalized.StartsWith("TR", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Turk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Türk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Turkiye", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Türkiye", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Turkish", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMediaDetail(DateTime? date, string genres, string language)
        {
            var parts = new List<string>();
            if (date.HasValue)
            {
                parts.Add(date.Value.Year.ToString());
            }
            if (!string.IsNullOrWhiteSpace(genres))
            {
                parts.Add(genres);
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                parts.Add(language.ToUpperInvariant());
            }

            return string.Join(" / ", parts);
        }
    }
}
