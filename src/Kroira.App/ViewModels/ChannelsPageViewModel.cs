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

namespace Kroira.App.ViewModels
{
    public partial class ChannelsPageViewModel : ObservableObject
    {
        private const string Domain = ProfileDomains.Live;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<BrowserChannelViewModel> _allChannels = new();
        private readonly List<(int Id, string Name, int Count)> _allRawCategories = new();
        private readonly Dictionary<int, string> _sourceNames = new();
        private readonly Dictionary<int, string> _categoryNames = new();
        private BrowsePreferences _preferences = new();
        private int _activeProfileId;
        private int _filterRequestVersion;
        private bool _isInitializing;

        public ObservableCollection<BrowserChannelViewModel> FilteredChannels { get; } = new();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new();
        public ObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new();
        public ObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new();
        public ObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private BrowseSortOptionViewModel? _selectedSortOption;

        [ObservableProperty]
        private BrowseSourceFilterOptionViewModel? _selectedSourceOption;

        [ObservableProperty]
        private bool _favoritesOnly;

        [ObservableProperty]
        private bool _guideMatchedOnly;

        [ObservableProperty]
        private BrowseCategoryManagerOptionViewModel? _selectedManageCategory;

        [ObservableProperty]
        private string _manageCategoryAliasDraft = string.Empty;

        [ObservableProperty]
        private bool _isManageCategoryHidden;

        [ObservableProperty]
        private bool _hasAdvancedFilters;

        public bool HasManageCategorySelection => SelectedManageCategory != null;

        partial void OnSearchQueryChanged(string value)
        {
            QueueApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            QueueApplyFilter();
        }

        partial void OnSelectedSortOptionChanged(BrowseSortOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.SortKey = value?.Key ?? "name_asc";
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnSelectedSourceOptionChanged(BrowseSourceFilterOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.SelectedSourceId = value?.Id ?? 0;
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnFavoritesOnlyChanged(bool value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.FavoritesOnly = value;
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnGuideMatchedOnlyChanged(bool value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.GuideMatchedOnly = value;
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnSelectedManageCategoryChanged(BrowseCategoryManagerOptionViewModel? value)
        {
            if (value == null)
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
                OnPropertyChanged(nameof(HasManageCategorySelection));
                return;
            }

            _isInitializing = true;
            try
            {
                ManageCategoryAliasDraft = string.Equals(value.RawName, value.EffectiveName, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : value.EffectiveName;
                IsManageCategoryHidden = value.IsHidden;
            }
            finally
            {
                _isInitializing = false;
            }

            OnPropertyChanged(nameof(HasManageCategorySelection));
        }

        public ChannelsPageViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            SortOptions.Add(new BrowseSortOptionViewModel("name_asc", "Name A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("name_desc", "Name Z-A"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
            SortOptions.Add(new BrowseSortOptionViewModel("guide_first", "Guide-ready first"));
        }

        [RelayCommand]
        public async Task LoadChannelsAsync()
        {
            _allChannels.Clear();
            _allRawCategories.Clear();
            _sourceNames.Clear();
            _categoryNames.Clear();
            Categories.Clear();
            SourceOptions.Clear();
            SourceVisibilityOptions.Clear();
            ManageCategoryOptions.Clear();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var access = await profileService.GetAccessSnapshotAsync(db);
            _activeProfileId = access.ProfileId;
            _preferences = await browsePreferencesService.GetAsync(db, Domain, _activeProfileId);

            var categories = await db.ChannelCategories
                .AsNoTracking()
                .OrderBy(category => category.Name)
                .ToListAsync();
            var categoryMap = categories.ToDictionary(category => category.Id);
            var categoryLabels = ContentClassifier.BuildCategoryLabelSet(categories.Select(category => category.Name));
            var sources = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(source => source.Name)
                .ToListAsync();
            var sourceTypeById = sources.ToDictionary(source => source.Id, source => source.Type);
            foreach (var source in sources)
            {
                _sourceNames[source.Id] = source.Name;
            }

            var favoriteIds = (await db.Favorites
                .Where(favorite => favorite.ProfileId == _activeProfileId && favorite.ContentType == FavoriteType.Channel)
                .Select(favorite => favorite.ContentId)
                .ToListAsync())
                .ToHashSet();

            var channels = (await db.Channels.AsNoTracking().ToListAsync())
                .Where(channel => categoryMap.TryGetValue(channel.ChannelCategoryId, out var category) &&
                                  sourceTypeById.TryGetValue(category.SourceProfileId, out var sourceType) &&
                                  ContentClassifier.IsPlayableStoredLiveChannel(channel.Name, channel.StreamUrl, sourceType, categoryLabels) &&
                                  access.IsLiveChannelAllowed(channel, category))
                .ToList();

            foreach (var channel in channels)
            {
                var category = categoryMap[channel.ChannelCategoryId];
                var displayCategory = browsePreferencesService.GetEffectiveCategoryName(_preferences, category.Name);
                _allChannels.Add(new BrowserChannelViewModel
                {
                    Id = channel.Id,
                    CategoryId = channel.ChannelCategoryId,
                    SourceProfileId = category.SourceProfileId,
                    SourceName = _sourceNames.TryGetValue(category.SourceProfileId, out var sourceName) ? sourceName : "Unknown Source",
                    Name = channel.Name,
                    CategoryName = category.Name,
                    DisplayCategoryName = displayCategory,
                    StreamUrl = channel.StreamUrl,
                    LogoUrl = channel.LogoUrl ?? string.Empty,
                    IsFavorite = favoriteIds.Contains(channel.Id)
                });
            }

            foreach (var group in _allChannels.GroupBy(channel => channel.CategoryId))
            {
                if (!categoryMap.TryGetValue(group.Key, out var category))
                {
                    continue;
                }

                _categoryNames[group.Key] = category.Name;
                _allRawCategories.Add((group.Key, category.Name, group.Count()));
            }

            BuildSourceOptions();
            BuildCategoryManagerOptions();
            BuildVisibleCategories();

            _isInitializing = true;
            try
            {
                FavoritesOnly = _preferences.FavoritesOnly;
                GuideMatchedOnly = _preferences.GuideMatchedOnly;
                SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, _preferences.SortKey, StringComparison.OrdinalIgnoreCase))
                    ?? SortOptions.First();
                SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _preferences.SelectedSourceId)
                    ?? SourceOptions.FirstOrDefault();
                SelectedCategory = Categories.FirstOrDefault();
            }
            finally
            {
                _isInitializing = false;
            }

            QueueApplyFilter();
        }

        [RelayCommand]
        public async Task SaveCategoryPreferenceAsync()
        {
            if (SelectedManageCategory == null)
            {
                return;
            }

            var normalizedKey = SelectedManageCategory.Key;
            if (IsManageCategoryHidden)
            {
                if (!_preferences.HiddenCategoryKeys.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase))
                {
                    _preferences.HiddenCategoryKeys.Add(normalizedKey);
                }
            }
            else
            {
                _preferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, normalizedKey, StringComparison.OrdinalIgnoreCase));
            }

            var alias = ManageCategoryAliasDraft.Trim();
            if (string.IsNullOrWhiteSpace(alias) ||
                string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase))
            {
                _preferences.CategoryRemaps.Remove(normalizedKey);
            }
            else
            {
                _preferences.CategoryRemaps[normalizedKey] = alias;
            }

            await SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        [RelayCommand]
        public async Task ClearCategoryPreferenceAsync()
        {
            if (SelectedManageCategory == null)
            {
                return;
            }

            _preferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, SelectedManageCategory.Key, StringComparison.OrdinalIgnoreCase));
            _preferences.CategoryRemaps.Remove(SelectedManageCategory.Key);

            _isInitializing = true;
            try
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        private void QueueApplyFilter()
        {
            _ = ApplyFilterAsync(System.Threading.Interlocked.Increment(ref _filterRequestVersion));
        }

        private async Task ApplyFilterAsync(int requestVersion)
        {
            var browsePreferencesService = _serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            var visibleSourceIds = SourceVisibilityOptions
                .Where(option => option.IsVisible)
                .Select(option => option.Id)
                .ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allChannels.Select(channel => channel.SourceProfileId).Distinct().ToHashSet();
            }

            var filtered = _allChannels
                .Where(channel => visibleSourceIds.Contains(channel.SourceProfileId))
                .Where(channel => !browsePreferencesService.IsCategoryHidden(_preferences, channel.CategoryName))
                .Select(channel =>
                {
                    channel.DisplayCategoryName = browsePreferencesService.GetEffectiveCategoryName(_preferences, channel.CategoryName);
                    return channel;
                })
                .AsEnumerable();

            if (SelectedSourceOption != null && SelectedSourceOption.Id != 0)
            {
                filtered = filtered.Where(channel => channel.SourceProfileId == SelectedSourceOption.Id);
            }

            if (SelectedCategory != null && !string.IsNullOrWhiteSpace(SelectedCategory.FilterKey))
            {
                filtered = filtered.Where(channel =>
                    string.Equals(
                        browsePreferencesService.NormalizeCategoryKey(channel.DisplayCategoryName),
                        SelectedCategory.FilterKey,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (FavoritesOnly)
            {
                filtered = filtered.Where(channel => channel.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(channel =>
                    channel.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    channel.DisplayCategoryName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    channel.SourceName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            var nowUtc = DateTime.UtcNow;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var guideService = scope.ServiceProvider.GetRequiredService<ILiveGuideService>();
                var summaries = await guideService.GetGuideSummariesAsync(
                    db,
                    filteredList.Select(channel => channel.Id).ToList(),
                    nowUtc);

                foreach (var channel in filteredList)
                {
                    summaries.TryGetValue(channel.Id, out var summary);
                    channel.ApplyGuideSummary(summary, nowUtc);
                }
            }
            catch
            {
                foreach (var channel in filteredList)
                {
                    channel.ApplyGuideSummary(null, nowUtc);
                }
            }

            if (GuideMatchedOnly)
            {
                filteredList = filteredList.Where(channel => channel.HasMatchedGuide).ToList();
            }

            filteredList = SortChannels(filteredList).ToList();

            if (requestVersion != _filterRequestVersion)
            {
                return;
            }

            BuildVisibleCategories();
            EnsureSelectedSourceOption();
            EnsureSelectedCategory();

            FilteredChannels.Clear();
            foreach (var channel in filteredList)
            {
                FilteredChannels.Add(channel);
            }

            HasAdvancedFilters = FavoritesOnly ||
                                 GuideMatchedOnly ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "name_asc", "name_asc", StringComparison.OrdinalIgnoreCase);
            IsEmpty = FilteredChannels.Count == 0;
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int channelId)
        {
            var target = FilteredChannels.FirstOrDefault(channel => channel.Id == channelId)
                      ?? _allChannels.FirstOrDefault(channel => channel.Id == channelId);
            if (target == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);

            if (target.IsFavorite)
            {
                var favorite = await db.Favorites.FirstOrDefaultAsync(favorite =>
                    favorite.ProfileId == activeProfileId &&
                    favorite.ContentType == FavoriteType.Channel &&
                    favorite.ContentId == channelId);
                if (favorite != null)
                {
                    db.Favorites.Remove(favorite);
                    await db.SaveChangesAsync();
                }

                target.IsFavorite = false;
            }
            else
            {
                db.Favorites.Add(new Favorite
                {
                    ProfileId = activeProfileId,
                    ContentType = FavoriteType.Channel,
                    ContentId = channelId
                });
                await db.SaveChangesAsync();
                target.IsFavorite = true;
            }

            QueueApplyFilter();
        }

        private async Task SavePreferencesAndRefreshAsync(bool rebuildCollections)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            await browsePreferencesService.SaveAsync(db, Domain, _activeProfileId, _preferences);

            if (rebuildCollections)
            {
                BuildSourceOptions();
                BuildCategoryManagerOptions();
                BuildVisibleCategories();
            }

            QueueApplyFilter();
        }

        private void BuildSourceOptions()
        {
            var existingSelection = SelectedSourceOption?.Id ?? _preferences.SelectedSourceId;
            SourceOptions.Clear();
            SourceVisibilityOptions.Clear();

            SourceOptions.Add(new BrowseSourceFilterOptionViewModel(0, "All providers", _allChannels.Count));

            foreach (var group in _allChannels
                         .GroupBy(channel => channel.SourceProfileId)
                         .Select(group => new
                         {
                             Id = group.Key,
                             Name = _sourceNames.TryGetValue(group.Key, out var name) ? name : $"Source {group.Key}",
                             Count = group.Count()
                         })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_preferences.HiddenSourceIds.Contains(group.Id);
                SourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                SourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(
                    group.Id,
                    group.Name,
                    $"{group.Count:N0} channels",
                    isVisible,
                    OnSourceVisibilityChanged));
            }

            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
        }

        private void BuildVisibleCategories()
        {
            var browsePreferencesService = _serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel
            {
                Id = 0,
                FilterKey = string.Empty,
                Name = "All Categories",
                OrderIndex = -1
            });

            var visibleSourceIds = SourceVisibilityOptions
                .Where(option => option.IsVisible)
                .Select(option => option.Id)
                .ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allChannels.Select(channel => channel.SourceProfileId).Distinct().ToHashSet();
            }

            var categories = _allChannels
                .Where(channel => visibleSourceIds.Contains(channel.SourceProfileId))
                .Where(channel => !browsePreferencesService.IsCategoryHidden(_preferences, channel.CategoryName))
                .GroupBy(channel => browsePreferencesService.GetEffectiveCategoryName(_preferences, channel.CategoryName))
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select((group, index) => new BrowserCategoryViewModel
                {
                    Id = index + 1,
                    FilterKey = browsePreferencesService.NormalizeCategoryKey(group.Key),
                    Name = group.Key,
                    OrderIndex = index
                })
                .ToList();

            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }

        private void BuildCategoryManagerOptions()
        {
            var browsePreferencesService = _serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            var currentSelectionKey = SelectedManageCategory?.Key ?? string.Empty;
            ManageCategoryOptions.Clear();

            foreach (var category in _allRawCategories
                         .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var normalizedKey = browsePreferencesService.NormalizeCategoryKey(category.Name);
                ManageCategoryOptions.Add(new BrowseCategoryManagerOptionViewModel(
                    normalizedKey,
                    category.Name,
                    browsePreferencesService.GetEffectiveCategoryName(_preferences, category.Name),
                    category.Count,
                    _preferences.HiddenCategoryKeys.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase)));
            }

            SelectedManageCategory = ManageCategoryOptions.FirstOrDefault(option => string.Equals(option.Key, currentSelectionKey, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureSelectedSourceOption()
        {
            var selectedSourceId = SelectedSourceOption?.Id ?? 0;
            if (selectedSourceId != 0 &&
                SourceVisibilityOptions.Any(option => option.Id == selectedSourceId && !option.IsVisible))
            {
                _isInitializing = true;
                try
                {
                    SelectedSourceOption = SourceOptions.FirstOrDefault();
                }
                finally
                {
                    _isInitializing = false;
                }
            }
        }

        private void EnsureSelectedCategory()
        {
            var currentKey = SelectedCategory?.FilterKey ?? string.Empty;
            var targetCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, currentKey, StringComparison.OrdinalIgnoreCase))
                ?? Categories.FirstOrDefault();

            if (!ReferenceEquals(SelectedCategory, targetCategory))
            {
                _isInitializing = true;
                try
                {
                    SelectedCategory = targetCategory;
                }
                finally
                {
                    _isInitializing = false;
                }
            }
        }

        private IEnumerable<BrowserChannelViewModel> SortChannels(IEnumerable<BrowserChannelViewModel> channels)
        {
            return (SelectedSortOption?.Key ?? "name_asc") switch
            {
                "name_desc" => channels.OrderByDescending(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                "favorites_first" => channels
                    .OrderByDescending(channel => channel.IsFavorite)
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                "guide_first" => channels
                    .OrderByDescending(channel => channel.HasMatchedGuide)
                    .ThenBy(channel => channel.CurrentProgramTitle == "No current listing")
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => channels.OrderBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
            };
        }

        private void OnSourceVisibilityChanged(BrowseSourceVisibilityViewModel sourceOption, bool isVisible)
        {
            if (_isInitializing)
            {
                return;
            }

            if (isVisible)
            {
                _preferences.HiddenSourceIds.RemoveAll(id => id == sourceOption.Id);
            }
            else if (!_preferences.HiddenSourceIds.Contains(sourceOption.Id))
            {
                _preferences.HiddenSourceIds.Add(sourceOption.Id);
            }

            _ = SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }
    }
}
