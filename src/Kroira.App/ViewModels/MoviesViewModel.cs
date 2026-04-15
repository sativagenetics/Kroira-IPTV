using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Kroira.App.ViewModels
{
    public partial class MoviesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<Movie> _allMovies = new List<Movie>();

        public ObservableCollection<Movie> FilteredMovies { get; } = new ObservableCollection<Movie>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isEmpty;

        partial void OnSearchQueryChanged(string value)
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
            _allMovies = await db.Movies.OrderBy(m => m.Title).ToListAsync();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredMovies.Clear();
            var filtered = string.IsNullOrWhiteSpace(SearchQuery) 
                ? _allMovies 
                : _allMovies.Where(m => m.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            
            foreach (var item in filtered)
            {
                FilteredMovies.Add(item);
            }

            IsEmpty = FilteredMovies.Count == 0;
        }
    }
}
