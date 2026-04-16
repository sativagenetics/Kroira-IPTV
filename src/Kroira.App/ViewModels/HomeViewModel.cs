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
    public sealed class HomeSummaryItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = "0";
        public string Detail { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
    }

    public sealed class HomeActionItem
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    public sealed class HomeContinueItem
    {
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public long SavedPositionMs { get; set; }
    }

    public partial class HomeViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEntitlementService _entitlementService;

        public ObservableCollection<HomeSummaryItem> SummaryItems { get; } = new();
        public ObservableCollection<HomeActionItem> QuickActions { get; } = new();
        public ObservableCollection<HomeContinueItem> ContinueItems { get; } = new();

        [ObservableProperty]
        private string _licenseStatusMessage = string.Empty;

        [ObservableProperty]
        private string _libraryStatusMessage = "Loading library status...";

        [ObservableProperty]
        private string _sourceStatusMessage = "Checking sources...";

        [ObservableProperty]
        private string _lastSyncMessage = "No sync history yet";

        [ObservableProperty]
        private string _heroSubtitle = "Review your IPTV library, sources, and playback progress from one place.";

        [ObservableProperty]
        private Visibility _continueItemsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _continueEmptyVisibility = Visibility.Visible;

        [ObservableProperty]
        private Visibility _sourceIssueVisibility = Visibility.Collapsed;

        public HomeViewModel(IServiceProvider serviceProvider, IEntitlementService entitlementService)
        {
            _serviceProvider = serviceProvider;
            _entitlementService = entitlementService;

            LicenseStatusMessage = _entitlementService.HasProLicense
                ? "Pro license active"
                : "Free tier active";

            QuickActions.Add(new HomeActionItem { Title = "Live Channels", Detail = "Browse imported TV channels", Glyph = "\uE714", Target = "Channels" });
            QuickActions.Add(new HomeActionItem { Title = "Movies", Detail = "Open your VOD movie library", Glyph = "\uE8B2", Target = "Movies" });
            QuickActions.Add(new HomeActionItem { Title = "Series", Detail = "Continue episodic content", Glyph = "\uE8A9", Target = "Series" });
            QuickActions.Add(new HomeActionItem { Title = "Favorites", Detail = "Jump to saved channels", Glyph = "\uE734", Target = "Favorites" });
            QuickActions.Add(new HomeActionItem { Title = "Sources", Detail = "Manage playlists and providers", Glyph = "\uE8F1", Target = "Sources" });
            QuickActions.Add(new HomeActionItem { Title = "Settings", Detail = "Tune app preferences", Glyph = "\uE713", Target = "Settings" });
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var channelsCount = await db.Channels.CountAsync();
            var moviesCount = await db.Movies.CountAsync();
            var seriesCount = await db.Series.CountAsync();
            var favoritesCount = await db.Favorites.CountAsync();
            var sourcesCount = await db.SourceProfiles.CountAsync();
            var sourceIssuesCount = await db.SourceSyncStates.CountAsync(s => s.ErrorLog != string.Empty || s.HttpStatusCode >= 400);

            SummaryItems.Clear();
            SummaryItems.Add(new HomeSummaryItem { Label = "Channels", Value = channelsCount.ToString("N0"), Detail = "Live entries", Glyph = "\uE714" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Movies", Value = moviesCount.ToString("N0"), Detail = "VOD titles", Glyph = "\uE8B2" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Series", Value = seriesCount.ToString("N0"), Detail = "Shows imported", Glyph = "\uE8A9" });
            SummaryItems.Add(new HomeSummaryItem { Label = "Favorites", Value = favoritesCount.ToString("N0"), Detail = "Saved items", Glyph = "\uE734" });

            var totalItems = channelsCount + moviesCount + seriesCount;
            LibraryStatusMessage = totalItems > 0
                ? $"{totalItems:N0} library items available across live TV and VOD"
                : "No imported library items yet";

            SourceStatusMessage = sourcesCount > 0
                ? $"{sourcesCount:N0} source{(sourcesCount == 1 ? string.Empty : "s")} configured"
                : "No sources configured";

            SourceIssueVisibility = sourceIssuesCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            HeroSubtitle = sourcesCount > 0
                ? "Your library is ready. Pick up where you left off or jump into a section."
                : "Add a source to import channels, movies, series, and EPG data.";

            var lastSync = await db.SourceProfiles
                .Where(s => s.LastSync != null)
                .OrderByDescending(s => s.LastSync)
                .Select(s => s.LastSync)
                .FirstOrDefaultAsync();

            LastSyncMessage = lastSync.HasValue
                ? $"Last source sync: {lastSync.Value:g}"
                : "No source sync has completed yet";

            await LoadContinueItemsAsync(db);
        }

        private async Task LoadContinueItemsAsync(AppDbContext db)
        {
            ContinueItems.Clear();

            var recs = await db.PlaybackProgresses
                .Where(p => !p.IsCompleted)
                .OrderByDescending(p => p.LastWatched)
                .Take(3)
                .ToListAsync();

            if (recs.Count == 0)
            {
                ContinueItemsVisibility = Visibility.Collapsed;
                ContinueEmptyVisibility = Visibility.Visible;
                return;
            }

            var channelIds = recs.Where(r => r.ContentType == PlaybackContentType.Channel).Select(r => r.ContentId).ToList();
            var channels = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

            var movieIds = recs.Where(r => r.ContentType == PlaybackContentType.Movie).Select(r => r.ContentId).ToList();
            var movies = await db.Movies.Where(m => movieIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

            var episodeIds = recs.Where(r => r.ContentType == PlaybackContentType.Episode).Select(r => r.ContentId).ToList();
            var episodes = await db.Episodes.Where(e => episodeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);

            foreach (var r in recs)
            {
                var title = string.Empty;
                var streamUrl = string.Empty;

                if (r.ContentType == PlaybackContentType.Channel && channels.TryGetValue(r.ContentId, out var ch))
                {
                    title = ch.Name;
                    streamUrl = ch.StreamUrl;
                }
                else if (r.ContentType == PlaybackContentType.Movie && movies.TryGetValue(r.ContentId, out var mv))
                {
                    title = mv.Title;
                    streamUrl = mv.StreamUrl;
                }
                else if (r.ContentType == PlaybackContentType.Episode && episodes.TryGetValue(r.ContentId, out var ep))
                {
                    title = ep.Title;
                    streamUrl = ep.StreamUrl;
                }

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(streamUrl))
                {
                    continue;
                }

                ContinueItems.Add(new HomeContinueItem
                {
                    ContentId = r.ContentId,
                    ContentType = r.ContentType,
                    Title = title,
                    Detail = r.ContentType == PlaybackContentType.Channel
                        ? "Live channel"
                        : $"Saved at {TimeSpan.FromMilliseconds(r.PositionMs):hh\\:mm\\:ss}",
                    StreamUrl = streamUrl,
                    SavedPositionMs = r.PositionMs
                });
            }

            ContinueItemsVisibility = ContinueItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ContinueEmptyVisibility = ContinueItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
