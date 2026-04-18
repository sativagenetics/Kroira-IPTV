#nullable enable
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
    public partial class FavoriteMovieViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string MetadataLine { get; set; } = string.Empty;
    }

    public partial class FavoriteSeriesViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string MetadataLine { get; set; } = string.Empty;
    }

    public partial class FavoritesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<BrowserChannelViewModel> FavoriteChannels { get; } = new();
        public ObservableCollection<FavoriteMovieViewModel> FavoriteMovies { get; } = new();
        public ObservableCollection<FavoriteSeriesViewModel> FavoriteSeries { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(ContentVisibility))]
        [NotifyPropertyChangedFor(nameof(FavoriteCountText))]
        [NotifyPropertyChangedFor(nameof(ChannelCountText))]
        [NotifyPropertyChangedFor(nameof(MovieCountText))]
        [NotifyPropertyChangedFor(nameof(SeriesCountText))]
        [NotifyPropertyChangedFor(nameof(ChannelsEmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(MoviesEmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(SeriesEmptyVisibility))]
        private bool _isEmpty;

        [ObservableProperty]
        private BrowserChannelViewModel? _featuredChannel;

        public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContentVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        public string FavoriteCountText => TotalFavorites == 1 ? "1 saved item" : $"{TotalFavorites} saved items";
        public string ChannelCountText => FavoriteChannels.Count == 1 ? "1 channel" : $"{FavoriteChannels.Count} channels";
        public string MovieCountText => FavoriteMovies.Count == 1 ? "1 movie" : $"{FavoriteMovies.Count} movies";
        public string SeriesCountText => FavoriteSeries.Count == 1 ? "1 series" : $"{FavoriteSeries.Count} series";
        public int TotalFavorites => FavoriteChannels.Count + FavoriteMovies.Count + FavoriteSeries.Count;
        public Visibility ChannelsEmptyVisibility => FavoriteChannels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MoviesEmptyVisibility => FavoriteMovies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SeriesEmptyVisibility => FavoriteSeries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public FavoritesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadFavoritesAsync()
        {
            FavoriteChannels.Clear();
            FavoriteMovies.Clear();
            FavoriteSeries.Clear();
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var channelIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (channelIds.Count > 0)
            {
                var channels = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToListAsync();
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

            var movieIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Movie)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (movieIds.Count > 0)
            {
                var movies = await db.Movies.Where(m => movieIds.Contains(m.Id)).ToListAsync();
                foreach (var movie in movies.OrderBy(m => m.Title))
                {
                    FavoriteMovies.Add(new FavoriteMovieViewModel
                    {
                        Id = movie.Id,
                        Title = movie.Title,
                        PosterUrl = movie.DisplayPosterUrl,
                        StreamUrl = movie.StreamUrl,
                        MetadataLine = movie.MetadataLine
                    });
                }
            }

            var seriesIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Series)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (seriesIds.Count > 0)
            {
                var series = await db.Series.Where(s => seriesIds.Contains(s.Id)).ToListAsync();
                foreach (var show in series.OrderBy(s => s.Title))
                {
                    FavoriteSeries.Add(new FavoriteSeriesViewModel
                    {
                        Id = show.Id,
                        Title = show.Title,
                        PosterUrl = show.DisplayPosterUrl,
                        MetadataLine = show.MetadataLine
                    });
                }
            }

            IsEmpty = TotalFavorites == 0;
            FeaturedChannel = FavoriteChannels.FirstOrDefault();
            NotifyCountsChanged();
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
                IsEmpty = TotalFavorites == 0;
                FeaturedChannel = FavoriteChannels.FirstOrDefault();
                NotifyCountsChanged();
            }
        }

        [RelayCommand]
        public async Task RemoveMovieFavoriteAsync(int movieId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Movie && f.ContentId == movieId);
            if (fav != null)
            {
                db.Favorites.Remove(fav);
                await db.SaveChangesAsync();
            }

            var target = FavoriteMovies.FirstOrDefault(m => m.Id == movieId);
            if (target != null)
            {
                FavoriteMovies.Remove(target);
            }

            IsEmpty = TotalFavorites == 0;
            NotifyCountsChanged();
        }

        [RelayCommand]
        public async Task RemoveSeriesFavoriteAsync(int seriesId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Series && f.ContentId == seriesId);
            if (fav != null)
            {
                db.Favorites.Remove(fav);
                await db.SaveChangesAsync();
            }

            var target = FavoriteSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target != null)
            {
                FavoriteSeries.Remove(target);
            }

            IsEmpty = TotalFavorites == 0;
            NotifyCountsChanged();
        }

        private void NotifyCountsChanged()
        {
            OnPropertyChanged(nameof(FavoriteCountText));
            OnPropertyChanged(nameof(ChannelCountText));
            OnPropertyChanged(nameof(MovieCountText));
            OnPropertyChanged(nameof(SeriesCountText));
            OnPropertyChanged(nameof(TotalFavorites));
            OnPropertyChanged(nameof(ChannelsEmptyVisibility));
            OnPropertyChanged(nameof(MoviesEmptyVisibility));
            OnPropertyChanged(nameof(SeriesEmptyVisibility));
        }
    }
}
