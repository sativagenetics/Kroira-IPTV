#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class MovieBrowseItemViewModel : ObservableObject
    {
        public MovieBrowseItemViewModel(CatalogMovieGroup group, bool isFavorite)
        {
            Group = group;
            IsFavorite = isFavorite;
        }

        public CatalogMovieGroup Group { get; }
        public Movie Movie => Group.PreferredMovie;
        public IReadOnlyList<CatalogMovieVariant> Variants => Group.Variants;
        public IReadOnlyList<int> VariantIds => Group.Variants.Select(variant => variant.Movie.Id).ToList();
        public int Id => Movie.Id;
        public string Title => Movie.Title;
        public string StreamUrl => Movie.StreamUrl;
        public string DisplayPosterUrl => Movie.DisplayPosterUrl;
        public string DisplayHeroArtworkUrl => Movie.DisplayHeroArtworkUrl;
        public string RatingText => Movie.RatingText;
        public string MetadataLine =>
            Group.Variants.Count > 1 && !string.IsNullOrWhiteSpace(Group.SourceSummary)
                ? string.IsNullOrWhiteSpace(Movie.MetadataLine)
                    ? Group.SourceSummary
                    : $"{Movie.MetadataLine} / {Group.SourceSummary}"
                : Movie.MetadataLine;
        public string Overview => Movie.Overview;
        public string CategoryName => Movie.CategoryName;
        public double Popularity => Movie.Popularity;
        public double VoteAverage => Movie.VoteAverage;
        public string BackdropUrl => Movie.BackdropUrl;
        public string TmdbBackdropPath => Movie.TmdbBackdropPath;
        public string PosterUrl => Movie.PosterUrl;
        public string TmdbPosterPath => Movie.TmdbPosterPath;
        public bool HasAlternateSources => Group.Variants.Count > 1;
        public string SourceSummary => Group.SourceSummary;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
        [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
        private bool _isFavorite;

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
        public string FavoriteLabel => IsFavorite ? "Saved" : "Save";
    }

    public partial class MovieBrowseSlotViewModel : ObservableObject
    {
        public MovieBrowseSlotViewModel(MovieBrowseItemViewModel? movie)
        {
            Movie = movie;
        }

        public MovieBrowseItemViewModel? Movie { get; }
        public bool HasMovie => Movie != null;
        public int Id => Movie?.Id ?? 0;
        public string Title => Movie?.Title ?? string.Empty;
        public string DisplayPosterUrl => Movie?.DisplayPosterUrl ?? string.Empty;
        public string RatingText => Movie?.RatingText ?? string.Empty;
        public string MetadataLine => Movie?.MetadataLine ?? string.Empty;
        public string FavoriteGlyph => Movie?.FavoriteGlyph ?? "\uE734";
        public string FavoriteLabel => Movie?.FavoriteLabel ?? string.Empty;
        public Visibility MovieVisibility => HasMovie ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => HasMovie ? Visibility.Collapsed : Visibility.Visible;

        public void RefreshFavoriteState()
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }

    public partial class MoviesViewModel : ObservableObject
    {
        private const string FixedFeaturedMovieTitle = "Kurtlar Vadisi Gladio";
        private const int BrowseGridColumns = 5;
        private readonly IServiceProvider _serviceProvider;
        private List<MovieBrowseItemViewModel> _allMovies = new List<MovieBrowseItemViewModel>();
        private Dictionary<int, SourceType> _sourceTypeById = new Dictionary<int, SourceType>();
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);

        public ObservableCollection<MovieBrowseItemViewModel> FilteredMovies { get; } = new ObservableCollection<MovieBrowseItemViewModel>();
        public ObservableCollection<MovieBrowseSlotViewModel> DisplayMovieSlots { get; } = new ObservableCollection<MovieBrowseSlotViewModel>();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new ObservableCollection<BrowserCategoryViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FeaturedMovieCanPlay))]
        private MovieBrowseItemViewModel _featuredMovie = CreatePlaceholderFeaturedMovie();

        public bool FeaturedMovieCanPlay => !string.IsNullOrWhiteSpace(FeaturedMovie?.StreamUrl);

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            ApplyFilter();
        }

        public MoviesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadMoviesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ICatalogDeduplicationService>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var access = await profileService.GetAccessSnapshotAsync(db);
            var languageCode = await AppLanguageService.GetLanguageAsync(db, access.ProfileId);
            var movieGroups = (await deduplicationService.LoadMovieGroupsAsync(db))
                .Select(group => FilterGroup(group, access))
                .OfType<CatalogMovieGroup>()
                .ToList();
            var favoriteIds = (await db.Favorites
                .Where(f => f.ProfileId == access.ProfileId && f.ContentType == FavoriteType.Movie)
                .Select(f => f.ContentId)
                .ToListAsync())
                .ToHashSet();

            _allMovies = CatalogOrderingService
                .OrderCatalog(movieGroups, languageCode, g => g.PreferredMovie.CategoryName, g => g.PreferredMovie.Title)
                .Select(group => new MovieBrowseItemViewModel(
                    group,
                    group.Variants.Any(variant => favoriteIds.Contains(variant.Movie.Id))))
                .ToList();

            // Cache SourceProfileId -> SourceType for featured-safety gate.
            // The M3U safety rules are applied only to M3U items; Xtream
            // items pass through untouched.
            var sourceIds = movieGroups.Select(group => group.PreferredMovie.SourceProfileId).Distinct().ToList();
            _sourceTypeById = await db.SourceProfiles
                .Where(p => sourceIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Type);

            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });

            var categoryIndex = 1;
            var orderedCategories = CatalogOrderingService.OrderCategories(
                _allMovies
                    .Select(m => string.IsNullOrWhiteSpace(m.CategoryName) ? "Uncategorized" : m.CategoryName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                languageCode);

            foreach (var categoryName in orderedCategories)
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = categoryIndex,
                    Name = categoryName,
                    OrderIndex = categoryIndex
                });
                categoryIndex++;
            }

            SelectedCategory = Categories.FirstOrDefault();
            RefreshFeaturedMovie();
            ApplyFilter();
            StartMetadataEnrichment();
        }

        private void ApplyFilter()
        {
            FilteredMovies.Clear();
            var filtered = _allMovies.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                filtered = filtered.Where(m =>
                    string.Equals(GetDisplayCategory(m.CategoryName), SelectedCategory.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(m => m.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in filtered)
            {
                FilteredMovies.Add(item);
            }

            IsEmpty = FilteredMovies.Count == 0;
            RefreshDisplayMovieSlots();
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int movieId)
        {
            var target = _allMovies.FirstOrDefault(m => m.Id == movieId);
            if (target == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var existingFavorites = await db.Favorites
                .Where(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Movie && target.VariantIds.Contains(f.ContentId))
                .ToListAsync();

            if (existingFavorites.Count == 0)
            {
                db.Favorites.Add(new Favorite { ProfileId = activeProfileId, ContentType = FavoriteType.Movie, ContentId = movieId });
                target.IsFavorite = true;
            }
            else
            {
                db.Favorites.RemoveRange(existingFavorites);
                target.IsFavorite = false;
            }

            await db.SaveChangesAsync();

            foreach (var slot in DisplayMovieSlots.Where(slot => slot.Movie?.Id == movieId))
            {
                slot.RefreshFavoriteState();
            }
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private void RefreshDisplayMovieSlots()
        {
            DisplayMovieSlots.Clear();

            foreach (var movie in FilteredMovies)
            {
                DisplayMovieSlots.Add(new MovieBrowseSlotViewModel(movie));
            }

            if (FilteredMovies.Count == 0)
            {
                return;
            }

            var remainder = FilteredMovies.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var i = 0; i < placeholderCount; i++)
            {
                DisplayMovieSlots.Add(new MovieBrowseSlotViewModel(null));
            }
        }

        private void RefreshFeaturedMovie()
        {
            // Pinned-title path must ALSO pass the featured-safety gate so a
            // bucket row can never be promoted even if its title matches the
            // pinned constant.
            var pinned = _allMovies.FirstOrDefault(m =>
                string.Equals(m.Title.Trim(), FixedFeaturedMovieTitle, StringComparison.OrdinalIgnoreCase));
            if (pinned != null && IsSafeForFeatured(pinned))
            {
                FeaturedMovie = pinned;
                return;
            }

            FeaturedMovie = SelectFeaturedMovie(_allMovies, _sourceTypeById);
        }

        private bool IsSafeForFeatured(MovieBrowseItemViewModel item)
        {
            var type = _sourceTypeById.TryGetValue(item.Movie.SourceProfileId, out var t)
                ? t
                : SourceType.M3U;
            return ContentClassifier.IsFeaturedSafeMovie(type, item.Title, item.CategoryName, item.StreamUrl);
        }

        private static MovieBrowseItemViewModel SelectFeaturedMovie(
            IEnumerable<MovieBrowseItemViewModel> movies,
            IReadOnlyDictionary<int, SourceType> sourceTypeById)
        {
            // Featured safety: exclude M3U bucket/adult/category items.
            // Xtream items pass through the gate untouched — the Xtream
            // pipeline already yields structured data and a "Movies" /
            // "Cinema" category name is a legitimate Xtream category.
            //
            // If NO movie passes the gate, we render a neutral placeholder
            // instead of promoting an unsafe title into the featured slot.
            // This is the only guarantee that a bucket row can never reach
            // the Movies-page hero even when it exists in the DB.
            SourceType TypeFor(MovieBrowseItemViewModel item) =>
                sourceTypeById.TryGetValue(item.Movie.SourceProfileId, out var t) ? t : SourceType.M3U;

            var safeMovies = movies
                .Where(m => ContentClassifier.IsFeaturedSafeMovie(
                    TypeFor(m), m.Title, m.CategoryName, m.StreamUrl))
                .ToList();

            if (safeMovies.Count == 0)
            {
                return CreatePlaceholderFeaturedMovie();
            }

            var allRanked = safeMovies
                .OrderByDescending(m => GetArtworkScore(m))
                .ThenByDescending(m => m.Popularity)
                .ThenByDescending(m => m.VoteAverage)
                .ToList();

            // Rotate only within candidates that have real backdrop artwork.
            var backdropPool = allRanked
                .Where(m => GetArtworkScore(m) >= 3)
                .Take(5)
                .ToList();

            if (backdropPool.Count > 0)
            {
                return backdropPool[_sessionRotationIndex % backdropPool.Count];
            }

            return allRanked.FirstOrDefault() ?? CreatePlaceholderFeaturedMovie();
        }

        private static int GetArtworkScore(MovieBrowseItemViewModel movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.BackdropUrl))
            {
                return 4;
            }

            if (!string.IsNullOrWhiteSpace(movie.TmdbBackdropPath))
            {
                return 3;
            }

            if (!string.IsNullOrWhiteSpace(movie.PosterUrl))
            {
                return 2;
            }

            return string.IsNullOrWhiteSpace(movie.TmdbPosterPath) ? 0 : 1;
        }

        private static MovieBrowseItemViewModel CreatePlaceholderFeaturedMovie()
        {
            return new MovieBrowseItemViewModel(
                new CatalogMovieGroup
                {
                    GroupKey = "placeholder",
                    PreferredMovie = new Movie
                    {
                        Title = "Movies",
                        Overview = "Sync an Xtream VOD source to build a poster-first library with TMDb artwork, ratings, genres, and backdrops.",
                        CategoryName = "VOD library"
                    },
                    Variants = new List<CatalogMovieVariant>()
                },
                false);
        }

        private void StartMetadataEnrichment()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var metadataService = scope.ServiceProvider.GetRequiredService<ITmdbMetadataService>();
                    var movies = await db.Movies.Take(36).ToListAsync();
                    await metadataService.EnrichMoviesAsync(db, movies, 36);
                }
                catch
                {
                }
            });
        }

        private static CatalogMovieGroup? FilterGroup(CatalogMovieGroup group, ProfileAccessSnapshot access)
        {
            var variants = group.Variants
                .Where(variant => access.IsMovieAllowed(variant.Movie))
                .ToList();

            if (variants.Count == 0)
            {
                return null;
            }

            return new CatalogMovieGroup
            {
                GroupKey = group.GroupKey,
                PreferredMovie = variants[0].Movie,
                Variants = variants,
                SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
            };
        }

        private static string BuildSourceSummary(IEnumerable<string> sourceNames)
        {
            var distinct = sourceNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                return string.Empty;
            }

            if (distinct.Count == 1)
            {
                return distinct[0];
            }

            if (distinct.Count == 2)
            {
                return $"{distinct[0]} + {distinct[1]}";
            }

            return $"{distinct[0]} +{distinct.Count - 1} more";
        }
    }
}
