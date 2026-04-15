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
    public partial class SeriesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<Series> _allSeries = new List<Series>();

        public ObservableCollection<Series> FilteredSeries { get; } = new ObservableCollection<Series>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private Series? _selectedSeries;

        [ObservableProperty]
        private Season? _selectedSeason;

        partial void OnSearchQueryChanged(string value)
        {
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
            _allSeries = await db.Series
                .Include(s => s.Seasons!)
                .ThenInclude(sn => sn.Episodes)
                .OrderBy(s => s.Title)
                .ToListAsync();
            
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

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredSeries.Clear();
            var filtered = string.IsNullOrWhiteSpace(SearchQuery) 
                ? _allSeries 
                : _allSeries.Where(s => s.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            
            foreach (var item in filtered)
            {
                FilteredSeries.Add(item);
            }
        }
    }
}
