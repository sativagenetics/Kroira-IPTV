using System;
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
    public partial class FavoritesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<BrowserChannelViewModel> FavoriteChannels { get; } = new();

        public FavoritesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadFavoritesAsync()
        {
            FavoriteChannels.Clear();
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var favs = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (favs.Count > 0)
            {
                var channels = await db.Channels.Where(c => favs.Contains(c.Id)).ToListAsync();
                foreach (var ch in channels)
                {
                    FavoriteChannels.Add(new BrowserChannelViewModel
                    {
                        Id = ch.Id,
                        CategoryId = ch.ChannelCategoryId,
                        Name = ch.Name,
                        StreamUrl = ch.StreamUrl,
                        LogoUrl = ch.LogoUrl ?? string.Empty,
                        IsFavorite = true
                    });
                }
            }
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = FavoriteChannels.FirstOrDefault(c => c.Id == channelId);
            if (target != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Channel && f.ContentId == channelId);
                if (fav != null)
                {
                    db.Favorites.Remove(fav);
                    await db.SaveChangesAsync();
                }
                FavoriteChannels.Remove(target);
            }
        }
    }
}
