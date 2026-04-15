using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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
        public Microsoft.UI.Xaml.Visibility BrowseVisibility => Type == "M3U" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SyncEpgVisibility => Type == "M3U" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public partial class SourceListViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<SourceItemViewModel> Sources { get; } = new();

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
            if (item != null) item.Status = "Syncing EPG...";

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
                if (item != null) item.Status = $"EPG Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DeleteSourceAsync(int id)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var profile = await db.SourceProfiles.FindAsync(id);
            if (profile != null)
            {
                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var creds = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == id);
                    if (creds != null) db.SourceCredentials.Remove(creds);

                    var sync = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == id);
                    if (sync != null) db.SourceSyncStates.Remove(sync);

                    var cats = await db.ChannelCategories.Where(c => c.SourceProfileId == id).ToListAsync();
                    if (cats.Count > 0)
                    {
                        var catIds = cats.Select(c => c.Id).ToList();
                        var channels = await db.Channels.Where(ch => catIds.Contains(ch.ChannelCategoryId)).ToListAsync();
                        db.Channels.RemoveRange(channels);
                        db.ChannelCategories.RemoveRange(cats);
                    }

                    db.SourceProfiles.Remove(profile);
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await LoadSourcesAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                }
            }
        }
    }
}
