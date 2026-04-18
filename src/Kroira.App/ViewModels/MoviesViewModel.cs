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

namespace Kroira.App.ViewModels
{
    public partial class MovieBrowseItemViewModel : ObservableObject
    {
        public MovieBrowseItemViewModel(Movie movie, bool isFavorite)
        {
            Movie = movie;
            IsFavorite = isFavorite;
        }

        public Movie Movie { get; }
        public int Id => Movie.Id;
        public string Title => Movie.Title;
        public string StreamUrl => Movie.StreamUrl;
        public string DisplayPosterUrl => Movie.DisplayPosterUrl;
        public string DisplayHeroArtworkUrl => Movie.DisplayHeroArtworkUrl;
        public string RatingText => Movie.RatingText;
        public string MetadataLine => Movie.MetadataLine;
        public string Overview => Movie.Overview;
        public string CategoryName => Movie.CategoryName;
        public double Popularity => Movie.Popularity;
        public double VoteAverage => Movie.VoteAverage;
        public string BackdropUrl => Movie.BackdropUrl;
        public string TmdbBackdropPath => Movie.TmdbBackdropPath;
        public string PosterUrl => Movie.PosterUrl;
        public string TmdbPosterPath => Movie.TmdbPosterPath;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
        [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
        private bool _isFavorite;

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
        public string FavoriteLabel => IsFavorite ? "Saved" : "Save";
    }

    public partial class MoviesViewModel : ObservableObject
    {
        private const string FixedFeaturedMovieTitle = "Kurtlar Vadisi Gladio";
        private readonly IServiceProvider _serviceProvider;
        private List<MovieBrowseItemViewModel> _allMovies = new List<MovieBrowseItemViewModel>();
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);

        public ObservableCollection<MovieBrowseItemViewModel> FilteredMovies { get; } = new ObservableCollection<MovieBrowseItemViewModel>();
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
            var languageCode = await AppLanguageService.GetLanguageAsync(db);
            var rawMovies = await db.Movies.ToListAsync();
            var favoriteIds = (await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Movie)
                .Select(f => f.ContentId)
                .ToListAsync())
                .ToHashSet();

            _allMovies = CatalogOrderingService
                .OrderCatalog(rawMovies, languageCode, m => m.CategoryName, m => m.Title)
                .Select(movie => new MovieBrowseItemViewModel(movie, favoriteIds.Contains(movie.Id)))
                .ToList();

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
            var favorite = await db.Favorites
                .FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Movie && f.ContentId == movieId);

            if (favorite == null)
            {
                db.Favorites.Add(new Favorite { ContentType = FavoriteType.Movie, ContentId = movieId });
                target.IsFavorite = true;
            }
            else
            {
                db.Favorites.Remove(favorite);
                target.IsFavorite = false;
            }

            await db.SaveChangesAsync();
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private void RefreshFeaturedMovie()
        {
            FeaturedMovie = _allMovies.FirstOrDefault(m =>
                string.Equals(m.Title.Trim(), FixedFeaturedMovieTitle, StringComparison.OrdinalIgnoreCase))
                ?? SelectFeaturedMovie(_allMovies);
        }

        private static MovieBrowseItemViewModel SelectFeaturedMovie(IEnumerable<MovieBrowseItemViewModel> movies)
        {
            var allRanked = movies
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
            return new MovieBrowseItemViewModel(new Movie
            {
                Title = "Movies",
                Overview = "Sync an Xtream VOD source to build a poster-first library with TMDb artwork, ratings, genres, and backdrops.",
                CategoryName = "VOD library"
            }, false);
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
    }
}
