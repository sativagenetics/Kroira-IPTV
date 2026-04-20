#nullable enable
using System;
using System.Collections.Generic;
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
    public partial class BrowserCategoryViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string FilterKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int OrderIndex { get; set; }
        public bool IsSmartCategory { get; set; }
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionBackgroundOpacity))]
        [NotifyPropertyChangedFor(nameof(SelectionChromeOpacity))]
        [NotifyPropertyChangedFor(nameof(SelectionAccentOpacity))]
        private bool _isSelected;
        public string DisplayName => ItemCount > 0 ? $"{Name} ({ItemCount:N0})" : Name;
        public string CountText => ItemCount > 0 ? $"{ItemCount:N0}" : string.Empty;
        public Visibility CountVisibility => ItemCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DescriptionVisibility => string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
        public double SelectionBackgroundOpacity => IsSelected ? 1 : 0;
        public double SelectionChromeOpacity => IsSelected ? 0.9 : 0;
        public double SelectionAccentOpacity => IsSelected ? 1 : 0.22;
    }

    public partial class BrowserChannelViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string RawName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string DisplayCategoryName { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string CurrentProgramTitle { get; set; } = string.Empty;
        public string CurrentProgramSubtitle { get; set; } = string.Empty;
        public string CurrentProgramTimeText { get; set; } = string.Empty;
        public string CurrentProgramDescription { get; set; } = string.Empty;
        public string CurrentProgramCategory { get; set; } = string.Empty;
        public string GuideMetaText { get; set; } = string.Empty;
        public string NextProgramTitle { get; set; } = string.Empty;
        public string NextProgramTimeText { get; set; } = string.Empty;
        public string NextProgramCompactText { get; set; } = string.Empty;
        public double LiveProgressValue { get; set; }
        public string LiveProgressText { get; set; } = string.Empty;
        public Visibility EpgVisibility { get; set; } = Visibility.Collapsed;
        public Visibility GuideMetaVisibility { get; set; } = Visibility.Collapsed;
        public Visibility NextProgramVisibility { get; set; } = Visibility.Collapsed;
        public Visibility DescriptionVisibility { get; set; } = Visibility.Collapsed;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public Visibility CategoryVisibility { get; set; } = Visibility.Collapsed;
        public Visibility LastTunedVisibility { get; set; } = Visibility.Collapsed;
        public Visibility QuickAccessBadgeVisibility { get; set; } = Visibility.Collapsed;
        public Visibility RemoveFromRecentVisibility { get; set; } = Visibility.Collapsed;
        public bool HasGuideData { get; set; }
        public bool HasMatchedGuide { get; set; }
        public bool IsLastTuned { get; set; }
        public bool IsSportsChannel { get; set; }
        public bool IsTurkishSportsChannel { get; set; }
        public int WatchCount { get; set; }
        public DateTime? LastWatchedAtUtc { get; set; }
        public string QuickAccessBadgeText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
        private bool _isFavorite;

        public string FavoriteIcon => IsFavorite ? "★" : "☆";
    }

    public sealed class LiveChannelSectionViewModel : ObservableObject
    {
        public LiveChannelSectionViewModel(string key, string title, string subtitle)
        {
            Key = key;
            Title = title;
            Subtitle = subtitle;
            Channels.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(Visibility));
                OnPropertyChanged(nameof(ClearRecentActionVisibility));
            };
        }

        public string Key { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public ObservableCollection<BrowserChannelViewModel> Channels { get; } = new();
        public Visibility Visibility => Channels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ClearRecentActionVisibility =>
            string.Equals(Key, "recent", StringComparison.OrdinalIgnoreCase) && Channels.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    public static class EpgProgramDisplay
    {
        public static void ApplyGuideSummary(this BrowserChannelViewModel channel, ChannelGuideSummary? summary, DateTime nowUtc)
        {
            var normalizedNowUtc = NormalizeUtc(nowUtc);
            if (summary == null)
            {
                ApplyGuideState(channel, "Guide unavailable", "Guide status could not be loaded.");
                return;
            }

            if (summary.SourceMode == EpgActiveMode.None)
            {
                ApplyGuideState(channel, "Guide disabled", summary.SourceStatusSummary);
                return;
            }

            if (summary.SourceStatus == EpgStatus.UnavailableNoXmltv)
            {
                ApplyGuideState(channel, "Guide unavailable from provider", summary.SourceStatusSummary);
                return;
            }

            if (summary.SourceStatus == EpgStatus.FailedFetchOrParse)
            {
                ApplyGuideState(channel, "Guide sync failed", summary.SourceStatusSummary);
                return;
            }

            if (!summary.HasGuideData)
            {
                var title = summary.SourceResultCode switch
                {
                    EpgSyncResultCode.PartialMatch => "No listing for this channel",
                    EpgSyncResultCode.ZeroCoverage => "No guide matches yet",
                    _ => "No guide data"
                };
                ApplyGuideState(channel, title, summary.SourceStatusSummary);
                return;
            }

            var current = summary.CurrentProgram;
            var next = summary.NextProgram;

            if (current == null)
            {
                channel.HasGuideData = true;
                channel.HasMatchedGuide = true;
                channel.CurrentProgramTitle = next == null ? "No current listing" : $"Upcoming: {next.Title}";
                channel.CurrentProgramSubtitle = string.Empty;
                channel.CurrentProgramTimeText = string.Empty;
                var meta = next == null
                    ? "Matched channel, but there is no current or next listing in the next 24 hours."
                    : $"Starts {FormatTimeRange(next.StartTimeUtc, next.EndTimeUtc)}";
                channel.GuideMetaText = summary.SourceStatus == EpgStatus.Stale
                    ? $"{meta} · Stale guide"
                    : meta;
                channel.CurrentProgramDescription = summary.SourceStatus == EpgStatus.Stale
                    ? summary.SourceStatusSummary
                    : string.Empty;
                channel.CurrentProgramCategory = string.Empty;
                channel.LiveProgressValue = 0;
                channel.LiveProgressText = string.Empty;
                channel.EpgVisibility = Visibility.Collapsed;
                channel.GuideMetaVisibility = Visibility.Visible;
                channel.DescriptionVisibility = string.IsNullOrWhiteSpace(channel.CurrentProgramDescription)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                channel.SubtitleVisibility = Visibility.Collapsed;
                channel.CategoryVisibility = Visibility.Collapsed;

                channel.NextProgramTitle = string.Empty;
                channel.NextProgramTimeText = string.Empty;
                channel.NextProgramCompactText = string.Empty;
                channel.NextProgramVisibility = Visibility.Collapsed;

                return;
            }

            channel.CurrentProgramTitle = current.Title;
            channel.HasGuideData = true;
            channel.HasMatchedGuide = true;
            channel.CurrentProgramSubtitle = current.Subtitle ?? string.Empty;
            channel.CurrentProgramTimeText = FormatTimeRange(current.StartTimeUtc, current.EndTimeUtc);
            channel.GuideMetaText = summary.SourceStatus == EpgStatus.Stale
                ? $"{channel.CurrentProgramTimeText} · Stale guide"
                : channel.CurrentProgramTimeText;
            channel.CurrentProgramDescription = summary.SourceStatus == EpgStatus.Stale
                ? summary.SourceStatusSummary
                : current.Description;
            channel.CurrentProgramCategory = current.Category ?? string.Empty;
            channel.LiveProgressValue = CalculateProgress(current.StartTimeUtc, current.EndTimeUtc, normalizedNowUtc);
            channel.LiveProgressText = $"{Math.Round(channel.LiveProgressValue):0}% live";
            channel.EpgVisibility = Visibility.Visible;
            channel.GuideMetaVisibility = Visibility.Visible;
            channel.DescriptionVisibility = string.IsNullOrWhiteSpace(current.Description)
                ? Visibility.Collapsed
                : Visibility.Visible;
            channel.SubtitleVisibility = string.IsNullOrWhiteSpace(current.Subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;
            channel.CategoryVisibility = string.IsNullOrWhiteSpace(current.Category)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (next != null)
            {
                channel.NextProgramTitle = $"Next: {next.Title}";
                channel.NextProgramTimeText = FormatTimeRange(next.StartTimeUtc, next.EndTimeUtc);
                channel.NextProgramCompactText = $"{NormalizeUtc(next.StartTimeUtc).ToLocalTime():HH:mm} {next.Title}";
                channel.NextProgramVisibility = Visibility.Visible;
            }
            else
            {
                channel.NextProgramTitle = string.Empty;
                channel.NextProgramTimeText = string.Empty;
                channel.NextProgramCompactText = string.Empty;
                channel.NextProgramVisibility = Visibility.Collapsed;
            }
        }

        private static void ApplyGuideState(BrowserChannelViewModel channel, string title, string detail)
        {
            channel.HasGuideData = false;
            channel.HasMatchedGuide = false;
            channel.CurrentProgramTitle = title;
            channel.CurrentProgramSubtitle = string.Empty;
            channel.CurrentProgramTimeText = string.Empty;
            channel.CurrentProgramDescription = detail;
            channel.CurrentProgramCategory = string.Empty;
            channel.GuideMetaText = detail;
            channel.NextProgramTitle = string.Empty;
            channel.NextProgramTimeText = string.Empty;
            channel.NextProgramCompactText = string.Empty;
            channel.LiveProgressValue = 0;
            channel.LiveProgressText = string.Empty;
            channel.EpgVisibility = Visibility.Collapsed;
            channel.GuideMetaVisibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
            channel.NextProgramVisibility = Visibility.Collapsed;
            channel.DescriptionVisibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
            channel.SubtitleVisibility = Visibility.Collapsed;
            channel.CategoryVisibility = Visibility.Collapsed;
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
            var localStart = NormalizeUtc(startUtc).ToLocalTime();
            var localEnd = NormalizeUtc(endUtc).ToLocalTime();
            return $"{localStart:HH:mm} - {localEnd:HH:mm}";
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    public partial class ChannelBrowserViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private List<BrowserChannelViewModel> _allChannelsCache = new();
        private int _filterRequestVersion;

        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowserChannelViewModel> DisplayedChannels { get; } = new();

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        public ChannelBrowserViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
        }

        public async Task LoadSourceAsync(int sourceProfileId)
        {
            Categories.Clear();
            DisplayedChannels.Clear();
            _allChannelsCache.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var access = await profileService.GetAccessSnapshotAsync(db);
            var activeProfileId = access.ProfileId;

            var cats = await db.ChannelCategories
                .Where(c => c.SourceProfileId == sourceProfileId)
                .OrderBy(c => c.OrderIndex)
                .ToListAsync();
            var categoryById = cats.ToDictionary(category => category.Id);
            var sourceType = await db.SourceProfiles
                .Where(source => source.Id == sourceProfileId)
                .Select(source => source.Type)
                .FirstOrDefaultAsync();

            var catIds = cats.Select(c => c.Id).ToList();
            var categoryLabels = ContentClassifier.BuildCategoryLabelSet(cats.Select(c => c.Name));
            var chans = (await db.Channels
                .Where(ch => catIds.Contains(ch.ChannelCategoryId))
                .ToListAsync())
                .Where(ch => ContentClassifier.IsPlayableStoredLiveChannel(ch.Name, ch.StreamUrl, sourceType, categoryLabels))
                .Where(ch => categoryById.TryGetValue(ch.ChannelCategoryId, out var category) &&
                             access.IsLiveChannelAllowed(ch, category))
                .ToList();

            var favIds = await db.Favorites
                .Where(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Channel)
                .Select(f => f.ContentId)
                .ToListAsync();

            // Populate category list
            Categories.Add(new BrowserCategoryViewModel { Id = 0, FilterKey = string.Empty, Name = "All Categories", OrderIndex = -1 });
            // Build channel view models first — always succeeds regardless of EPG state
            var channelVMs = chans.Select(ch =>
            {
                var category = categoryById[ch.ChannelCategoryId];
                var presentation = _taxonomyService.ResolveLiveChannelPresentation(ch.Name);
                var taxonomy = _taxonomyService.ResolveLiveCategory(category.Name, presentation.DisplayName);

                return new BrowserChannelViewModel
                {
                    Id = ch.Id,
                    CategoryId = ch.ChannelCategoryId,
                    RawName = ch.Name,
                    Name = string.IsNullOrWhiteSpace(presentation.DisplayName) ? ch.Name : presentation.DisplayName,
                    CategoryName = category.Name,
                    DisplayCategoryName = taxonomy.DisplayCategoryName,
                    StreamUrl = ch.StreamUrl,
                    LogoUrl = ch.LogoUrl ?? string.Empty,
                    IsFavorite = favIds.Contains(ch.Id)
                };
            }).ToList();

            var categoryIndex = 1;
            foreach (var category in channelVMs
                         .GroupBy(channel => channel.DisplayCategoryName)
                         .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = categoryIndex,
                    FilterKey = NormalizeCategoryKey(category.Key),
                    Name = category.Key,
                    ItemCount = category.Count(),
                    OrderIndex = categoryIndex
                });
                categoryIndex++;
            }

            foreach (var item in channelVMs)
                _allChannelsCache.Add(item);

            SelectedCategory = Categories.FirstOrDefault();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            QueueApplyFilter();
        }

        partial void OnSearchQueryChanged(string value)
        {
            QueueApplyFilter();
        }

        private void QueueApplyFilter()
        {
            _ = ApplyFilterAsync(System.Threading.Interlocked.Increment(ref _filterRequestVersion));
        }

        private async Task ApplyFilterAsync(int requestVersion)
        {
            var query = _allChannelsCache.AsEnumerable();

            if (SelectedCategory != null && !string.IsNullOrWhiteSpace(SelectedCategory.FilterKey))
            {
                query = query.Where(c => string.Equals(
                    NormalizeCategoryKey(c.DisplayCategoryName),
                    SelectedCategory.FilterKey,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                query = query.Where(c =>
                    c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    c.RawName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    c.DisplayCategoryName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            var filtered = query.ToList();
            var nowUtc = DateTime.UtcNow;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var guideService = scope.ServiceProvider.GetRequiredService<ILiveGuideService>();
                var summaries = await guideService.GetGuideSummariesAsync(
                    db,
                    filtered.Select(channel => channel.Id).ToList(),
                    nowUtc);

                foreach (var channel in filtered)
                {
                    summaries.TryGetValue(channel.Id, out var summary);
                    channel.ApplyGuideSummary(summary, nowUtc);
                }
            }
            catch
            {
                foreach (var channel in filtered)
                {
                    channel.ApplyGuideSummary(null, nowUtc);
                }
            }

            if (requestVersion != _filterRequestVersion)
            {
                return;
            }

            DisplayedChannels.Clear();
            foreach (var ch in filtered)
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
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var activeProfileId = await profileService.GetActiveProfileIdAsync(db);

                if (target.IsFavorite)
                {
                    var fav = await db.Favorites.FirstOrDefaultAsync(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Channel && f.ContentId == channelId);
                    if (fav != null)
                    {
                        db.Favorites.Remove(fav);
                        await db.SaveChangesAsync();
                    }
                    target.IsFavorite = false;
                }
                else
                {
                    var fav = new Favorite { ProfileId = activeProfileId, ContentType = FavoriteType.Channel, ContentId = channelId };
                    db.Favorites.Add(fav);
                    await db.SaveChangesAsync();
                    target.IsFavorite = true;
                }
            }
        }

        private static string NormalizeCategoryKey(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName)
                ? string.Empty
                : ContentClassifier.NormalizeLabel(categoryName).Trim().ToLowerInvariant();
        }
    }
}
