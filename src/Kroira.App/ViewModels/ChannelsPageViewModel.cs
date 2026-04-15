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

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isEmpty;

        partial void OnSearchQueryChanged(string value)
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

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            var filtered = string.IsNullOrWhiteSpace(SearchQuery)
                ? _allChannels
                : _allChannels.Where(c => c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

            foreach (var ch in filtered)
            {
                FilteredChannels.Add(ch);
            }

            IsEmpty = FilteredChannels.Count == 0;
        }
    }
}
