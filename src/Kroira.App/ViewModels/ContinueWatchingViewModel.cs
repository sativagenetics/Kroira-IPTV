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

            // Preload lookup data for all content types
            var channelIds = recs.Where(r => r.ContentType == PlaybackContentType.Channel).Select(r => r.ContentId).ToList();
            var channels = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

            var movieIds = recs.Where(r => r.ContentType == PlaybackContentType.Movie).Select(r => r.ContentId).ToList();
            var movies = await db.Movies.Where(m => movieIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

            var episodeIds = recs.Where(r => r.ContentType == PlaybackContentType.Episode).Select(r => r.ContentId).ToList();
            var episodes = await db.Episodes.Where(e => episodeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);

            foreach (var r in recs)
            {
                string title = null;
                string logo = string.Empty;
                string streamUrl = null;

                if (r.ContentType == PlaybackContentType.Channel && channels.TryGetValue(r.ContentId, out var ch))
                {
                    title = ch.Name;
                    logo = ch.LogoUrl ?? string.Empty;
                    streamUrl = ch.StreamUrl;
                }
                else if (r.ContentType == PlaybackContentType.Movie && movies.TryGetValue(r.ContentId, out var mv))
                {
                    title = mv.Title;
                    logo = mv.PosterUrl ?? string.Empty;
                    streamUrl = mv.StreamUrl;
                }
                else if (r.ContentType == PlaybackContentType.Episode && episodes.TryGetValue(r.ContentId, out var ep))
                {
                    title = ep.Title;
                    streamUrl = ep.StreamUrl;
                }

                if (title == null || string.IsNullOrWhiteSpace(streamUrl)) continue;

                string text = r.PositionMs > 0
                    ? TimeSpan.FromMilliseconds(r.PositionMs).ToString(@"hh\:mm\:ss")
                    : (r.ContentType == PlaybackContentType.Channel ? "Live Channel" : "Not started");

                ProgressItems.Add(new ProgressItemViewModel
                {
                    Id = r.Id,
                    ContentId = r.ContentId,
                    ContentType = r.ContentType,
                    Title = title,
                    LogoUrl = logo,
                    StreamUrl = streamUrl,
                    ProgressPercent = r.PositionMs > 0 ? -1 : 0,
                    ProgressText = text,
                    SavedPositionMs = r.PositionMs
                });
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
