#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage
    {
        private sealed class PlayerChannelSwitchItem
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string SourceName { get; init; } = string.Empty;
            public string StreamUrl { get; init; } = string.Empty;
            public string MetaText { get; init; } = string.Empty;
        }

        private sealed class PlayerEpisodeSwitchItem
        {
            public int Id { get; init; }
            public string StreamUrl { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string EpisodeLabel { get; init; } = string.Empty;
            public int SeasonNumber { get; init; }
            public int EpisodeNumber { get; init; }
            public long ResumePositionMs { get; init; }
        }

        private readonly ObservableCollection<PlayerChannelSwitchItem> _channelSwitchItems = new();
        private readonly ObservableCollection<PlayerEpisodeSwitchItem> _episodeSwitchItems = new();
        private readonly List<PlayerChannelSwitchItem> _allChannelSwitchItems = new();
        private readonly List<PlayerEpisodeSwitchItem> _allEpisodeSwitchItems = new();
        private readonly List<(double Speed, ToggleMenuFlyoutItem Item)> _speedItems = new();
        private readonly List<(string Key, ToggleMenuFlyoutItem Item)> _toggleToolItems = new();
        private readonly List<(double Scale, string Label)> _subtitleScalePresets = new()
        {
            (0.85, "Small"),
            (1.0, "Medium"),
            (1.2, "Large")
        };

        private IPlayerPreferencesService? _playerPreferencesService;
        private PlayerPreferencesSnapshot _playerPreferences = new();
        private DispatcherTimer? _zapBannerTimer;
        private DispatcherTimer? _sleepTimer;
        private DateTimeOffset? _sleepDeadline;
        private string _resolvedSourceName = string.Empty;
        private string _resolvedGuideSummary = string.Empty;
        private int _resolvedSourceProfileId;
        private FavoriteType? _favoriteType;
        private int _favoriteContentId;
        private int _lastChannelCandidateId;
        private bool _guidePanelOpen;
        private bool _channelPanelOpen;
        private bool _episodePanelOpen;
        private bool _infoPanelOpen;

        private void InitializeEnhancedPlayerUi(IServiceProvider services)
        {
            _playerPreferencesService = services.GetRequiredService<IPlayerPreferencesService>();
            ChannelSwitchList.ItemsSource = _channelSwitchItems;
            EpisodeSwitchList.ItemsSource = _episodeSwitchItems;
            BuildSpeedFlyout();
            BuildToolsFlyout();
            UpdateFavoriteUi();
            UpdatePanelVisibility();
            UpdatePlaybackHint();
            ClearGuidePanel();
            RefreshInfoPanel();
        }

        private async Task LoadEnhancedPlayerStateAsync(CancellationToken cancellationToken)
        {
            if (_context == null || _playerPreferencesService == null)
            {
                return;
            }

            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var guideService = scope.ServiceProvider.GetRequiredService<ILiveGuideService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var access = await profileService.GetAccessSnapshotAsync(db);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _playerPreferences = await _playerPreferencesService.LoadAsync(db, _context.ProfileId);
            ApplyEnhancedPreferencesToContext();

            _resolvedSourceName = string.Empty;
            _resolvedGuideSummary = string.Empty;
            _resolvedSourceProfileId = 0;
            _favoriteType = null;
            _favoriteContentId = 0;
            _allChannelSwitchItems.Clear();
            _channelSwitchItems.Clear();
            _allEpisodeSwitchItems.Clear();
            _episodeSwitchItems.Clear();
            _lastChannelCandidateId = 0;

            switch (_context.ContentType)
            {
                case PlaybackContentType.Channel:
                    await LoadLiveContentStateAsync(db, access, guideService, browsePreferencesService, cancellationToken);
                    break;
                case PlaybackContentType.Movie:
                    await LoadMovieContentStateAsync(db, access, cancellationToken);
                    break;
                case PlaybackContentType.Episode:
                    await LoadEpisodeContentStateAsync(db, access, profileService, cancellationToken);
                    break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            UpdateFavoriteUi();
            UpdateResolvedContextText();
            UpdateEnhancedControlState();
            UpdatePlaybackHint();
            RefreshSpeedUi();
            RefreshInfoPanel();
        }

        private async Task LoadLiveContentStateAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            ILiveGuideService guideService,
            IBrowsePreferencesService browsePreferencesService,
            CancellationToken cancellationToken)
        {
            if (_context == null)
            {
                return;
            }

            var channelRows = await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new { Channel = channel, Category = category })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.Category.SourceProfileId,
                    source => source.Id,
                    (item, source) => new
                    {
                        item.Channel,
                        item.Category,
                        SourceId = source.Id,
                        SourceName = source.Name
                    })
                .ToListAsync(cancellationToken);

            var visibleRows = channelRows
                .Where(row => access.IsLiveChannelAllowed(row.Channel, row.Category))
                .ToList();
            var currentRow = visibleRows.FirstOrDefault(row => row.Channel.Id == _context.ContentId);
            if (currentRow == null)
            {
                return;
            }

            _resolvedSourceName = currentRow.SourceName;
            _resolvedSourceProfileId = currentRow.SourceId;
            _favoriteType = FavoriteType.Channel;
            _favoriteContentId = currentRow.Channel.Id;
            _isFavorite = await db.Favorites.AnyAsync(
                favorite => favorite.ProfileId == _context.ProfileId &&
                            favorite.ContentType == FavoriteType.Channel &&
                            favorite.ContentId == currentRow.Channel.Id,
                cancellationToken);

            TitleText.Text = currentRow.Channel.Name;

            var browsePreferences = await browsePreferencesService.GetAsync(db, ProfileDomains.Live, _context.ProfileId);
            _lastChannelCandidateId = browsePreferences.RecentChannelIds.FirstOrDefault(id => id != currentRow.Channel.Id);

            var scopedRows = visibleRows
                .Where(row => row.SourceId == currentRow.SourceId)
                .OrderBy(row => row.Channel.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (scopedRows.Count <= 1)
            {
                scopedRows = visibleRows
                    .OrderBy(row => row.SourceName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(row => row.Channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            var guideSummaries = await guideService.GetGuideSummariesAsync(
                db,
                scopedRows.Select(row => row.Channel.Id).ToList(),
                DateTime.UtcNow);

            foreach (var row in scopedRows)
            {
                guideSummaries.TryGetValue(row.Channel.Id, out var summary);
                var meta = BuildChannelMeta(summary);
                _allChannelSwitchItems.Add(new PlayerChannelSwitchItem
                {
                    Id = row.Channel.Id,
                    Name = row.Channel.Name,
                    SourceName = row.SourceName,
                    StreamUrl = row.Channel.StreamUrl,
                    MetaText = meta
                });
            }

            ApplyChannelSearchFilter();
            if (guideSummaries.TryGetValue(currentRow.Channel.Id, out var currentSummary))
            {
                ApplyGuideSummary(currentSummary);
            }
            else
            {
                ClearGuidePanel();
            }
        }

        private async Task LoadMovieContentStateAsync(AppDbContext db, ProfileAccessSnapshot access, CancellationToken cancellationToken)
        {
            if (_context == null)
            {
                return;
            }

            var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(item => item.Id == _context.ContentId, cancellationToken);
            if (movie == null || !access.IsMovieAllowed(movie))
            {
                return;
            }

            _favoriteType = FavoriteType.Movie;
            _favoriteContentId = movie.Id;
            _isFavorite = await db.Favorites.AnyAsync(
                favorite => favorite.ProfileId == _context.ProfileId &&
                            favorite.ContentType == FavoriteType.Movie &&
                            favorite.ContentId == movie.Id,
                cancellationToken);
            _resolvedSourceName = movie.CategoryName;
            TitleText.Text = movie.Title;
            ContextText.Text = movie.MetadataLine;
            ContextText.Visibility = string.IsNullOrWhiteSpace(ContextText.Text) ? Visibility.Collapsed : Visibility.Visible;
            ClearGuidePanel();
        }

        private async Task LoadEpisodeContentStateAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            IProfileStateService profileService,
            CancellationToken cancellationToken)
        {
            if (_context == null)
            {
                return;
            }

            var episode = await db.Episodes.AsNoTracking().FirstOrDefaultAsync(item => item.Id == _context.ContentId, cancellationToken);
            if (episode == null)
            {
                return;
            }

            var season = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(item => item.Id == episode.SeasonId, cancellationToken);
            if (season == null)
            {
                return;
            }

            var series = await db.Series
                .AsNoTracking()
                .Include(item => item.Seasons!)
                .ThenInclude(item => item.Episodes)
                .FirstOrDefaultAsync(item => item.Id == season.SeriesId, cancellationToken);
            if (series == null || !access.IsSeriesAllowed(series))
            {
                return;
            }

            _favoriteType = FavoriteType.Series;
            _favoriteContentId = series.Id;
            _isFavorite = await db.Favorites.AnyAsync(
                favorite => favorite.ProfileId == _context.ProfileId &&
                            favorite.ContentType == FavoriteType.Series &&
                            favorite.ContentId == series.Id,
                cancellationToken);
            _resolvedSourceName = series.Title;
            TitleText.Text = series.Title;
            ContextText.Text = $"S{season.SeasonNumber:00} E{episode.EpisodeNumber:00}  {episode.Title}";
            ContextText.Visibility = Visibility.Visible;

            var episodeIds = (series.Seasons ?? Array.Empty<Season>())
                .SelectMany(item => item.Episodes ?? Array.Empty<Episode>())
                .Where(item => !string.IsNullOrWhiteSpace(item.StreamUrl))
                .Select(item => item.Id)
                .ToList();
            var watchStateService = ((App)Application.Current).Services.GetRequiredService<ILibraryWatchStateService>();
            var snapshots = await watchStateService.LoadSnapshotsAsync(db, _context.ProfileId, PlaybackContentType.Episode, episodeIds);

            foreach (var seriesSeason in (series.Seasons ?? Array.Empty<Season>()).OrderBy(item => item.SeasonNumber))
            {
                foreach (var seriesEpisode in (seriesSeason.Episodes ?? Array.Empty<Episode>()).Where(item => !string.IsNullOrWhiteSpace(item.StreamUrl)).OrderBy(item => item.EpisodeNumber))
                {
                    snapshots.TryGetValue(seriesEpisode.Id, out var snapshot);
                    _allEpisodeSwitchItems.Add(new PlayerEpisodeSwitchItem
                    {
                        Id = seriesEpisode.Id,
                        StreamUrl = seriesEpisode.StreamUrl,
                        Title = seriesEpisode.Title,
                        EpisodeLabel = $"S{seriesSeason.SeasonNumber:00} E{seriesEpisode.EpisodeNumber:00}",
                        SeasonNumber = seriesSeason.SeasonNumber,
                        EpisodeNumber = seriesEpisode.EpisodeNumber,
                        ResumePositionMs = snapshot?.ResumePositionMs ?? 0
                    });
                }
            }

            ApplyEpisodeFilter();
            EpisodePanelHeaderText.Text = "Season / episode quick panel";
            EpisodePanelStatusText.Text = $"{_allEpisodeSwitchItems.Count:N0} playable episodes in this series.";
            ClearGuidePanel();
        }

        private void ApplyEnhancedPreferencesToContext()
        {
            if (_context == null)
            {
                return;
            }

            _context.InitialVolume = _playerPreferences.Volume;
            _context.IsMuted = _playerPreferences.IsMuted;
            _context.InitialAspectMode = _playerPreferences.AspectMode;
            _context.InitialPlaybackSpeed = _playerPreferences.PlaybackSpeed;
            _context.AudioDelaySeconds = _playerPreferences.AudioDelaySeconds;
            _context.SubtitleDelaySeconds = _playerPreferences.SubtitleDelaySeconds;
            _context.SubtitleScale = _playerPreferences.SubtitleScale;
            _context.SubtitlePosition = _playerPreferences.SubtitlePosition;
            _context.SubtitlesEnabled = _playerPreferences.SubtitlesEnabled;
            _context.Deinterlace = _playerPreferences.Deinterlace;

            _selectedAspectMode = _playerPreferences.AspectMode;
            SetVolumeSliderValue(_playerPreferences.Volume);
            _lastNonZeroVolume = _playerPreferences.Volume > 0 ? _playerPreferences.Volume : _lastNonZeroVolume;
            SetMutedUi(_playerPreferences.IsMuted || _playerPreferences.Volume <= 0);
            if (!IsPictureInPictureMode())
            {
                _windowManager?.SetAlwaysOnTop(_playerPreferences.AlwaysOnTop);
            }
        }

        private async Task SavePlayerPreferencesAsync()
        {
            if (_context == null || _playerPreferencesService == null)
            {
                return;
            }

            var snapshot = new PlayerPreferencesSnapshot
            {
                Volume = VolumeSlider.Value,
                IsMuted = _isMuted,
                AspectMode = _selectedAspectMode,
                PlaybackSpeed = _player?.PlaybackSpeed > 0 ? _player.PlaybackSpeed : _playerPreferences.PlaybackSpeed,
                AudioDelaySeconds = _player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds,
                SubtitleDelaySeconds = _player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds,
                SubtitleScale = _player?.SubtitleScale > 0 ? _player.SubtitleScale : _playerPreferences.SubtitleScale,
                SubtitlePosition = _player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition,
                SubtitlesEnabled = !string.IsNullOrWhiteSpace(GetSelectedTrackId(_player?.GetSubtitleTracks() ?? Array.Empty<MpvTrackInfo>())),
                Deinterlace = _player?.IsDeinterlaceEnabled ?? _playerPreferences.Deinterlace,
                AlwaysOnTop = _windowManager?.IsAlwaysOnTop == true
            };

            _playerPreferences = snapshot;
            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _playerPreferencesService.SaveAsync(db, _context.ProfileId, snapshot);
        }

        private void UpdateEnhancedControlState()
        {
            var isLive = IsLivePlayback();
            var canSeek = IsTimelineSeekAllowed() && _stateMachine.State != PlaybackSessionState.Opening;
            var hasEpisodeNavigation = _allEpisodeSwitchItems.Count > 1;

            PreviousChannelButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            LastChannelButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            NextChannelButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            GuidePanelButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            ChannelPanelButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            EpisodePanelButton.Visibility = hasEpisodeNavigation ? Visibility.Visible : Visibility.Collapsed;

            Back10Button.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
            Back30Button.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
            Forward10Button.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
            Forward30Button.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
            RestartButton.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
            SpeedButton.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;

            PreviousChannelButton.IsEnabled = isLive && _allChannelSwitchItems.Count > 1;
            NextChannelButton.IsEnabled = isLive && _allChannelSwitchItems.Count > 1;
            LastChannelButton.IsEnabled = isLive && _lastChannelCandidateId > 0;
            EpisodePanelButton.IsEnabled = hasEpisodeNavigation;
            FavoriteButton.Visibility = _favoriteType.HasValue ? Visibility.Visible : Visibility.Collapsed;
            UpdatePanelVisibility();
        }

        private void UpdateResolvedContextText()
        {
            if (IsLivePlayback())
            {
                ContextText.Text = string.IsNullOrWhiteSpace(_resolvedSourceName)
                    ? _resolvedGuideSummary
                    : string.IsNullOrWhiteSpace(_resolvedGuideSummary)
                        ? _resolvedSourceName
                        : $"{_resolvedSourceName}  •  {_resolvedGuideSummary}";
            }

            ContextText.Visibility = string.IsNullOrWhiteSpace(ContextText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyGuideSummary(ChannelGuideSummary summary)
        {
            GuideCurrentTitleText.Text = summary.CurrentProgram?.Title ?? "No current programme";
            GuideCurrentTimeText.Text = summary.CurrentProgram != null
                ? $"{summary.CurrentProgram.StartTimeUtc.ToLocalTime():HH:mm} - {summary.CurrentProgram.EndTimeUtc.ToLocalTime():HH:mm}"
                : string.Empty;
            GuideNextTitleText.Text = summary.NextProgram?.Title ?? "No upcoming programme";
            GuideNextTimeText.Text = summary.NextProgram != null
                ? $"{summary.NextProgram.StartTimeUtc.ToLocalTime():HH:mm}"
                : string.Empty;
            GuideConfidenceText.Text = summary.SourceStatusSummary;
            _resolvedGuideSummary = summary.SourceStatusSummary;
        }

        private void ClearGuidePanel()
        {
            GuideCurrentTitleText.Text = string.Empty;
            GuideCurrentTimeText.Text = string.Empty;
            GuideNextTitleText.Text = string.Empty;
            GuideNextTimeText.Text = string.Empty;
            GuideConfidenceText.Text = string.Empty;
            _resolvedGuideSummary = string.Empty;
        }

        private string BuildChannelMeta(ChannelGuideSummary? summary)
        {
            if (summary?.CurrentProgram != null && summary.NextProgram != null)
            {
                return $"Now: {summary.CurrentProgram.Title} • Next: {summary.NextProgram.Title}";
            }

            if (summary?.CurrentProgram != null)
            {
                return $"Now: {summary.CurrentProgram.Title}";
            }

            return summary?.SourceStatusSummary ?? "Guide not available.";
        }
    }
}
