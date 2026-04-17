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
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class FavoritesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<BrowserChannelViewModel> FavoriteChannels { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(ContentVisibility))]
        [NotifyPropertyChangedFor(nameof(FavoriteCountText))]
        private bool _isEmpty;

        public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContentVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        public string FavoriteCountText => FavoriteChannels.Count == 1 ? "1 saved channel" : $"{FavoriteChannels.Count} saved channels";

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

            IsEmpty = FavoriteChannels.Count == 0;
            OnPropertyChanged(nameof(FavoriteCountText));
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
                IsEmpty = FavoriteChannels.Count == 0;
                OnPropertyChanged(nameof(FavoriteCountText));
            }
        }
    }
}
