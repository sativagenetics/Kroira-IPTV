using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.ViewModels
{
    public partial class SourceItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        [ObservableProperty]
        private string _status = string.Empty;

        public Microsoft.UI.Xaml.Visibility ParseVisibility => Type == "M3U" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SyncEpgVisibility => Type == "M3U" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SyncXtreamVisibility => Type == "Xtream" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility BrowseVisibility => (Type == "M3U" || Type == "Xtream") ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public partial class SourceListViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<SourceItemViewModel> Sources { get; } = new();

        [ObservableProperty]
        private bool _isEmpty;

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
                .ToListAsync();

            foreach (var item in profiles)
            {
                var syncStr = item.Profile.LastSync?.ToString("g") ?? "Never";
                var statusStr = item.Sync == null
                    ? $"Saved, Last Sync: {syncStr}"
                    : $"Attempt: {item.Sync.LastAttempt:g} | Code: {item.Sync.HttpStatusCode}\n{item.Sync.ErrorLog}";

                Sources.Add(new SourceItemViewModel
                {
                    Id = item.Profile.Id,
                    Name = item.Profile.Name,
                    Type = item.Profile.Type.ToString(),
                    Status = statusStr
                });
            }

            IsEmpty = Sources.Count == 0;
        }

        [RelayCommand]
        public async Task ParseSourceAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
            if (item != null) item.Status = "Parsing...";

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
                if (item != null) item.Status = $"Parse Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SyncEpgAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
            if (item != null) item.Status = "Checking EPG configuration...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == id);
                if (cred == null || string.IsNullOrWhiteSpace(cred.EpgUrl))
                {
                    if (item != null) item.Status = "No EPG URL configured for this source. Edit the source to add one.";
                    return;
                }

                if (item != null) item.Status = "Syncing EPG...";

                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null) item.Status = $"EPG Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SyncXtreamAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
            if (item != null) item.Status = "Syncing Xtream...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();

                await parser.ParseAndImportXtreamAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null) item.Status = $"Xtream Sync Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SyncXtreamVodAsync(int id)
        {
            var item = Sources.FirstOrDefault(s => s.Id == id);
            if (item != null) item.Status = "Syncing Xtream VOD...";

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
                if (item != null) item.Status = $"Xtream VOD Sync Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DeleteSourceAsync(int id)
        {
            var uiItem = Sources.FirstOrDefault(s => s.Id == id);
            if (uiItem != null) uiItem.Status = "Deleting...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var profile = await db.SourceProfiles.FindAsync(id);
                if (profile == null)
                {
                    if (uiItem != null) uiItem.Status = "Source not found.";
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

                    // 3. Credentials and sync state (may cascade via FK, but explicit is safer)
                    var creds = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == id);
                    if (creds != null) db.SourceCredentials.Remove(creds);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == id);
                    if (syncState != null) db.SourceSyncStates.Remove(syncState);

                    // 4. The profile itself
                    db.SourceProfiles.Remove(profile);

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await LoadSourcesAsync();
                }
                catch (Exception ex)
                {
                    try { await transaction.RollbackAsync(); } catch { }
                    if (uiItem != null) uiItem.Status = $"Delete failed: {ex.Message}";
                }
            }
            catch (Exception ex)
            {
                if (uiItem != null) uiItem.Status = $"Delete error: {ex.Message}";
            }
        }
    }
}
