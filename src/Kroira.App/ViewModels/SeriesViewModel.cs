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
    public partial class SeriesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<Series> _allSeries = new List<Series>();

        public ObservableCollection<Series> FilteredSeries { get; } = new ObservableCollection<Series>();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new ObservableCollection<BrowserCategoryViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private Series? _selectedSeries;

        [ObservableProperty]
        private Season? _selectedSeason;

        [ObservableProperty]
        private bool _isEmpty;
        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            SelectedSeries = null;
            ApplyFilter();
        }

        partial void OnSelectedSeriesChanged(Series? value)
        {
            if (value != null && value.Seasons != null)
            {
                SelectedSeason = value.Seasons.OrderBy(s => s.SeasonNumber).FirstOrDefault();
            }
            else
            {
                SelectedSeason = null;
            }
        }

        public SeriesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadSeriesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rawSeries = await db.Series
                .Include(s => s.Seasons!)
                .ThenInclude(sn => sn.Episodes)
                .OrderBy(s => s.Title)
                .ToListAsync();

            var categoryLabels = rawSeries
                .Select(s => s.CategoryName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(NormalizeCatalogLabel)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _allSeries = rawSeries
                .Where(s => IsPlayableSeries(s, categoryLabels))
                .ToList();

            foreach (var s in _allSeries)
            {
                if (s.Seasons != null)
                {
                    s.Seasons = s.Seasons.OrderBy(sn => sn.SeasonNumber).ToList();
                    foreach (var sn in s.Seasons)
                    {
                        if (sn.Episodes != null)
                        {
                            sn.Episodes = sn.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
                        }
                    }
                }
            }

            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });

            var categoryIndex = 1;
            foreach (var categoryName in _allSeries
                         .Select(s => string.IsNullOrWhiteSpace(s.CategoryName) ? "Uncategorized" : s.CategoryName.Trim())
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
            FilteredSeries.Clear();
            var filtered = _allSeries.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                filtered = filtered.Where(s =>
                    string.Equals(GetDisplayCategory(s.CategoryName), SelectedCategory.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(s => s.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in filtered)
            {
                FilteredSeries.Add(item);
            }

            IsEmpty = FilteredSeries.Count == 0;
        }

        private static bool IsPlayableSeries(Series series, HashSet<string> categoryLabels)
        {
            if (string.IsNullOrWhiteSpace(series.Title)) return false;
            if (categoryLabels.Contains(NormalizeCatalogLabel(series.Title))) return false;

            return series.Seasons != null &&
                   series.Seasons.Any(season =>
                       season.Episodes != null &&
                       season.Episodes.Any(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl)));
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
