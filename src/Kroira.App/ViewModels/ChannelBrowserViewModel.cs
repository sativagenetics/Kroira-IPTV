#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class BrowserCategoryViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
    }

    public partial class BrowserChannelViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string CurrentProgramTitle { get; set; } = "Guide not synced";
        public string CurrentProgramTimeText { get; set; } = string.Empty;
        public string CurrentProgramDescription { get; set; } = string.Empty;
        public string NextProgramTitle { get; set; } = string.Empty;
        public string NextProgramTimeText { get; set; } = string.Empty;
        public double LiveProgressValue { get; set; }
        public string LiveProgressText { get; set; } = string.Empty;
        public Visibility EpgVisibility { get; set; } = Visibility.Collapsed;
        public Visibility NextProgramVisibility { get; set; } = Visibility.Collapsed;
        public Visibility DescriptionVisibility { get; set; } = Visibility.Collapsed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
        private bool _isFavorite;

        public string FavoriteIcon => IsFavorite ? "★" : "☆";
    }

    public static class EpgProgramDisplay
    {
        public static void ApplyEpg(this BrowserChannelViewModel channel, EpgProgram? current, EpgProgram? next, DateTime nowUtc)
        {
            if (current == null)
            {
                channel.CurrentProgramTitle = next == null ? "Guide not synced" : "No current program";
                channel.CurrentProgramTimeText = string.Empty;
                channel.CurrentProgramDescription = string.Empty;
                channel.LiveProgressValue = 0;
                channel.LiveProgressText = string.Empty;
                channel.EpgVisibility = Visibility.Collapsed;
                channel.DescriptionVisibility = Visibility.Collapsed;

                if (next != null)
                {
                    channel.NextProgramTitle = $"Upcoming: {next.Title}";
                    channel.NextProgramTimeText = FormatTimeRange(next.StartTimeUtc, next.EndTimeUtc);
                    channel.NextProgramVisibility = Visibility.Visible;
                }
                else
                {
                    channel.NextProgramTitle = string.Empty;
                    channel.NextProgramTimeText = string.Empty;
                    channel.NextProgramVisibility = Visibility.Collapsed;
                }

                return;
            }

            channel.CurrentProgramTitle = current.Title;
            channel.CurrentProgramTimeText = FormatTimeRange(current.StartTimeUtc, current.EndTimeUtc);
            channel.CurrentProgramDescription = current.Description;
            channel.LiveProgressValue = CalculateProgress(current.StartTimeUtc, current.EndTimeUtc, nowUtc);
            channel.LiveProgressText = $"{Math.Round(channel.LiveProgressValue):0}% live";
            channel.EpgVisibility = Visibility.Visible;
            channel.DescriptionVisibility = string.IsNullOrWhiteSpace(current.Description)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (next != null)
            {
                channel.NextProgramTitle = $"Next: {next.Title}";
                channel.NextProgramTimeText = FormatTimeRange(next.StartTimeUtc, next.EndTimeUtc);
                channel.NextProgramVisibility = Visibility.Visible;
            }
            else
            {
                channel.NextProgramTitle = string.Empty;
                channel.NextProgramTimeText = string.Empty;
                channel.NextProgramVisibility = Visibility.Collapsed;
            }
        }

        private static double CalculateProgress(DateTime startUtc, DateTime endUtc, DateTime nowUtc)
        {
            var duration = (endUtc - startUtc).TotalSeconds;
            if (duration <= 0) return 0;

            var elapsed = (nowUtc - startUtc).TotalSeconds;
            return Math.Clamp(elapsed / duration * 100, 0, 100);
        }

        private static string FormatTimeRange(DateTime startUtc, DateTime endUtc)
        {
            return $"{startUtc.ToLocalTime():HH:mm} - {endUtc.ToLocalTime():HH:mm}";
        }
    }

    public partial class ChannelBrowserViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private int _sourceProfileId;
        private System.Collections.Generic.List<BrowserChannelViewModel> _allChannelsCache = new();

        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowserChannelViewModel> DisplayedChannels { get; } = new();

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        public ChannelBrowserViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task LoadSourceAsync(int sourceProfileId)
        {
            _sourceProfileId = sourceProfileId;
            Categories.Clear();
            DisplayedChannels.Clear();
            _allChannelsCache.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cats = await db.ChannelCategories
                .Where(c => c.SourceProfileId == sourceProfileId)
                .OrderBy(c => c.OrderIndex)
                .ToListAsync();

            var catIds = cats.Select(c => c.Id).ToList();
            var chans = await db.Channels
                .Where(ch => catIds.Contains(ch.ChannelCategoryId))
                .ToListAsync();

            var favIds = await db.Favorites
                .Where(f => f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            var chIds = chans.Select(c => c.Id).ToList();

            var now = DateTime.UtcNow;
            var epgWindowEnd = now.AddHours(8);

            var epgs = await db.EpgPrograms
                .Where(e => chIds.Contains(e.ChannelId)
                         && e.EndTimeUtc > now
                         && e.StartTimeUtc < epgWindowEnd)
                .OrderBy(e => e.StartTimeUtc)
                .ToListAsync();

            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });

            foreach (var c in cats)
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    OrderIndex = c.OrderIndex
                });
            }

            foreach (var ch in chans)
            {
                var chEpg = epgs.Where(e => e.ChannelId == ch.Id).ToList();
                var curr = chEpg.FirstOrDefault(e => e.StartTimeUtc <= now && e.EndTimeUtc > now);
                var next = chEpg.Where(e => e.StartTimeUtc >= (curr?.EndTimeUtc ?? now)).OrderBy(e => e.StartTimeUtc).FirstOrDefault();

                var item = new BrowserChannelViewModel
                {
                    Id = ch.Id,
                    CategoryId = ch.ChannelCategoryId,
                    Name = ch.Name,
                    StreamUrl = ch.StreamUrl,
                    LogoUrl = ch.LogoUrl ?? string.Empty,
                    IsFavorite = favIds.Contains(ch.Id)
                };
                item.ApplyEpg(curr, next, now);

                _allChannelsCache.Add(item);
            }

            SelectedCategory = Categories.FirstOrDefault();
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            ApplyFilter();
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = _allChannelsCache.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                query = query.Where(c => c.CategoryId == SelectedCategory.Id);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                query = query.Where(c => c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            DisplayedChannels.Clear();
            foreach (var ch in query)
            {
                DisplayedChannels.Add(ch);
            }
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = DisplayedChannels.FirstOrDefault(c => c.Id == channelId);
            if (target != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (target.IsFavorite)
                {
                    var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ContentType == FavoriteType.Channel && f.ContentId == channelId);
                    if (fav != null)
                    {
                        db.Favorites.Remove(fav);
                        await db.SaveChangesAsync();
                    }
                    target.IsFavorite = false;
                }
                else
                {
                    var fav = new Favorite { ContentType = FavoriteType.Channel, ContentId = channelId };
                    db.Favorites.Add(fav);
                    await db.SaveChangesAsync();
                    target.IsFavorite = true;
                }
            }
        }
    }
}
