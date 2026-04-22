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
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class ProgressItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int ProgressId { get; set; }
        public int SeriesId { get; set; }
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public double ProgressPercent { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public string TypeLabel { get; set; } = string.Empty;
        public string ResumeContextText { get; set; } = string.Empty;
        public string LastWatchedText { get; set; } = string.Empty;
        public long SavedPositionMs { get; set; }
        public bool IsWatched { get; set; }
        public bool CanRemove { get; set; }
        public Visibility RemoveVisibility => CanRemove ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MarkWatchedVisibility => ContentType == PlaybackContentType.Channel || IsWatched ? Visibility.Collapsed : Visibility.Visible;
        public Visibility MarkUnwatchedVisibility => ContentType == PlaybackContentType.Channel || !IsWatched ? Visibility.Collapsed : Visibility.Visible;
    }

    public partial class ContinueWatchingViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private bool _isLoadingPreferences;

        public ObservableCollection<ProgressItemViewModel> ProgressItems { get; } = new();
        public ObservableCollection<ProgressItemViewModel> LiveProgressItems { get; } = new();
        public ObservableCollection<ProgressItemViewModel> MovieProgressItems { get; } = new();
        public ObservableCollection<ProgressItemViewModel> SeriesProgressItems { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(ContentVisibility))]
        [NotifyPropertyChangedFor(nameof(LiveCountText))]
        [NotifyPropertyChangedFor(nameof(MovieCountText))]
        [NotifyPropertyChangedFor(nameof(SeriesCountText))]
        [NotifyPropertyChangedFor(nameof(LiveEmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(MovieEmptyVisibility))]
        [NotifyPropertyChangedFor(nameof(SeriesEmptyVisibility))]
        private bool _isEmpty = true;

        [ObservableProperty]
        private bool _hideWatchedItems = true;

        [ObservableProperty]
        private SurfaceStatePresentation _surfaceState = SurfaceStateCopies.ContinueWatching.Create(SurfaceViewState.Loading);

        public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContentVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        public string LiveCountText => LiveProgressItems.Count == 1 ? "1 live item" : $"{LiveProgressItems.Count} live items";
        public string MovieCountText => MovieProgressItems.Count == 1 ? "1 movie" : $"{MovieProgressItems.Count} movies";
        public string SeriesCountText => SeriesProgressItems.Count == 1 ? "1 series" : $"{SeriesProgressItems.Count} series";
        public Visibility LiveEmptyVisibility => LiveProgressItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MovieEmptyVisibility => MovieProgressItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SeriesEmptyVisibility => SeriesProgressItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ContinueWatchingViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        partial void OnHideWatchedItemsChanged(bool value)
        {
            if (_isLoadingPreferences)
            {
                return;
            }

            _ = SaveHideWatchedPreferenceAsync(value);
        }

        [RelayCommand]
        public async Task LoadProgressAsync()
        {
            SurfaceState = SurfaceStateCopies.ContinueWatching.Create(SurfaceViewState.Loading);
            ProgressItems.Clear();
            LiveProgressItems.Clear();
            MovieProgressItems.Clear();
            SeriesProgressItems.Clear();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
                var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
                var surfaceStateService = scope.ServiceProvider.GetRequiredService<ISurfaceStateService>();
                var access = await profileService.GetAccessSnapshotAsync(db);
                await logicalCatalogStateService.ReconcilePlaybackProgressAsync(db, access.ProfileId);

                _isLoadingPreferences = true;
                try
                {
                    HideWatchedItems = await watchStateService.GetHideWatchedInContinueAsync(db, access.ProfileId);
                }
                finally
                {
                    _isLoadingPreferences = false;
                }

                var liveProgressRows = await db.PlaybackProgresses
                    .Where(progress => progress.ProfileId == access.ProfileId &&
                                       progress.ContentType == PlaybackContentType.Channel &&
                                       !progress.IsCompleted)
                    .OrderByDescending(progress => progress.LastWatched)
                    .ToListAsync();

                var movieProgressRows = await db.PlaybackProgresses
                    .Where(progress => progress.ProfileId == access.ProfileId &&
                                       progress.ContentType == PlaybackContentType.Movie)
                    .OrderByDescending(progress => progress.LastWatched)
                    .ToListAsync();

                var episodeProgressRows = await db.PlaybackProgresses
                    .Where(progress => progress.ProfileId == access.ProfileId &&
                                       progress.ContentType == PlaybackContentType.Episode)
                    .OrderByDescending(progress => progress.LastWatched)
                    .ToListAsync();

                await LoadLiveProgressItemsAsync(db, access, liveProgressRows);
                await LoadMovieProgressItemsAsync(db, access, watchStateService, movieProgressRows);
                await LoadSeriesProgressItemsAsync(db, access, watchStateService, episodeProgressRows);

                IsEmpty = ProgressItems.Count == 0;
                NotifyCountsChanged();
                SurfaceState = surfaceStateService.ResolveLocalState(ProgressItems.Count, SurfaceStateCopies.ContinueWatching);
            }
            catch (Exception ex)
            {
                _isLoadingPreferences = false;
                BrowseRuntimeLogger.Log("CONTINUE", $"load failed {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.ContinueWatching, ex);
            }
        }

        [RelayCommand]
        public async Task RemoveProgressAsync(int progressId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var progress = await db.PlaybackProgresses.FirstOrDefaultAsync(existing => existing.Id == progressId && existing.ProfileId == activeProfileId);
            if (progress != null)
            {
                db.PlaybackProgresses.Remove(progress);
                await db.SaveChangesAsync();
            }

            await LoadProgressAsync();
        }

        [RelayCommand]
        public async Task MarkWatchedAsync(ProgressItemViewModel? item)
        {
            if (item == null || item.ContentType == PlaybackContentType.Channel)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            await watchStateService.MarkWatchedAsync(db, activeProfileId, item.ContentType, item.ContentId);
            await LoadProgressAsync();
        }

        [RelayCommand]
        public async Task MarkUnwatchedAsync(ProgressItemViewModel? item)
        {
            if (item == null || item.ContentType == PlaybackContentType.Channel)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            await watchStateService.MarkUnwatchedAsync(db, activeProfileId, item.ContentType, item.ContentId);
            await LoadProgressAsync();
        }

        private async Task LoadLiveProgressItemsAsync(AppDbContext db, ProfileAccessSnapshot access, IReadOnlyList<PlaybackProgress> liveProgressRows)
        {
            if (liveProgressRows.Count == 0)
            {
                return;
            }

            var channelIds = liveProgressRows.Select(progress => progress.ContentId).Distinct().ToList();
            var channels = await db.Channels.Where(channel => channelIds.Contains(channel.Id)).ToDictionaryAsync(channel => channel.Id);
            var channelCategoryIds = channels.Values.Select(channel => channel.ChannelCategoryId).Distinct().ToList();
            var channelCategories = await db.ChannelCategories
                .Where(category => channelCategoryIds.Contains(category.Id))
                .ToDictionaryAsync(category => category.Id);

            foreach (var progress in liveProgressRows)
            {
                if (!channels.TryGetValue(progress.ContentId, out var channel))
                {
                    continue;
                }

                if (!channelCategories.TryGetValue(channel.ChannelCategoryId, out var category) || !access.IsLiveChannelAllowed(channel, category))
                {
                    continue;
                }

                AddItem(new ProgressItemViewModel
                {
                    Id = progress.Id,
                    ProgressId = progress.Id,
                    ContentId = progress.ContentId,
                    ContentType = PlaybackContentType.Channel,
                    Title = channel.Name,
                    LogoUrl = channel.LogoUrl ?? string.Empty,
                    StreamUrl = channel.StreamUrl,
                    ProgressPercent = 0,
                    ProgressText = "LIVE",
                    TypeLabel = "Live channel",
                    ResumeContextText = "Return to this live source",
                    LastWatchedText = progress.LastWatched == default ? string.Empty : $"Saved {progress.LastWatched.ToLocalTime():MMM d, HH:mm}",
                    SavedPositionMs = progress.PositionMs,
                    CanRemove = true
                }, LiveProgressItems);
            }
        }

        private async Task LoadMovieProgressItemsAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            ILibraryWatchStateService watchStateService,
            IReadOnlyList<PlaybackProgress> movieProgressRows)
        {
            var movieIds = movieProgressRows.Select(progress => progress.ContentId).Distinct().ToList();
            if (movieIds.Count == 0)
            {
                return;
            }

            var snapshots = await watchStateService.LoadSnapshotsAsync(db, access.ProfileId, PlaybackContentType.Movie, movieIds);
            var movies = await db.Movies.Where(movie => movieIds.Contains(movie.Id)).ToDictionaryAsync(movie => movie.Id);

            foreach (var progress in movieProgressRows)
            {
                if (!movies.TryGetValue(progress.ContentId, out var movie) || !access.IsMovieAllowed(movie))
                {
                    continue;
                }

                if (!snapshots.TryGetValue(movie.Id, out var snapshot))
                {
                    continue;
                }

                if (HideWatchedItems && snapshot.IsWatched)
                {
                    continue;
                }

                AddItem(new ProgressItemViewModel
                {
                    Id = snapshot.ProgressId,
                    ProgressId = snapshot.ProgressId,
                    ContentId = movie.Id,
                    ContentType = PlaybackContentType.Movie,
                    Title = movie.Title,
                    LogoUrl = movie.DisplayPosterUrl,
                    StreamUrl = movie.StreamUrl,
                    ProgressPercent = snapshot.ProgressPercent,
                    ProgressText = snapshot.IsWatched
                        ? "WATCHED"
                        : snapshot.HasResumePoint
                            ? TimeSpan.FromMilliseconds(snapshot.ResumePositionMs).ToString(@"hh\:mm\:ss")
                            : "NEXT",
                    TypeLabel = "Movie",
                    ResumeContextText = snapshot.IsWatched
                        ? "Marked watched"
                        : snapshot.HasResumePoint
                            ? $"Resume from {TimeSpan.FromMilliseconds(snapshot.ResumePositionMs):hh\\:mm\\:ss}"
                            : "Ready to start",
                    LastWatchedText = snapshot.LastWatched == default
                        ? string.Empty
                        : $"{(snapshot.IsWatched ? "Watched" : "Saved")} {snapshot.LastWatched.ToLocalTime():MMM d, HH:mm}",
                    SavedPositionMs = snapshot.ResumePositionMs,
                    IsWatched = snapshot.IsWatched,
                    CanRemove = true
                }, MovieProgressItems);
            }
        }

        private async Task LoadSeriesProgressItemsAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            ILibraryWatchStateService watchStateService,
            IReadOnlyList<PlaybackProgress> episodeProgressRows)
        {
            var episodeIds = episodeProgressRows.Select(progress => progress.ContentId).Distinct().ToList();
            if (episodeIds.Count == 0)
            {
                return;
            }

            var episodeEntities = await db.Episodes.Where(episode => episodeIds.Contains(episode.Id)).ToDictionaryAsync(episode => episode.Id);
            var seasonIds = episodeEntities.Values.Select(episode => episode.SeasonId).Distinct().ToList();
            var seasonEntities = await db.Seasons.Where(season => seasonIds.Contains(season.Id)).ToDictionaryAsync(season => season.Id);
            var seriesIds = seasonEntities.Values.Select(season => season.SeriesId).Distinct().ToList();
            var seriesList = await db.Series
                .AsNoTracking()
                .Include(series => series.Seasons!)
                .ThenInclude(season => season.Episodes)
                .Where(series => seriesIds.Contains(series.Id))
                .ToListAsync();

            var episodeSnapshots = await watchStateService.LoadSnapshotsAsync(db, access.ProfileId, PlaybackContentType.Episode, episodeIds);

            foreach (var selection in seriesList
                         .Where(access.IsSeriesAllowed)
                         .Select(series => watchStateService.BuildSeriesQueueSelection(series, episodeSnapshots, includeWatched: !HideWatchedItems))
                         .Where(selection => selection != null)
                         .Cast<SeriesQueueSelection>()
                         .OrderByDescending(selection => selection.SortAtUtc))
            {
                var episodeCode = $"S{selection.Season.SeasonNumber:00} E{selection.Episode.EpisodeNumber:00}";
                AddItem(new ProgressItemViewModel
                {
                    Id = selection.EpisodeSnapshot?.ProgressId ?? 0,
                    ProgressId = selection.EpisodeSnapshot?.ProgressId ?? 0,
                    SeriesId = selection.Series.Id,
                    ContentId = selection.Episode.Id,
                    ContentType = PlaybackContentType.Episode,
                    Title = selection.Series.Title,
                    LogoUrl = selection.Series.DisplayPosterUrl,
                    StreamUrl = selection.Episode.StreamUrl,
                    ProgressPercent = selection.EpisodeSnapshot?.ProgressPercent ?? (selection.IsWatched ? 100 : 0),
                    ProgressText = selection.IsResumeCandidate
                        ? TimeSpan.FromMilliseconds(selection.ResumePositionMs).ToString(@"hh\:mm\:ss")
                        : $"{selection.WatchedEpisodeCount}/{selection.TotalEpisodeCount}",
                    TypeLabel = "Series",
                    ResumeContextText = selection.IsWatched
                        ? $"Watched through {episodeCode}"
                        : selection.IsResumeCandidate
                            ? $"Resume {episodeCode} / {selection.Episode.Title}"
                            : $"Next up {episodeCode} / {selection.Episode.Title}",
                    LastWatchedText = selection.SortAtUtc == default
                        ? string.Empty
                        : $"{(selection.IsWatched ? "Watched" : "Saved")} {selection.SortAtUtc.ToLocalTime():MMM d, HH:mm}",
                    SavedPositionMs = selection.ResumePositionMs,
                    IsWatched = selection.IsWatched,
                    CanRemove = selection.EpisodeSnapshot?.ProgressId > 0
                }, SeriesProgressItems);
            }
        }

        private void AddItem(ProgressItemViewModel item, ObservableCollection<ProgressItemViewModel> bucket)
        {
            ProgressItems.Add(item);
            bucket.Add(item);
        }

        private async Task SaveHideWatchedPreferenceAsync(bool value)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            await watchStateService.SetHideWatchedInContinueAsync(db, activeProfileId, value);
            await LoadProgressAsync();
        }

        private void NotifyCountsChanged()
        {
            OnPropertyChanged(nameof(LiveCountText));
            OnPropertyChanged(nameof(MovieCountText));
            OnPropertyChanged(nameof(SeriesCountText));
            OnPropertyChanged(nameof(LiveEmptyVisibility));
            OnPropertyChanged(nameof(MovieEmptyVisibility));
            OnPropertyChanged(nameof(SeriesEmptyVisibility));
        }
    }
}
