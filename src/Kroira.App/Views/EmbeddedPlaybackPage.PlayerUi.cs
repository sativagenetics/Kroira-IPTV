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
            public int PreferredSourceProfileId { get; init; }
            public string LogicalContentKey { get; init; } = string.Empty;
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

        private sealed class PlayerGuideProgramItem
        {
            public string Title { get; init; } = string.Empty;
            public string ProgramTitle { get; init; } = string.Empty;
            public string TimeText { get; init; } = string.Empty;
            public string StatusText { get; init; } = string.Empty;
            public bool IsCurrent { get; init; }
            public CatchupRequestKind RequestKind { get; init; }
            public DateTime StartTimeUtc { get; init; }
            public DateTime EndTimeUtc { get; init; }
            public string ActionLabel { get; init; } = string.Empty;
            public Visibility ActionVisibility { get; init; }
            public Visibility StatusVisibility { get; init; }
        }

        private readonly ObservableCollection<PlayerChannelSwitchItem> _channelSwitchItems = new();
        private readonly ObservableCollection<PlayerEpisodeSwitchItem> _episodeSwitchItems = new();
        private readonly ObservableCollection<PlayerGuideProgramItem> _guideProgramItems = new();
        private readonly List<PlayerChannelSwitchItem> _allChannelSwitchItems = new();
        private readonly List<PlayerEpisodeSwitchItem> _allEpisodeSwitchItems = new();
        private readonly List<(double Speed, ToggleMenuFlyoutItem Item)> _speedItems = new();
        private readonly List<(string Key, ToggleMenuFlyoutItem Item)> _toggleToolItems = new();
        private readonly List<(double Scale, string Label)> _subtitleScalePresets = new()
        {
            (0.85, "Player.SubtitleSize.Small"),
            (1.0, "Player.SubtitleSize.Medium"),
            (1.2, "Player.SubtitleSize.Large")
        };

        private IPlayerPreferencesService? _playerPreferencesService;
        private PlayerPreferencesSnapshot _playerPreferences = new();
        private DispatcherTimer? _zapBannerTimer;
        private DispatcherTimer? _sleepTimer;
        private DateTimeOffset? _sleepDeadline;
        private string _resolvedSourceName = string.Empty;
        private string _resolvedGuideSummary = string.Empty;
        private string _resolvedRoutingSummary = string.Empty;
        private int _resolvedSourceProfileId;
        private FavoriteType? _favoriteType;
        private int _favoriteContentId;
        private int _lastChannelCandidateId;
        private bool _guidePanelOpen;
        private bool _channelPanelOpen;
        private bool _episodePanelOpen;
        private bool _infoPanelOpen;
        private bool _toolsPanelOpen;

        private void InitializeEnhancedPlayerUi(IServiceProvider services)
        {
            _playerPreferencesService = services.GetRequiredService<IPlayerPreferencesService>();
            ChannelSwitchList.ItemsSource = _channelSwitchItems;
            EpisodeSwitchList.ItemsSource = _episodeSwitchItems;
            GuideProgramList.ItemsSource = _guideProgramItems;
            BuildSpeedFlyout();
            BuildToolsFlyout();
            UpdateFavoriteUi();
            UpdatePanelVisibility();
            UpdatePlaybackHint();
            ClearGuidePanel();
            RefreshInfoPanel();
        }

        private void ResetResolvedPlaybackUiState()
        {
            _resolvedSourceName = string.Empty;
            _resolvedGuideSummary = string.Empty;
            _resolvedRoutingSummary = string.Empty;
            _resolvedSourceProfileId = 0;
            _favoriteType = null;
            _favoriteContentId = 0;
            _isFavorite = false;
            _lastChannelCandidateId = 0;
            _allChannelSwitchItems.Clear();
            _channelSwitchItems.Clear();
            _allEpisodeSwitchItems.Clear();
            _episodeSwitchItems.Clear();
            EpisodePanelHeaderText.Text = string.Empty;
            EpisodePanelStatusText.Text = string.Empty;
            BottomLiveTitleText.Text = string.Empty;
            BottomLiveMetaText.Text = string.Empty;
            ClearGuidePanel();
            UpdateFavoriteUi();
            UpdateResolvedContextText();
            UpdateEnhancedControlState();
            UpdatePlaybackHint();
            RefreshInfoPanel();
        }

        private async Task LoadEnhancedPlayerStateAsync(CancellationToken cancellationToken)
        {
            if (_context == null || _playerPreferencesService == null)
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var switchGeneration = _switchGeneration;
            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var guideService = scope.ServiceProvider.GetRequiredService<ILiveGuideService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var providerStreamResolverService = scope.ServiceProvider.GetRequiredService<IProviderStreamResolverService>();
            var access = await profileService.GetAccessSnapshotAsync(db);

            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            _playerPreferences = await _playerPreferencesService.LoadAsync(db, _context.ProfileId);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            ApplyEnhancedPreferencesToContext();
            if (string.IsNullOrWhiteSpace(_context.CatalogStreamUrl) && !string.IsNullOrWhiteSpace(_context.StreamUrl))
            {
                _context.CatalogStreamUrl = _context.StreamUrl;
            }

            await logicalCatalogStateService.EnsureLaunchContextLogicalStateAsync(db, _context);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            await contentOperationalService.ResolvePlaybackContextAsync(db, _context);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            var providerResolution = await providerStreamResolverService.ResolvePlaybackContextAsync(
                db,
                _context,
                SourceNetworkPurpose.Playback,
                cancellationToken);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            if (!providerResolution.Success && string.IsNullOrWhiteSpace(_context.StreamUrl))
            {
                throw new InvalidOperationException(providerResolution.Message);
            }

            if (_context.ContentType == PlaybackContentType.Channel &&
                string.IsNullOrWhiteSpace(_context.LiveStreamUrl))
            {
                _context.LiveStreamUrl = _context.StreamUrl;
            }

            ResetResolvedPlaybackUiState();
            _resolvedRoutingSummary = _context.RoutingSummary;

            switch (_context.ContentType)
            {
                case PlaybackContentType.Channel:
                    await LoadLiveContentStateAsync(db, access, guideService, browsePreferencesService, logicalCatalogStateService, cancellationToken);
                    break;
                case PlaybackContentType.Movie:
                    await LoadMovieContentStateAsync(db, access, logicalCatalogStateService, cancellationToken);
                    break;
                case PlaybackContentType.Episode:
                    await LoadEpisodeContentStateAsync(db, access, profileService, logicalCatalogStateService, cancellationToken);
                    break;
            }

            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
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
            ILogicalCatalogStateService logicalCatalogStateService,
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
            _isFavorite = await logicalCatalogStateService.IsFavoritedAsync(db, _context.ProfileId, FavoriteType.Channel, currentRow.Channel.Id);

            TitleText.Text = currentRow.Channel.Name;
            TitleText.Visibility = Visibility.Collapsed;
            ContextText.Text = string.Empty;
            ContextText.Visibility = Visibility.Collapsed;
            BottomLiveTitleText.Text = currentRow.Channel.Name;
            BottomLiveMetaText.Text = string.Empty;
            BottomLiveMetaText.Visibility = Visibility.Collapsed;

            var browsePreferences = await browsePreferencesService.GetAsync(db, ProfileDomains.Live, _context.ProfileId);
            var logicalKeyByChannelId = visibleRows.ToDictionary(
                row => row.Channel.Id,
                row => logicalCatalogStateService.BuildChannelLogicalKey(row.Channel));
            var currentLogicalKey = logicalKeyByChannelId.TryGetValue(currentRow.Channel.Id, out var currentKey)
                ? currentKey
                : _context.LogicalContentKey;
            _lastChannelCandidateId = browsePreferences.RecentChannels
                .Where(reference => !string.IsNullOrWhiteSpace(reference.LogicalKey) &&
                                    !string.Equals(reference.LogicalKey, currentLogicalKey, StringComparison.OrdinalIgnoreCase))
                .Select(reference => visibleRows
                    .Where(row => logicalKeyByChannelId.TryGetValue(row.Channel.Id, out var rowKey) &&
                                  string.Equals(rowKey, reference.LogicalKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(row => row.SourceId == reference.PreferredSourceProfileId)
                    .Select(row => row.Channel.Id)
                    .FirstOrDefault())
                .FirstOrDefault(id => id > 0);
            if (_lastChannelCandidateId <= 0)
            {
                _lastChannelCandidateId = browsePreferences.RecentChannelIds.FirstOrDefault(id => id != currentRow.Channel.Id);
            }

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
                    PreferredSourceProfileId = row.SourceId,
                    LogicalContentKey = logicalKeyByChannelId.TryGetValue(row.Channel.Id, out var rowLogicalKey) ? rowLogicalKey : string.Empty,
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

        private async Task LoadMovieContentStateAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            ILogicalCatalogStateService logicalCatalogStateService,
            CancellationToken cancellationToken)
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
            _isFavorite = await logicalCatalogStateService.IsFavoritedAsync(db, _context.ProfileId, FavoriteType.Movie, movie.Id);
            _resolvedSourceName = movie.CategoryName;
            TitleText.Visibility = Visibility.Visible;
            TitleText.Text = movie.Title;
            ContextText.Text = movie.MetadataLine;
            ContextText.Visibility = string.IsNullOrWhiteSpace(ContextText.Text) ? Visibility.Collapsed : Visibility.Visible;
            ClearGuidePanel();
        }

        private async Task LoadEpisodeContentStateAsync(
            AppDbContext db,
            ProfileAccessSnapshot access,
            IProfileStateService profileService,
            ILogicalCatalogStateService logicalCatalogStateService,
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
            _isFavorite = await logicalCatalogStateService.IsFavoritedAsync(db, _context.ProfileId, FavoriteType.Series, series.Id);
            _resolvedSourceName = series.Title;
            TitleText.Visibility = Visibility.Visible;
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
            EpisodePanelHeaderText.Text = LocalizedStrings.Get("Player.Episodes.PanelHeader");
            EpisodePanelStatusText.Text = LocalizedStrings.Format("Player.Episodes.PanelStatus", _allEpisodeSwitchItems.Count);
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
            var isChannel = IsChannelPlayback();
            var canSeek = IsTimelineSeekAllowed() && _stateMachine.State != PlaybackSessionState.Opening;
            var hasEpisodeNavigation = _allEpisodeSwitchItems.Count > 1;
            var useLiveControlLayout = isLive && !canSeek;
            var useSeekControlLayout = !useLiveControlLayout;

            SeekTimelineContainer.Visibility = useSeekControlLayout ? Visibility.Visible : Visibility.Collapsed;
            LiveSummaryPanel.Visibility = useLiveControlLayout ? Visibility.Visible : Visibility.Collapsed;

            PreviousChannelButton.Visibility = useLiveControlLayout ? Visibility.Visible : Visibility.Collapsed;
            LastChannelButton.Visibility = Visibility.Collapsed;
            NextChannelButton.Visibility = useLiveControlLayout ? Visibility.Visible : Visibility.Collapsed;
            GuidePanelButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;
            ChannelPanelButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;
            EpisodePanelButton.Visibility = hasEpisodeNavigation ? Visibility.Visible : Visibility.Collapsed;

            Back10Button.Visibility = useSeekControlLayout && canSeek ? Visibility.Visible : Visibility.Collapsed;
            Back30Button.Visibility = useSeekControlLayout && canSeek ? Visibility.Visible : Visibility.Collapsed;
            Forward10Button.Visibility = useSeekControlLayout && canSeek ? Visibility.Visible : Visibility.Collapsed;
            Forward30Button.Visibility = useSeekControlLayout && canSeek ? Visibility.Visible : Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Collapsed;
            SpeedButton.Visibility = useSeekControlLayout && canSeek ? Visibility.Visible : Visibility.Collapsed;
            GoLiveButton.Visibility = IsCatchupPlayback() || (isLive && canSeek) ? Visibility.Visible : Visibility.Collapsed;
            BottomGuideButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;
            BottomChannelListButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;

            PreviousChannelButton.IsEnabled = useLiveControlLayout && _allChannelSwitchItems.Count > 1;
            NextChannelButton.IsEnabled = useLiveControlLayout && _allChannelSwitchItems.Count > 1;
            LastChannelButton.IsEnabled = isChannel && _lastChannelCandidateId > 0;
            EpisodePanelButton.IsEnabled = hasEpisodeNavigation;
            FavoriteButton.Visibility = _favoriteType.HasValue ? Visibility.Visible : Visibility.Collapsed;
            BuildToolsFlyout();
            UpdatePanelVisibility();
        }

        private void UpdateResolvedContextText()
        {
            if (IsChannelPlayback())
            {
                BottomLiveTitleText.Text = TitleText.Text;
                BottomLiveMetaText.Text = string.Empty;
                BottomLiveMetaText.Visibility = Visibility.Collapsed;
                ContextText.Text = string.Empty;
                ContextText.Visibility = Visibility.Collapsed;
                return;
            }

            ContextText.Visibility = string.IsNullOrWhiteSpace(ContextText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyGuideSummary(ChannelGuideSummary summary)
        {
            GuideCurrentTitleText.Text = summary.CurrentProgram?.Title ?? LocalizedStrings.Get("Player.Guide.NoCurrentProgramme");
            GuideCurrentTimeText.Text = summary.CurrentProgram != null
                ? $"{summary.CurrentProgram.StartTimeUtc.ToLocalTime():HH:mm} - {summary.CurrentProgram.EndTimeUtc.ToLocalTime():HH:mm}"
                : string.Empty;
            GuideNextTitleText.Text = summary.NextProgram?.Title ?? LocalizedStrings.Get("Player.Guide.NoUpcomingProgramme");
            GuideNextTimeText.Text = summary.NextProgram != null
                ? $"{summary.NextProgram.StartTimeUtc.ToLocalTime():HH:mm}"
                : string.Empty;
            GuideConfidenceText.Text = summary.SourceStatusSummary;
            GuideCatchupStatusText.Text = summary.CatchupStatusSummary;
            _resolvedGuideSummary = string.IsNullOrWhiteSpace(summary.CatchupStatusSummary)
                ? summary.SourceStatusSummary
                : $"{summary.SourceStatusSummary} - {summary.CatchupStatusSummary}";
            BottomLiveTitleText.Text = TitleText.Text;
            BottomLiveMetaText.Text = string.Empty;
            BottomLiveMetaText.Visibility = Visibility.Collapsed;
            _guideProgramItems.Clear();
            foreach (var program in summary.TimelinePrograms)
            {
                _guideProgramItems.Add(new PlayerGuideProgramItem
                {
                    Title = program.IsCurrent ? LocalizedStrings.Format("Player.Guide.NowProgram", program.Title) : program.Title,
                    ProgramTitle = program.Title,
                    TimeText = $"{program.StartTimeUtc.ToLocalTime():HH:mm} - {program.EndTimeUtc.ToLocalTime():HH:mm}",
                    StatusText = program.CatchupStatusText,
                    IsCurrent = program.IsCurrent,
                    RequestKind = program.CatchupRequestKind,
                    StartTimeUtc = program.StartTimeUtc,
                    EndTimeUtc = program.EndTimeUtc,
                    ActionLabel = program.CatchupActionLabel,
                    ActionVisibility = program.CatchupRequestKind == CatchupRequestKind.None
                        ? Visibility.Collapsed
                        : Visibility.Visible,
                    StatusVisibility = string.IsNullOrWhiteSpace(program.CatchupStatusText)
                        ? Visibility.Collapsed
                        : Visibility.Visible
                });
            }
        }

        private void ClearGuidePanel()
        {
            GuideCurrentTitleText.Text = string.Empty;
            GuideCurrentTimeText.Text = string.Empty;
            GuideNextTitleText.Text = string.Empty;
            GuideNextTimeText.Text = string.Empty;
            GuideConfidenceText.Text = string.Empty;
            GuideCatchupStatusText.Text = string.Empty;
            _guideProgramItems.Clear();
            _resolvedGuideSummary = string.Empty;
            BottomLiveTitleText.Text = TitleText.Text;
            BottomLiveMetaText.Text = string.Empty;
            BottomLiveMetaText.Visibility = Visibility.Collapsed;
        }

        private string BuildChannelMeta(ChannelGuideSummary? summary)
        {
            if (IsCatchupPlayback())
            {
                return BuildCatchupContextText();
            }

            if (summary?.CurrentProgram != null && summary.NextProgram != null)
            {
                return LocalizedStrings.Format("Player.Meta.NowNext", summary.CurrentProgram.Title, summary.NextProgram.Title);
            }

            if (summary?.CurrentProgram != null)
            {
                return LocalizedStrings.Format("Player.Meta.Now", summary.CurrentProgram.Title);
            }

            return summary?.SourceStatusSummary ?? LocalizedStrings.Get("Player.Guide.NotAvailable");
        }

        private string BuildCatchupContextText()
        {
            if (_context == null || !IsCatchupPlayback())
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_context.CatchupProgramTitle))
            {
                parts.Add(_context.CatchupProgramTitle);
            }

            if (_context.CatchupProgramStartTimeUtc.HasValue && _context.CatchupProgramEndTimeUtc.HasValue)
            {
                parts.Add($"{_context.CatchupProgramStartTimeUtc.Value.ToLocalTime():HH:mm} - {_context.CatchupProgramEndTimeUtc.Value.ToLocalTime():HH:mm}");
            }

            parts.Add(LocalizedStrings.Get("Player.Catchup.Playback"));

            if (!string.IsNullOrWhiteSpace(_context.CatchupStatusText))
            {
                parts.Add(_context.CatchupStatusText);
            }

            return string.Join(" - ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
}
