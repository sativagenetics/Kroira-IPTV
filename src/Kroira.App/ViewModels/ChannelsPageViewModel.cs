#nullable enable
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
    public partial class ChannelsPageViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<BrowserChannelViewModel> _allChannels = new();

        public ObservableCollection<BrowserChannelViewModel> FilteredChannels { get; } = new();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            ApplyFilter();
        }

        public ChannelsPageViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadChannelsAsync()
        {
            _allChannels.Clear();
            Categories.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Load all categories across all sources
            var cats = await db.ChannelCategories
                .OrderBy(c => c.Name)
                .ToListAsync();

            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });
            foreach (var c in cats)
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    OrderIndex = c.OrderIndex
                });
            }

            SelectedCategory = Categories.FirstOrDefault();

            // Load all channels
            var channels = await db.Channels.ToListAsync();
            var favIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            foreach (var ch in channels)
            {
                _allChannels.Add(new BrowserChannelViewModel
                {
                    Id = ch.Id,
                    CategoryId = ch.ChannelCategoryId,
                    Name = ch.Name,
                    StreamUrl = ch.StreamUrl,
                    LogoUrl = ch.LogoUrl ?? string.Empty,
                    IsFavorite = favIds.Contains(ch.Id)
                });
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredChannels.Clear();

            var query = _allChannels.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                query = query.Where(c => c.CategoryId == SelectedCategory.Id);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                query = query.Where(c => c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var ch in query)
            {
                FilteredChannels.Add(ch);
            }

            IsEmpty = FilteredChannels.Count == 0;
        }
    }
}

