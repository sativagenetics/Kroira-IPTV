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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

            var cats = await db.ChannelCategories
                .OrderBy(c => c.Name)
                .ToListAsync();
            var categoryMap = cats.ToDictionary(c => c.Id);
            var categoryLabels = ContentClassifier.BuildCategoryLabelSet(cats.Select(c => c.Name));
            var sourceTypes = await db.SourceProfiles
                .Select(source => new { source.Id, source.Type })
                .ToDictionaryAsync(source => source.Id, source => source.Type);
            var categorySourceTypes = cats.ToDictionary(
                category => category.Id,
                category => sourceTypes.TryGetValue(category.SourceProfileId, out var type) ? type : SourceType.M3U);

            var channels = (await db.Channels.ToListAsync())
                .Where(ch => categorySourceTypes.TryGetValue(ch.ChannelCategoryId, out var sourceType) &&
                             ContentClassifier.IsPlayableStoredLiveChannel(ch.Name, ch.StreamUrl, sourceType, categoryLabels))
                .Where(ch => categoryMap.ContainsKey(ch.ChannelCategoryId))
                .ToList();
            var favIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            // Build channel view models first — always succeeds regardless of EPG state
            var now = DateTime.UtcNow;
            var channelVMs = channels.Select(ch => new BrowserChannelViewModel
            {
                Id = ch.Id,
                CategoryId = ch.ChannelCategoryId,
                Name = ch.Name,
                StreamUrl = ch.StreamUrl,
                LogoUrl = ch.LogoUrl ?? string.Empty,
                IsFavorite = favIds.Contains(ch.Id)
            }).ToList();

            // EPG decoration — optional; failure leaves channels with neutral "No guide data" state
            try
            {
                var channelIds = channels.Select(c => c.Id).ToList();
                var epgWindowEnd = now.AddHours(8);
                var epgs = await db.EpgPrograms
                    .Where(e => channelIds.Contains(e.ChannelId)
                             && e.EndTimeUtc > now
                             && e.StartTimeUtc < epgWindowEnd)
                    .OrderBy(e => e.StartTimeUtc)
                    .ToListAsync();

                foreach (var item in channelVMs)
                {
                    var chEpg = epgs.Where(e => e.ChannelId == item.Id).ToList();
                    var current = chEpg.FirstOrDefault(e => e.StartTimeUtc <= now && e.EndTimeUtc > now);
                    var next = chEpg
                        .Where(e => e.StartTimeUtc >= (current?.EndTimeUtc ?? now))
                        .OrderBy(e => e.StartTimeUtc)
                        .FirstOrDefault();
                    item.ApplyEpg(current, next, now);
                }
            }
            catch
            {
                // EpgPrograms schema mismatch or missing columns — channels still shown, EPG skipped
            }

            foreach (var item in channelVMs)
                _allChannels.Add(item);

            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });
            foreach (var c in cats
                         .Where(c => _allChannels.Any(ch => ch.CategoryId == c.Id))
                         .OrderBy(c => c.Name))
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    OrderIndex = c.OrderIndex
                });
            }

            SelectedCategory = Categories.FirstOrDefault();
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

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = FilteredChannels.FirstOrDefault(c => c.Id == channelId)
                      ?? _allChannels.FirstOrDefault(c => c.Id == channelId);
            if (target == null) return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (target.IsFavorite)
            {
                var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Channel && f.ContentId == channelId);
                if (fav != null)
                {
                    db.Favorites.Remove(fav);
                    await db.SaveChangesAsync();
                }
                target.IsFavorite = false;
            }
            else
            {
                var fav = new Favorite { ContentType = FavoriteType.Channel, ContentId = channelId };
                db.Favorites.Add(fav);
                await db.SaveChangesAsync();
                target.IsFavorite = true;
            }
        }
    }
}
