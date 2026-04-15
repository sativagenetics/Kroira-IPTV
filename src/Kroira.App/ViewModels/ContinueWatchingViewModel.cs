using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Kroira.App.ViewModels
{
    public partial class ProgressItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        
        public double ProgressPercent { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public long SavedPositionMs { get; set; }
    }

    public partial class ContinueWatchingViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<ProgressItemViewModel> ProgressItems { get; } = new();

        public ContinueWatchingViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadProgressAsync()
        {
            ProgressItems.Clear();
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var recs = await db.PlaybackProgresses
                .Where(p => !p.IsCompleted)
                .OrderByDescending(p => p.LastWatched)
                .ToListAsync();

            if (recs.Count == 0) return;

            var channelIds = recs.Where(r => r.ContentType == PlaybackContentType.Channel).Select(r => r.ContentId).ToList();
            var channels = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

            foreach (var r in recs)
            {
                if (r.ContentType == PlaybackContentType.Channel && channels.TryGetValue(r.ContentId, out var ch))
                {
                    double pct = r.PositionMs > 0 ? -1 : 0; 
                    string text = r.PositionMs > 0 ? TimeSpan.FromMilliseconds(r.PositionMs).ToString(@"hh\:mm\:ss") : "Live Channel";

                    ProgressItems.Add(new ProgressItemViewModel
                    {
                        Id = r.Id,
                        ContentId = r.ContentId,
                        ContentType = r.ContentType,
                        Title = ch.Name,
                        LogoUrl = ch.LogoUrl ?? string.Empty,
                        StreamUrl = ch.StreamUrl,
                        ProgressPercent = pct,
                        ProgressText = text,
                        SavedPositionMs = r.PositionMs
                    });
                }
            }
        }

        [RelayCommand]
        public async Task RemoveProgressAsync(int progressId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await db.PlaybackProgresses.FindAsync(progressId);
            if (r != null)
            {
                db.PlaybackProgresses.Remove(r);
                await db.SaveChangesAsync();
                var vm = ProgressItems.FirstOrDefault(x => x.Id == progressId);
                if (vm != null) ProgressItems.Remove(vm);
            }
        }
    }
}
