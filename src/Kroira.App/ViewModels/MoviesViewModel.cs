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
            var rawMovies = await db.Movies
                .Where(m => m.StreamUrl != null && m.StreamUrl != "")
                .OrderBy(m => m.Title)
                .ToListAsync();

            var categoryLabels = rawMovies
                .Select(m => m.CategoryName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(NormalizeCatalogLabel)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _allMovies = rawMovies
                .Where(m => IsPlayableMovie(m, categoryLabels))
                .ToList();

            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });

            var categoryIndex = 1;
            foreach (var categoryName in _allMovies
                         .Select(m => string.IsNullOrWhiteSpace(m.CategoryName) ? "Uncategorized" : m.CategoryName.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(c => c))
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

        private static bool IsPlayableMovie(Movie movie, HashSet<string> categoryLabels)
        {
            if (string.IsNullOrWhiteSpace(movie.StreamUrl)) return false;
            if (string.IsNullOrWhiteSpace(movie.Title)) return false;

            return !categoryLabels.Contains(NormalizeCatalogLabel(movie.Title));
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private static string NormalizeCatalogLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return string.Join(" ", value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
