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
    public partial class MoviesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<Movie> _allMovies = new List<Movie>();

        public ObservableCollection<Movie> FilteredMovies { get; } = new ObservableCollection<Movie>();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new ObservableCollection<BrowserCategoryViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private Movie _featuredMovie = new Movie
        {
            Title = "Movies",
            Overview = "Sync an Xtream VOD source to build a poster-first library with TMDb artwork, ratings, genres, and backdrops.",
            CategoryName = "VOD library"
        };

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

            _allMovies = CatalogOrderingService
                .OrderCatalog(rawMovies, languageCode, m => m.CategoryName, m => m.Title)
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
            FeaturedMovie = SelectFeaturedMovie(FilteredMovies);
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private static Movie SelectFeaturedMovie(IEnumerable<Movie> movies)
        {
            var featured = movies
                .OrderByDescending(m => GetArtworkScore(m))
                .ThenByDescending(m => m.Popularity)
                .ThenByDescending(m => m.VoteAverage)
                .FirstOrDefault();

            return featured ?? new Movie
            {
                Title = "Movies",
                Overview = "Sync an Xtream VOD source to build a poster-first library with TMDb artwork, ratings, genres, and backdrops.",
                CategoryName = "VOD library"
            };
        }

        private static int GetArtworkScore(Movie movie)
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
