using System;
using System.Collections.Generic;
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
    public partial class SourceItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LastSyncText { get; set; } = "Never";
        public int ChannelCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }

        // EPG health (set at load time)
        public bool HasEpgUrl { get; set; }
        public bool CanSyncEpg { get; set; }
        public bool HasEpgData { get; set; }
        public string EpgLastSyncText { get; set; } = string.Empty;
        public int EpgMatchedChannels { get; set; }
        public int EpgProgramCount { get; set; }
        public string EpgSummaryText { get; set; } = string.Empty;
        public bool EpgSyncSuccess { get; set; }

        // Per-row EPG sync busy state
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EpgSyncButtonText))]
        [NotifyPropertyChangedFor(nameof(IsEpgSyncEnabled))]
        private bool _isEpgSyncing;

        public string EpgSyncButtonText => IsEpgSyncing ? "Syncing…" : "EPG";
        public bool IsEpgSyncEnabled => CanSyncEpg && !IsEpgSyncing;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private string _healthLabel = "Saved";

        public Microsoft.UI.Xaml.Visibility ParseVisibility => Type == "M3U" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SyncEpgVisibility => CanSyncEpg ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SyncXtreamVisibility => Type == "Xtream" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility BrowseVisibility => (Type == "M3U" || Type == "Xtream") ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility HealthyVisibility => HealthLabel is "Healthy" or "Ready" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility AttentionVisibility => HealthLabel == "Attention" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility WorkingVisibility => HealthLabel == "Working" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility IdleVisibility => HealthLabel is "Saved" or "Not synced" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility EpgHealthVisibility => HasEpgData ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility EpgConfiguredVisibility => HasEpgUrl && !HasEpgData ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        partial void OnHealthLabelChanged(string value)
        {
            OnPropertyChanged(nameof(HealthyVisibility));
            OnPropertyChanged(nameof(AttentionVisibility));
            OnPropertyChanged(nameof(WorkingVisibility));
            OnPropertyChanged(nameof(IdleVisibility));
        }
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

        [RelayCommand]
        public async Task LoadSourcesAsync()
        {
            Sources.Clear();
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var profiles = await db.SourceProfiles
                .GroupJoin(db.SourceSyncStates, p => p.Id, s => s.SourceProfileId, (p, s) => new { Profile = p, SyncStates = s })
                .SelectMany(x => x.SyncStates.DefaultIfEmpty(), (x, sync) => new { x.Profile, Sync = sync })
                .OrderBy(x => x.Profile.Name)
                .ToListAsync();

            var sourceIds = profiles.Select(x => x.Profile.Id).ToList();

            // EPG health data — fully optional; any DB/schema error just means no EPG decoration
            var epgLogs = new Dictionary<int, Kroira.App.Models.EpgSyncLog>();
            var credEpgUrls = new Dictionary<int, string>();
            try
            {
                epgLogs = await db.EpgSyncLogs
                    .Where(e => sourceIds.Contains(e.SourceProfileId))
                    .ToDictionaryAsync(e => e.SourceProfileId);

                var rawCreds = await db.SourceCredentials
                    .Where(c => sourceIds.Contains(c.SourceProfileId))
                    .Select(c => new { c.SourceProfileId, c.EpgUrl })
                    .ToListAsync();

                foreach (var row in rawCreds)
                    credEpgUrls[row.SourceProfileId] = row.EpgUrl ?? string.Empty;
            }
            catch
            {
                // EpgSyncLogs table or EpgUrl column may be absent on an older runtime DB.
                // Sources page continues loading without EPG health decoration.
            }

            var channelCounts = await db.ChannelCategories
                .Where(c => sourceIds.Contains(c.SourceProfileId))
                .Join(db.Channels, c => c.Id, ch => ch.ChannelCategoryId, (c, ch) => c.SourceProfileId)
                .GroupBy(sourceProfileId => sourceProfileId)
                .Select(g => new { SourceProfileId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SourceProfileId, x => x.Count);

            var movieCounts = await db.Movies
                .Where(m => sourceIds.Contains(m.SourceProfileId))
                .GroupBy(m => m.SourceProfileId)
                .Select(g => new { SourceProfileId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SourceProfileId, x => x.Count);

            var seriesCounts = await db.Series
                .Where(s => sourceIds.Contains(s.SourceProfileId))
                .GroupBy(s => s.SourceProfileId)
                .Select(g => new { SourceProfileId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SourceProfileId, x => x.Count);

            foreach (var item in profiles)
            {
                var syncStr = item.Profile.LastSync?.ToString("g") ?? "Never";
                var hasSyncIssue = item.Sync != null && (item.Sync.HttpStatusCode >= 400 || !string.IsNullOrWhiteSpace(item.Sync.ErrorLog));
                var statusStr = item.Sync == null
                    ? item.Profile.LastSync == null ? "Saved source. No sync attempt recorded." : $"Last sync completed {syncStr}."
                    : hasSyncIssue
                        ? $"Last attempt returned code {item.Sync.HttpStatusCode}. {item.Sync.ErrorLog}".Trim()
                        : $"Last attempt returned code {item.Sync.HttpStatusCode}.";
                var healthLabel = item.Sync == null
                    ? item.Profile.LastSync == null ? "Not synced" : "Ready"
                    : hasSyncIssue ? "Attention" : "Healthy";

                var hasEpgUrl = credEpgUrls.TryGetValue(item.Profile.Id, out var epgUrl) && !string.IsNullOrWhiteSpace(epgUrl);
                var canSyncEpg = item.Profile.Type is SourceType.M3U or SourceType.Xtream;
                epgLogs.TryGetValue(item.Profile.Id, out var epgLog);

                var epgSummary = epgLog != null
                    ? epgLog.IsSuccess
                        ? $"{epgLog.ProgrammeCount:N0} programs · {epgLog.MatchedChannelCount} ch matched"
                        : $"Failed: {(epgLog.FailureReason.Length > 60 ? epgLog.FailureReason.Substring(0, 60) + "…" : epgLog.FailureReason)}"
                    : hasEpgUrl ? "Not synced yet" : string.Empty;
                var epgLastSync = epgLog != null ? epgLog.SyncedAtUtc.ToLocalTime().ToString("g") : string.Empty;

                Sources.Add(new SourceItemViewModel
                {
                    Id = item.Profile.Id,
                    Name = item.Profile.Name,
                    Type = item.Profile.Type.ToString(),
                    LastSyncText = syncStr,
                    ChannelCount = channelCounts.TryGetValue(item.Profile.Id, out var channelCount) ? channelCount : 0,
                    MovieCount = movieCounts.TryGetValue(item.Profile.Id, out var movieCount) ? movieCount : 0,
                    SeriesCount = seriesCounts.TryGetValue(item.Profile.Id, out var seriesCount) ? seriesCount : 0,
                    HealthLabel = healthLabel,
                    Status = statusStr,
                    HasEpgUrl = hasEpgUrl,
                    CanSyncEpg = canSyncEpg,
                    HasEpgData = epgLog != null,
                    EpgLastSyncText = epgLastSync,
                    EpgMatchedChannels = epgLog?.MatchedChannelCount ?? 0,
                    EpgProgramCount = epgLog?.ProgrammeCount ?? 0,
                    EpgSummaryText = epgSummary,
                    EpgSyncSuccess = epgLog?.IsSuccess ?? false
                });
            }

            SourceCount = Sources.Count;
            M3uSourceCount = Sources.Count(s => s.Type == "M3U");
            XtreamSourceCount = Sources.Count(s => s.Type == "Xtream");
            IsEmpty = Sources.Count == 0;
        }

        [RelayCommand]
        public async Task ParseSourceAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
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
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Attention";
                    item.Status = $"Parse Failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task SyncEpgAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
            if (item == null) return;

            item.IsEpgSyncing = true;
            item.HealthLabel = "Working";
            item.Status = "Syncing EPG…";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, id);

                // Refresh the full row — new item will have IsEpgSyncing = false and updated EPG health
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                item.IsEpgSyncing = false;
                item.HealthLabel = "Attention";
                item.Status = $"EPG failed: {(ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "…" : ex.Message)}";
            }
        }

        [RelayCommand]
        public async Task SyncXtreamAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
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
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Attention";
                    item.Status = $"Xtream Sync Failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task SyncXtreamVodAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
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
                    item.HealthLabel = "Attention";
                    item.Status = $"Xtream VOD Sync Failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task DeleteSourceAsync(int id)
        {
            var uiItem = Sources.FirstOrDefault(s => s.Id == id);
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
                        uiItem.HealthLabel = "Attention";
                        uiItem.Status = "Source not found.";
                    }
                    return;
                }

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    // 1. Delete EPG programs linked to channels in this source's categories
                    var catIds = await db.ChannelCategories
                        .Where(c => c.SourceProfileId == id)
                        .Select(c => c.Id)
                        .ToListAsync();

                    if (catIds.Count > 0)
                    {
                        var channelIds = await db.Channels
                            .Where(ch => catIds.Contains(ch.ChannelCategoryId))
                            .Select(ch => ch.Id)
                            .ToListAsync();

                        if (channelIds.Count > 0)
                        {
                            // EPG programs reference ChannelId
                            var epgs = await db.EpgPrograms.Where(e => channelIds.Contains(e.ChannelId)).ToListAsync();
                            if (epgs.Count > 0) db.EpgPrograms.RemoveRange(epgs);

                            // Favorites referencing these channels
                            var favs = await db.Favorites
                                .Where(f => f.ContentType == Models.FavoriteType.Channel && channelIds.Contains(f.ContentId))
                                .ToListAsync();
                            if (favs.Count > 0) db.Favorites.RemoveRange(favs);

                            // Channels themselves
                            var channels = await db.Channels.Where(ch => channelIds.Contains(ch.Id)).ToListAsync();
                            db.Channels.RemoveRange(channels);
                        }

                        // Channel categories
                        var cats = await db.ChannelCategories.Where(c => catIds.Contains(c.Id)).ToListAsync();
                        db.ChannelCategories.RemoveRange(cats);
                    }

                    // 2. Delete Xtream VOD: Episodes → Seasons → Series, then Movies
                    var seriesIds = await db.Series.Where(s => s.SourceProfileId == id).Select(s => s.Id).ToListAsync();
                    if (seriesIds.Count > 0)
                    {
                        var seasonIds = await db.Seasons.Where(sn => seriesIds.Contains(sn.SeriesId)).Select(sn => sn.Id).ToListAsync();
                        if (seasonIds.Count > 0)
                        {
                            var episodes = await db.Episodes.Where(ep => seasonIds.Contains(ep.SeasonId)).ToListAsync();
                            if (episodes.Count > 0) db.Episodes.RemoveRange(episodes);

                            var seasons = await db.Seasons.Where(sn => seasonIds.Contains(sn.Id)).ToListAsync();
                            db.Seasons.RemoveRange(seasons);
                        }

                        var series = await db.Series.Where(s => seriesIds.Contains(s.Id)).ToListAsync();
                        db.Series.RemoveRange(series);
                    }

                    var movies = await db.Movies.Where(m => m.SourceProfileId == id).ToListAsync();
                    if (movies.Count > 0) db.Movies.RemoveRange(movies);

                    // 3. Credentials, sync state, and EPG log
                    var creds = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == id);
                    if (creds != null) db.SourceCredentials.Remove(creds);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == id);
                    if (syncState != null) db.SourceSyncStates.Remove(syncState);

                    var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(e => e.SourceProfileId == id);
                    if (epgLog != null) db.EpgSyncLogs.Remove(epgLog);

                    // 4. The profile itself
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
                        uiItem.HealthLabel = "Attention";
                        uiItem.Status = $"Delete failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (uiItem != null)
                {
                    uiItem.HealthLabel = "Attention";
                    uiItem.Status = $"Delete error: {ex.Message}";
                }
            }
        }
    }
}
