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

namespace Kroira.App.ViewModels
{
    public partial class SourceItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LastSyncText { get; set; } = "Never";
        public int ChannelCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public string ImportResultText { get; set; } = string.Empty;
        public string EpgCoverageText { get; set; } = string.Empty;
        public string ParseWarningsText { get; set; } = string.Empty;
        public string NetworkFailureText { get; set; } = string.Empty;
        public string LastSuccessfulSyncText { get; set; } = string.Empty;
        public string GuideStatusText { get; set; } = string.Empty;
        public string GuideStatusSummaryText { get; set; } = string.Empty;
        public string GuideModeText { get; set; } = string.Empty;
        public string GuideUrlText { get; set; } = string.Empty;
        public string GuideMatchText { get; set; } = string.Empty;
        public int ImportWarningCount { get; set; }
        public int GuideWarningCount { get; set; }

        public bool HasEpgUrl { get; set; }
        public bool CanSyncEpg { get; set; }
        public bool HasEpgData { get; set; }
        public string EpgLastSyncText { get; set; } = string.Empty;
        public int EpgMatchedChannels { get; set; }
        public int EpgProgramCount { get; set; }
        public string EpgSummaryText { get; set; } = string.Empty;
        public bool EpgSyncSuccess { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EpgSyncButtonText))]
        [NotifyPropertyChangedFor(nameof(IsEpgSyncEnabled))]
        private bool _isEpgSyncing;

        public string EpgSyncButtonText => IsEpgSyncing ? "Syncing..." : "EPG";
        public bool IsEpgSyncEnabled => CanSyncEpg && !IsEpgSyncing;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private string _healthLabel = "Saved";

        public Microsoft.UI.Xaml.Visibility ParseVisibility => Type == "M3U"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility SyncEpgVisibility => CanSyncEpg
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility SyncXtreamVisibility => Type == "Xtream"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility BrowseVisibility => (Type == "M3U" || Type == "Xtream")
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility HealthyVisibility => HealthLabel is "Healthy" or "Ready"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility DegradedVisibility => HealthLabel == "Degraded"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility AttentionVisibility => HealthLabel is "Attention" or "Failing"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility WorkingVisibility => HealthLabel == "Working"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IdleVisibility => HealthLabel is "Saved" or "Not synced"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility EpgHealthVisibility => CanSyncEpg
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility EpgConfiguredVisibility => HasEpgUrl && !HasEpgData
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility GuideUrlVisibility => string.IsNullOrWhiteSpace(GuideUrlText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility GuideMatchVisibility => string.IsNullOrWhiteSpace(GuideMatchText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility GuideStatusSummaryVisibility => string.IsNullOrWhiteSpace(GuideStatusSummaryText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ParseWarningsVisibility => string.IsNullOrWhiteSpace(ParseWarningsText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility NetworkFailureVisibility => string.IsNullOrWhiteSpace(NetworkFailureText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        partial void OnHealthLabelChanged(string value)
        {
            OnPropertyChanged(nameof(HealthyVisibility));
            OnPropertyChanged(nameof(DegradedVisibility));
            OnPropertyChanged(nameof(AttentionVisibility));
            OnPropertyChanged(nameof(WorkingVisibility));
            OnPropertyChanged(nameof(IdleVisibility));
        }
    }

    public sealed class SourceGuideSettingsDraft
    {
        public int SourceId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public EpgActiveMode ActiveMode { get; set; } = EpgActiveMode.Detected;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string DetectedEpgUrl { get; set; } = string.Empty;
    }

    public partial class SourceListViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<SourceItemViewModel> Sources { get; } = new();

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private int _sourceCount;

        [ObservableProperty]
        private int _m3uSourceCount;

        [ObservableProperty]
        private int _xtreamSourceCount;

        public SourceListViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<SourceGuideSettingsDraft?> GetGuideSettingsAsync(int sourceId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => profile.Id == sourceId)
                .Select(profile => new
                {
                    profile.Id,
                    profile.Name,
                    profile.Type
                })
                .FirstOrDefaultAsync();
            if (source == null)
            {
                return null;
            }

            var credential = await db.SourceCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceId);
            if (credential == null)
            {
                return null;
            }

            return new SourceGuideSettingsDraft
            {
                SourceId = source.Id,
                SourceName = source.Name,
                SourceType = source.Type,
                ActiveMode = credential.EpgMode,
                ManualEpgUrl = credential.ManualEpgUrl,
                DetectedEpgUrl = credential.DetectedEpgUrl
            };
        }

        public async Task SaveGuideSettingsAsync(SourceGuideSettingsDraft draft, bool syncNow)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var credential = await db.SourceCredentials.FirstOrDefaultAsync(item => item.SourceProfileId == draft.SourceId);
            if (credential == null)
            {
                throw new Exception("Source credentials were not found.");
            }

            if (draft.ActiveMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(draft.ManualEpgUrl))
            {
                throw new Exception("Manual XMLTV mode requires a manual XMLTV URL.");
            }

            var previousMode = credential.EpgMode;
            var previousManualUrl = credential.ManualEpgUrl ?? string.Empty;
            credential.EpgMode = draft.ActiveMode;
            credential.ManualEpgUrl = draft.ManualEpgUrl?.Trim() ?? string.Empty;
            await db.SaveChangesAsync();

            var settingsChanged = previousMode != draft.ActiveMode ||
                                  !string.Equals(previousManualUrl, credential.ManualEpgUrl, StringComparison.OrdinalIgnoreCase);

            if (syncNow || draft.ActiveMode == EpgActiveMode.None)
            {
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, draft.SourceId);
            }
            else if (settingsChanged)
            {
                var channelIds = await db.ChannelCategories
                    .Where(category => category.SourceProfileId == draft.SourceId)
                    .Join(
                        db.Channels,
                        category => category.Id,
                        channel => channel.ChannelCategoryId,
                        (category, channel) => channel.Id)
                    .ToListAsync();

                if (channelIds.Count > 0)
                {
                    var existingPrograms = await db.EpgPrograms
                        .Where(program => channelIds.Contains(program.ChannelId))
                        .ToListAsync();
                    if (existingPrograms.Count > 0)
                    {
                        db.EpgPrograms.RemoveRange(existingPrograms);
                    }
                }

                var log = await db.EpgSyncLogs.FirstOrDefaultAsync(item => item.SourceProfileId == draft.SourceId);
                if (log == null)
                {
                    log = new EpgSyncLog { SourceProfileId = draft.SourceId };
                    db.EpgSyncLogs.Add(log);
                }

                log.SyncedAtUtc = DateTime.UtcNow;
                log.LastSuccessAtUtc = null;
                log.IsSuccess = false;
                log.Status = EpgStatus.Unknown;
                log.ResultCode = EpgSyncResultCode.None;
                log.FailureStage = EpgFailureStage.None;
                log.ActiveMode = draft.ActiveMode;
                log.ActiveXmltvUrl = string.Empty;
                log.MatchedChannelCount = 0;
                log.UnmatchedChannelCount = channelIds.Count;
                log.CurrentCoverageCount = 0;
                log.NextCoverageCount = 0;
                log.TotalLiveChannelCount = channelIds.Count;
                log.ProgrammeCount = 0;
                log.MatchBreakdown = string.Empty;
                log.FailureReason = "Guide settings updated. Sync pending.";

                await db.SaveChangesAsync();
            }

            await LoadSourcesAsync();
        }

        private async Task<string?> TryRefreshGuideAsync(int sourceId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, sourceId);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        [RelayCommand]
        public async Task LoadSourcesAsync()
        {
            Sources.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var diagnosticsService = scope.ServiceProvider.GetRequiredService<ISourceDiagnosticsService>();

            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(profile => profile.Name)
                .ToListAsync();

            var sourceIds = profiles.Select(profile => profile.Id).ToList();
            var diagnostics = await diagnosticsService.GetSnapshotsAsync(db, sourceIds);

            foreach (var profile in profiles)
            {
                diagnostics.TryGetValue(profile.Id, out var snapshot);
                snapshot ??= new SourceDiagnosticsSnapshot
                {
                    SourceProfileId = profile.Id,
                    SourceType = profile.Type,
                    HealthLabel = profile.LastSync.HasValue ? "Healthy" : "Not synced",
                    StatusSummary = profile.LastSync.HasValue
                        ? $"Last import completed {profile.LastSync.Value.ToLocalTime():g}."
                        : "Saved source. No successful import recorded yet.",
                    ImportResultText = profile.LastSync.HasValue
                        ? $"Imported at {profile.LastSync.Value.ToLocalTime():g}"
                        : "No successful import recorded.",
                    EpgCoverageText = "Guide not synced.",
                    EpgStatusText = "Guide not synced",
                    EpgStatusSummary = "Guide has not synced yet.",
                    LastSuccessfulSyncText = $"Import {(profile.LastSync.HasValue ? profile.LastSync.Value.ToLocalTime().ToString("g") : "Never")} - Guide Never",
                    LastImportSuccessText = profile.LastSync?.ToLocalTime().ToString("g") ?? "Never",
                    LastEpgSuccessText = "Never",
                    ActiveEpgModeText = "Detected from provider"
                };

                Sources.Add(new SourceItemViewModel
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.Type.ToString(),
                    LastSyncText = snapshot.LastImportSuccessText,
                    ChannelCount = snapshot.LiveChannelCount,
                    MovieCount = snapshot.MovieCount,
                    SeriesCount = snapshot.SeriesCount,
                    HealthLabel = snapshot.HealthLabel,
                    Status = snapshot.StatusSummary,
                    HasEpgUrl = snapshot.HasEpgUrl,
                    CanSyncEpg = profile.Type is SourceType.M3U or SourceType.Xtream,
                    HasEpgData = snapshot.HasPersistedGuideData,
                    EpgLastSyncText = snapshot.LastEpgSuccessText,
                    EpgMatchedChannels = snapshot.MatchedLiveChannelCount,
                    EpgProgramCount = snapshot.EpgProgramCount,
                    EpgSummaryText = snapshot.EpgStatusText,
                    EpgSyncSuccess = snapshot.EpgSyncSuccess,
                    ImportResultText = snapshot.ImportResultText,
                    EpgCoverageText = snapshot.EpgCoverageText,
                    ParseWarningsText = snapshot.WarningSummaryText,
                    NetworkFailureText = snapshot.FailureSummaryText,
                    LastSuccessfulSyncText = snapshot.LastSuccessfulSyncText,
                    GuideStatusText = snapshot.EpgStatusText,
                    GuideStatusSummaryText = snapshot.EpgStatusSummary,
                    GuideModeText = snapshot.ActiveEpgModeText,
                    GuideUrlText = snapshot.EpgUrlSummaryText,
                    GuideMatchText = snapshot.MatchBreakdownText,
                    ImportWarningCount = snapshot.ImportWarningCount,
                    GuideWarningCount = snapshot.GuideWarningCount
                });
            }

            SourceCount = Sources.Count;
            M3uSourceCount = Sources.Count(source => source.Type == "M3U");
            XtreamSourceCount = Sources.Count(source => source.Type == "Xtream");
            IsEmpty = Sources.Count == 0;
        }

        [RelayCommand]
        public async Task ParseSourceAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Parsing...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IM3uParserService>();

                await parser.ParseAndImportM3uAsync(db, id);
                var guideError = await TryRefreshGuideAsync(id);
                await LoadSourcesAsync();
                if (!string.IsNullOrWhiteSpace(guideError))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = $"Import completed, but guide sync failed: {(guideError.Length > 120 ? guideError.Substring(0, 120) + "..." : guideError)}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Parse failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task SyncEpgAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item == null) return;

            item.IsEpgSyncing = true;
            item.HealthLabel = "Working";
            item.Status = "Syncing EPG...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();

                await parser.ParseAndImportEpgAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                item.IsEpgSyncing = false;
                item.HealthLabel = "Failing";
                item.Status = $"EPG failed: {(ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "..." : ex.Message)}";
            }
        }

        [RelayCommand]
        public async Task SyncXtreamAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Syncing Xtream...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();

                await parser.ParseAndImportXtreamAsync(db, id);
                await parser.ParseAndImportXtreamVodAsync(db, id);
                var guideError = await TryRefreshGuideAsync(id);
                await LoadSourcesAsync();
                if (!string.IsNullOrWhiteSpace(guideError))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = $"Xtream sync completed, but guide sync failed: {(guideError.Length > 120 ? guideError.Substring(0, 120) + "..." : guideError)}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Xtream sync failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task SyncXtreamVodAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Syncing Xtream VOD...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();

                await parser.ParseAndImportXtreamVodAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Xtream VOD sync failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task DeleteSourceAsync(int id)
        {
            var uiItem = Sources.FirstOrDefault(source => source.Id == id);
            if (uiItem != null)
            {
                uiItem.HealthLabel = "Working";
                uiItem.Status = "Deleting...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var profile = await db.SourceProfiles.FindAsync(id);
                if (profile == null)
                {
                    if (uiItem != null)
                    {
                        uiItem.HealthLabel = "Failing";
                        uiItem.Status = "Source not found.";
                    }
                    return;
                }

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var catIds = await db.ChannelCategories
                        .Where(category => category.SourceProfileId == id)
                        .Select(category => category.Id)
                        .ToListAsync();

                    if (catIds.Count > 0)
                    {
                        var channelIds = await db.Channels
                            .Where(channel => catIds.Contains(channel.ChannelCategoryId))
                            .Select(channel => channel.Id)
                            .ToListAsync();

                        if (channelIds.Count > 0)
                        {
                            var epgs = await db.EpgPrograms.Where(program => channelIds.Contains(program.ChannelId)).ToListAsync();
                            if (epgs.Count > 0) db.EpgPrograms.RemoveRange(epgs);

                            var favs = await db.Favorites
                                .Where(favorite => favorite.ContentType == FavoriteType.Channel && channelIds.Contains(favorite.ContentId))
                                .ToListAsync();
                            if (favs.Count > 0) db.Favorites.RemoveRange(favs);

                            var progress = await db.PlaybackProgresses
                                .Where(item => item.ContentType == PlaybackContentType.Channel && channelIds.Contains(item.ContentId))
                                .ToListAsync();
                            if (progress.Count > 0) db.PlaybackProgresses.RemoveRange(progress);

                            var channels = await db.Channels.Where(channel => channelIds.Contains(channel.Id)).ToListAsync();
                            db.Channels.RemoveRange(channels);
                        }

                        var cats = await db.ChannelCategories.Where(category => catIds.Contains(category.Id)).ToListAsync();
                        db.ChannelCategories.RemoveRange(cats);
                    }

                    var seriesIds = await db.Series.Where(series => series.SourceProfileId == id).Select(series => series.Id).ToListAsync();
                    if (seriesIds.Count > 0)
                    {
                        var seasonIds = await db.Seasons.Where(season => seriesIds.Contains(season.SeriesId)).Select(season => season.Id).ToListAsync();
                        if (seasonIds.Count > 0)
                        {
                            var episodeIds = await db.Episodes.Where(episode => seasonIds.Contains(episode.SeasonId)).Select(episode => episode.Id).ToListAsync();
                            var episodes = await db.Episodes.Where(episode => seasonIds.Contains(episode.SeasonId)).ToListAsync();
                            if (episodes.Count > 0) db.Episodes.RemoveRange(episodes);

                            if (episodeIds.Count > 0)
                            {
                                var episodeProgress = await db.PlaybackProgresses
                                    .Where(item => item.ContentType == PlaybackContentType.Episode && episodeIds.Contains(item.ContentId))
                                    .ToListAsync();
                                if (episodeProgress.Count > 0) db.PlaybackProgresses.RemoveRange(episodeProgress);
                            }

                            var seasons = await db.Seasons.Where(season => seasonIds.Contains(season.Id)).ToListAsync();
                            db.Seasons.RemoveRange(seasons);
                        }

                        var seriesFavorites = await db.Favorites
                            .Where(favorite => favorite.ContentType == FavoriteType.Series && seriesIds.Contains(favorite.ContentId))
                            .ToListAsync();
                        if (seriesFavorites.Count > 0) db.Favorites.RemoveRange(seriesFavorites);

                        var series = await db.Series.Where(series => seriesIds.Contains(series.Id)).ToListAsync();
                        db.Series.RemoveRange(series);
                    }

                    var movieIds = await db.Movies.Where(movie => movie.SourceProfileId == id).Select(movie => movie.Id).ToListAsync();
                    if (movieIds.Count > 0)
                    {
                        var movieFavorites = await db.Favorites
                            .Where(favorite => favorite.ContentType == FavoriteType.Movie && movieIds.Contains(favorite.ContentId))
                            .ToListAsync();
                        if (movieFavorites.Count > 0) db.Favorites.RemoveRange(movieFavorites);

                        var movieProgress = await db.PlaybackProgresses
                            .Where(item => item.ContentType == PlaybackContentType.Movie && movieIds.Contains(item.ContentId))
                            .ToListAsync();
                        if (movieProgress.Count > 0) db.PlaybackProgresses.RemoveRange(movieProgress);
                    }

                    var movies = await db.Movies.Where(movie => movie.SourceProfileId == id).ToListAsync();
                    if (movies.Count > 0) db.Movies.RemoveRange(movies);

                    var creds = await db.SourceCredentials.FirstOrDefaultAsync(credential => credential.SourceProfileId == id);
                    if (creds != null) db.SourceCredentials.Remove(creds);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(state => state.SourceProfileId == id);
                    if (syncState != null) db.SourceSyncStates.Remove(syncState);

                    var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(log => log.SourceProfileId == id);
                    if (epgLog != null) db.EpgSyncLogs.Remove(epgLog);

                    db.SourceProfiles.Remove(profile);

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await LoadSourcesAsync();
                }
                catch (Exception ex)
                {
                    try { await transaction.RollbackAsync(); } catch { }
                    if (uiItem != null)
                    {
                        uiItem.HealthLabel = "Failing";
                        uiItem.Status = $"Delete failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (uiItem != null)
                {
                    uiItem.HealthLabel = "Failing";
                    uiItem.Status = $"Delete error: {ex.Message}";
                }
            }
        }
    }
}
