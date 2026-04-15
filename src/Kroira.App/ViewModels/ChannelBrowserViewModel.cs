using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Models;

namespace Kroira.App.ViewModels
{
    public partial class BrowserCategoryViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
    }

    public partial class BrowserChannelViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
        private bool _isFavorite;

        public string FavoriteIcon => IsFavorite ? "★" : "☆";
    }

    public partial class ChannelBrowserViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private int _sourceProfileId;
        private System.Collections.Generic.List<BrowserChannelViewModel> _allChannelsCache = new();

        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowserChannelViewModel> DisplayedChannels { get; } = new();

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        public ChannelBrowserViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task LoadSourceAsync(int sourceProfileId)
        {
            _sourceProfileId = sourceProfileId;
            Categories.Clear();
            DisplayedChannels.Clear();
            _allChannelsCache.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cats = await db.ChannelCategories
                .Where(c => c.SourceProfileId == sourceProfileId)
                .OrderBy(c => c.OrderIndex)
                .ToListAsync();

            var catIds = cats.Select(c => c.Id).ToList();
            var chans = await db.Channels
                .Where(ch => catIds.Contains(ch.ChannelCategoryId))
                .ToListAsync();

            var favIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
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

            foreach (var ch in chans)
            {
                _allChannelsCache.Add(new BrowserChannelViewModel
                {
                    Id = ch.Id,
                    CategoryId = ch.ChannelCategoryId,
                    Name = ch.Name,
                    StreamUrl = ch.StreamUrl,
                    LogoUrl = ch.LogoUrl ?? string.Empty,
                    IsFavorite = favIds.Contains(ch.Id)
                });
            }

            SelectedCategory = Categories.FirstOrDefault();
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            ApplyFilter();
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = _allChannelsCache.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                query = query.Where(c => c.CategoryId == SelectedCategory.Id);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                query = query.Where(c => c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            DisplayedChannels.Clear();
            foreach (var ch in query)
            {
                DisplayedChannels.Add(ch);
            }
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = DisplayedChannels.FirstOrDefault(c => c.Id == channelId);
            if (target != null)
            {
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
}
