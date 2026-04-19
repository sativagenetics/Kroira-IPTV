#nullable enable
using System;
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
        public Series Series { get; set; } = new Series();
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string HeroArtworkUrl { get; set; } = string.Empty;
        public string MetadataLine { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public ObservableCollection<Season> Seasons { get; } = new();
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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SeriesDetailVisibility))]
        [NotifyPropertyChangedFor(nameof(SeriesDetailEmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(SelectedSeriesEpisodesVisibility))]
        [NotifyPropertyChangedFor(nameof(SelectedSeriesEpisodesEmptyVisibility))]
        private FavoriteSeriesViewModel? _selectedSeries;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedSeriesEpisodesVisibility))]
        [NotifyPropertyChangedFor(nameof(SelectedSeriesEpisodesEmptyVisibility))]
        private Season? _selectedSeason;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedEpisodePlayVisibility))]
        private Episode? _selectedEpisode;

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
        public Visibility SeriesDetailVisibility => SelectedSeries == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SeriesDetailEmptyVisibility => SelectedSeries == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedSeriesEpisodesVisibility => SelectedSeason?.Episodes?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedSeriesEpisodesEmptyVisibility => SelectedSeries != null && !(SelectedSeason?.Episodes?.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectedEpisodePlayVisibility => SelectedEpisode == null ? Visibility.Collapsed : Visibility.Visible;

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
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var access = await profileService.GetAccessSnapshotAsync(db);

            var channelIds = await db.Favorites
                .Where(f => f.ProfileId == access.ProfileId && f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (channelIds.Count > 0)
            {
                var channels = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToListAsync();
                var categoryIds = channels.Select(channel => channel.ChannelCategoryId).Distinct().ToList();
                var categories = await db.ChannelCategories
                    .Where(category => categoryIds.Contains(category.Id))
                    .ToDictionaryAsync(category => category.Id);
                foreach (var ch in channels)
                {
                    if (!categories.TryGetValue(ch.ChannelCategoryId, out var category) || !access.IsLiveChannelAllowed(ch, category))
                    {
                        continue;
                    }

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
                .Where(f => f.ProfileId == access.ProfileId && f.ContentType == FavoriteType.Movie)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (movieIds.Count > 0)
            {
                var movies = await db.Movies.Where(m => movieIds.Contains(m.Id)).ToListAsync();
                foreach (var movie in movies.Where(access.IsMovieAllowed).OrderBy(m => m.Title))
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
                .Where(f => f.ProfileId == access.ProfileId && f.ContentType == FavoriteType.Series)
                .Select(f => f.ContentId)
                .ToListAsync();

            if (seriesIds.Count > 0)
            {
                var series = await db.Series
                    .AsNoTracking()
                    .Include(s => s.Seasons!)
                    .ThenInclude(season => season.Episodes)
                    .Where(s => seriesIds.Contains(s.Id))
                    .ToListAsync();
                foreach (var show in series.Where(access.IsSeriesAllowed).OrderBy(s => s.Title))
                {
                    FavoriteSeries.Add(BuildFavoriteSeriesViewModel(show));
                }
            }

            IsEmpty = TotalFavorites == 0;
            FeaturedChannel = FavoriteChannels.FirstOrDefault();
            NotifyCountsChanged();
        }

        partial void OnSelectedSeriesChanged(FavoriteSeriesViewModel? value)
        {
            SelectedEpisode = null;
            if (value == null)
            {
                SelectedSeason = null;
            }
        }

        partial void OnSelectedSeasonChanged(Season? value)
        {
            var playableEpisodes = value?.Episodes?
                .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                .OrderBy(episode => episode.EpisodeNumber)
                .ToList();

            SelectedEpisode = playableEpisodes?.Count == 1 ? playableEpisodes[0] : null;
        }

        public async Task SelectSeriesAsync(int seriesId)
        {
            ClearSelectedSeries();

            if (seriesId <= 0)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var show = await db.Series
                .AsNoTracking()
                .Include(s => s.Seasons!)
                .ThenInclude(season => season.Episodes)
                .FirstOrDefaultAsync(s => s.Id == seriesId);

            if (show == null)
            {
                return;
            }

            var hydratedSeries = BuildFavoriteSeriesViewModel(show);
            SelectedSeries = hydratedSeries;
            SelectedSeason = hydratedSeries.Seasons.FirstOrDefault(season => season.Episodes?.Count > 0)
                ?? hydratedSeries.Seasons.FirstOrDefault();
        }

        public void SelectEpisode(Episode episode)
        {
            SelectedEpisode = episode;
        }

        public void ClearSelectedSeries()
        {
            SelectedSeries = null;
            SelectedSeason = null;
            SelectedEpisode = null;
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = FavoriteChannels.FirstOrDefault(c => c.Id == channelId);
            if (target != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var activeProfileId = await profileService.GetActiveProfileIdAsync(db);

                var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Channel && f.ContentId == channelId);
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
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Movie && f.ContentId == movieId);
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
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Series && f.ContentId == seriesId);
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

            if (SelectedSeries?.Id == seriesId)
            {
                ClearSelectedSeries();
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

        private static FavoriteSeriesViewModel BuildFavoriteSeriesViewModel(Series show)
        {
            var favoriteSeries = new FavoriteSeriesViewModel
            {
                Series = show,
                Id = show.Id,
                Title = show.Title,
                PosterUrl = show.DisplayPosterUrl,
                HeroArtworkUrl = show.DisplayHeroArtworkUrl,
                MetadataLine = show.MetadataLine,
                Overview = show.Overview
            };

            var seasons = (show.Seasons ?? Array.Empty<Season>())
                .OrderBy(season => season.SeasonNumber)
                .ToList();

            foreach (var season in seasons)
            {
                season.Episodes = (season.Episodes ?? Array.Empty<Episode>())
                    .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                    .OrderBy(episode => episode.EpisodeNumber)
                    .ToList();

                favoriteSeries.Seasons.Add(season);
            }

            return favoriteSeries;
        }
    }
}
