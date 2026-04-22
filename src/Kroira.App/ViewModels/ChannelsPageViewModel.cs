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
using System.IO;

namespace Kroira.App.ViewModels
{
    public partial class ChannelsPageViewModel : ObservableObject
    {
        private const string DefaultPrioritySortKey = "priority_first";
        private const string SmartPriorityCategoryKey = "__smart_priority";
        private const string SmartSportsCategoryKey = "__smart_sports";
        private const string SmartTurkishSportsCategoryKey = "__smart_turkish_sports";
        private const string SmartRecentCategoryKey = "__smart_recent";
        private static readonly bool EnableDeferredSpotlightHelpers = false;
        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CHANNELSVM {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private const string Domain = ProfileDomains.Live;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBrowsePreferencesService _browsePreferencesService;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private readonly ILogicalCatalogStateService _logicalCatalogStateService;
        private readonly List<BrowserChannelViewModel> _allChannels = new();
        private readonly List<(int Id, string Name, int Count)> _allRawCategories = new();
        private readonly Dictionary<int, string> _sourceNames = new();
        private readonly Dictionary<int, string> _categoryNames = new();
        private readonly Dictionary<string, LiveChannelSectionViewModel> _spotlightSectionMap = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _favoriteLogicalKeys = new(StringComparer.OrdinalIgnoreCase);
        private BrowsePreferences _preferences = new();
        private int _activeProfileId;
        private int _filterRequestVersion;
        private bool _isInitializing;

        public ObservableCollection<BrowserChannelViewModel> FilteredChannels { get; } = new();
        public ObservableCollection<LiveChannelSectionViewModel> SpotlightSections { get; } = new();
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
        private SurfaceStatePresentation _surfaceState = SurfaceStateCopies.LiveTv.Create(SurfaceViewState.Loading);

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
        public string BrowseResultTitle => SelectedCategory?.Name ?? "All channels";
        public string BrowseResultSubtitle => ResolveBrowseResultSubtitle();
        public string BrowseResultCountText => FilteredChannels.Count == 1
            ? "1 channel"
            : $"{FilteredChannels.Count:N0} channels";
        public Microsoft.UI.Xaml.Visibility ClearRecentHistoryVisibility => HasRecentHistory
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility BrowseClearRecentVisibility =>
            HasRecentHistory &&
            string.Equals(SelectedCategory?.FilterKey, SmartRecentCategoryKey, StringComparison.OrdinalIgnoreCase)
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility SpotlightVisibility => SpotlightSections.Any(section => section.Channels.Count > 0)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        private bool HasRecentHistory =>
            !string.IsNullOrWhiteSpace(_preferences.LastChannel?.LogicalKey) ||
            _preferences.RecentChannels.Count > 0 ||
            _preferences.LastChannelId > 0 ||
            _preferences.RecentChannelIds.Count > 0;

        partial void OnSearchQueryChanged(string value)
        {
            Log($"SEL: search query changed -> '{value}'");
            QueueApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.SelectedCategoryKey = value?.FilterKey ?? string.Empty;
            Log($"SEL: category -> {value?.FilterKey ?? "(all)"} ({value?.Name ?? "All channels"})");
            NotifyBrowseResultChanged();
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnSelectedSortOptionChanged(BrowseSortOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.SortKey = value?.Key ?? DefaultPrioritySortKey;
            _preferences.HasExplicitLiveSortPreference = true;
            Log($"SEL: sort -> {_preferences.SortKey}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnSelectedSourceOptionChanged(BrowseSourceFilterOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.SelectedSourceId = value?.Id ?? 0;
            Log($"SEL: source filter -> {_preferences.SelectedSourceId}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        partial void OnFavoritesOnlyChanged(bool value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.FavoritesOnly = value;
            Log($"SEL: favorites only -> {value}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnGuideMatchedOnlyChanged(bool value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.GuideMatchedOnly = value;
            Log($"SEL: guide matched only -> {value}");
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
                ManageCategoryAliasDraft = value.HasManualAlias ? value.EffectiveName : string.Empty;
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
            _browsePreferencesService = serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
            _logicalCatalogStateService = serviceProvider.GetRequiredService<ILogicalCatalogStateService>();
            RegisterSpotlightSection("last_tuned", "Last tuned", "Jump straight back into the last live channel you opened.");
            RegisterSpotlightSection("recent", "Recent", "Fast return to recently watched live channels.");
            RegisterSpotlightSection("priority", "Priority channels", "Favorites, sports, guide-ready channels, and channels you revisit often.");
            RegisterSpotlightSection("turkish_sports", "Turkish sports", "Quick access to Turkish sports and football coverage across providers.");
            RegisterSpotlightSection("most_watched_sports", "Most watched sports", "Sports channels you actually watch the most on this profile.");
            SortOptions.Add(new BrowseSortOptionViewModel(DefaultPrioritySortKey, "Priority first"));
            SortOptions.Add(new BrowseSortOptionViewModel("name_desc", "Name Z-A"));
            SortOptions.Add(new BrowseSortOptionViewModel("name_asc", "Name A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
            SortOptions.Add(new BrowseSortOptionViewModel("guide_first", "Guide-ready first"));
        }

        [RelayCommand]
        public async Task LoadChannelsAsync()
        {
            Log("01: LoadChannelsAsync entered");
            SurfaceState = SurfaceStateCopies.LiveTv.Create(SurfaceViewState.Loading);
            _allChannels.Clear();
            _allRawCategories.Clear();
            _sourceNames.Clear();
            _categoryNames.Clear();
            Categories.Clear();
            ClearSpotlightSections();
            SourceOptions.Clear();
            SourceVisibilityOptions.Clear();
            ManageCategoryOptions.Clear();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var surfaceStateService = scope.ServiceProvider.GetRequiredService<ISurfaceStateService>();
                var sourceAvailability = await surfaceStateService.GetSourceAvailabilityAsync(db);
                if (sourceAvailability.SourceCount == 0)
                {
                    SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, 0, SurfaceStateCopies.LiveTv);
                    return;
                }

                var access = await profileService.GetAccessSnapshotAsync(db);
                _activeProfileId = access.ProfileId;
                try
                {
                    await _logicalCatalogStateService.ReconcilePersistentStateAsync(db, _activeProfileId);
                }
                catch (Exception ex)
                {
                    Log($"02a: state reconciliation skipped - {ex.GetType().Name}: {ex.Message}");
                }
                _preferences = await _browsePreferencesService.GetAsync(db, Domain, _activeProfileId);
                Log("02: resolved services and preferences");

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

                _favoriteLogicalKeys = await _logicalCatalogStateService.GetFavoriteLogicalKeysAsync(db, _activeProfileId, FavoriteType.Channel);
                var channelProgressRows = await db.PlaybackProgresses
                    .Where(progress => progress.ProfileId == _activeProfileId && progress.ContentType == PlaybackContentType.Channel)
                    .OrderByDescending(progress => progress.LastWatched)
                    .ToListAsync();
                var progressByLogicalKey = channelProgressRows
                    .Where(progress => !string.IsNullOrWhiteSpace(progress.LogicalContentKey))
                    .GroupBy(progress => progress.LogicalContentKey!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                var progressByChannelId = channelProgressRows
                    .GroupBy(progress => progress.ContentId)
                    .ToDictionary(group => group.Key, group => group.First());

                var channels = (await db.Channels.AsNoTracking().ToListAsync())
                    .Where(channel => categoryMap.TryGetValue(channel.ChannelCategoryId, out var category) &&
                                      sourceTypeById.TryGetValue(category.SourceProfileId, out var sourceType) &&
                                      ContentClassifier.IsPlayableStoredLiveChannel(channel.Name, channel.StreamUrl, sourceType, categoryLabels) &&
                                      access.IsLiveChannelAllowed(channel, category))
                    .ToList();
                Log($"03: loaded {channels.Count} candidate channels");

                foreach (var channel in channels)
                {
                    var category = categoryMap[channel.ChannelCategoryId];
                    var logicalKey = _logicalCatalogStateService.BuildChannelLogicalKey(channel);
                    var watchCount = GetChannelWatchCount(logicalKey, channel.Id);
                    var lastWatchedAtUtc = ResolveLastWatchedAtUtc(progressByLogicalKey, progressByChannelId, logicalKey, channel.Id);
                    var presentation = _taxonomyService.ResolveLiveChannelPresentation(channel.Name);
                    var cleanedChannelName = string.IsNullOrWhiteSpace(presentation.DisplayName)
                        ? channel.Name
                        : presentation.DisplayName;
                    var displayCategory = ResolveEffectiveLiveDisplayCategoryName(category.Name, cleanedChannelName);
                    _allChannels.Add(new BrowserChannelViewModel
                    {
                        Id = channel.Id,
                        CategoryId = channel.ChannelCategoryId,
                        SourceProfileId = category.SourceProfileId,
                        PreferredSourceProfileId = category.SourceProfileId,
                        LogicalContentKey = logicalKey,
                        SourceName = _sourceNames.TryGetValue(category.SourceProfileId, out var sourceName) ? sourceName : "Unknown Source",
                        RawName = channel.Name,
                        Name = cleanedChannelName,
                        CategoryName = category.Name,
                        DisplayCategoryName = displayCategory,
                        StreamUrl = channel.StreamUrl,
                        LogoUrl = channel.LogoUrl ?? string.Empty,
                        IsFavorite = _favoriteLogicalKeys.Contains(logicalKey),
                        IsSportsChannel = ContentClassifier.IsSportsLikeChannel(cleanedChannelName, displayCategory),
                        IsTurkishSportsChannel = ContentClassifier.IsTurkishSportsLikeChannel(cleanedChannelName, displayCategory),
                        WatchCount = watchCount,
                        LastWatchedAtUtc = lastWatchedAtUtc,
                        SupportsCatchup = channel.SupportsCatchup,
                        CatchupWindowHours = channel.CatchupWindowHours,
                        CatchupSummary = channel.CatchupSummary ?? string.Empty
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
                Log("04: built source/category collections");

                _isInitializing = true;
                try
                {
                    FavoritesOnly = _preferences.FavoritesOnly;
                    GuideMatchedOnly = _preferences.GuideMatchedOnly;
                    SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, ResolveInitialSortKey(), StringComparison.OrdinalIgnoreCase))
                        ?? SortOptions.First();
                    SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _preferences.SelectedSourceId)
                        ?? SourceOptions.FirstOrDefault();
                    SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, _preferences.SelectedCategoryKey, StringComparison.OrdinalIgnoreCase))
                        ?? Categories.FirstOrDefault();
                }
                finally
                {
                    _isInitializing = false;
                }

                Log("05: initialized selected filters");
                QueueApplyFilter();
                SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, _allChannels.Count, SurfaceStateCopies.LiveTv);
                Log("06: queued ApplyFilter");
            }
            catch (Exception ex)
            {
                Log($"LOAD FAILED: {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.LiveTv, ex);
            }
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
                string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, SelectedManageCategory.AutoDisplayName, StringComparison.OrdinalIgnoreCase))
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

        [RelayCommand]
        public async Task ClearRecentHistoryAsync()
        {
            if (!HasRecentHistory)
            {
                return;
            }

            _preferences.LastChannel = null;
            _preferences.RecentChannels.Clear();
            _preferences.LastChannelId = 0;
            _preferences.RecentChannelIds.Clear();
            await SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        [RelayCommand]
        public async Task RemoveRecentHistoryItemAsync(int channelId)
        {
            if (channelId <= 0)
            {
                return;
            }

            var target = _allChannels.FirstOrDefault(channel => channel.Id == channelId);
            var logicalKey = target?.LogicalContentKey ?? string.Empty;
            var removed = _preferences.RecentChannelIds.RemoveAll(id => id == channelId) > 0;
            if (_preferences.LastChannelId == channelId)
            {
                _preferences.LastChannelId = _preferences.RecentChannelIds.FirstOrDefault();
                removed = true;
            }

            if (!string.IsNullOrWhiteSpace(logicalKey))
            {
                removed = _preferences.RecentChannels.RemoveAll(item =>
                    string.Equals(item.LogicalKey, logicalKey, StringComparison.OrdinalIgnoreCase)) > 0 || removed;

                if (string.Equals(_preferences.LastChannel?.LogicalKey, logicalKey, StringComparison.OrdinalIgnoreCase))
                {
                    _preferences.LastChannel = _preferences.RecentChannels.FirstOrDefault();
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            await SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        private void QueueApplyFilter()
        {
            _ = ApplyFilterAsync(System.Threading.Interlocked.Increment(ref _filterRequestVersion));
        }

        private async Task ApplyFilterAsync(int requestVersion)
        {
            Log($"07: ApplyFilterAsync start request={requestVersion}");
            var baseVisibleChannels = BuildSourceScopedChannels();

            var filtered = baseVisibleChannels.AsEnumerable();

            if (SelectedCategory != null && !string.IsNullOrWhiteSpace(SelectedCategory.FilterKey))
            {
                filtered = filtered.Where(channel => MatchesCategorySelection(channel, SelectedCategory.FilterKey));
            }

            if (FavoritesOnly)
            {
                filtered = filtered.Where(channel => channel.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(channel =>
                    channel.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    channel.RawName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    channel.DisplayCategoryName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    channel.SourceName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            var nowUtc = DateTime.UtcNow;
            Log($"08: filtered down to {filteredList.Count} channels before guide load");

            if (requestVersion != _filterRequestVersion)
            {
                Log($"08a: request {requestVersion} became stale before initial grid apply");
                return;
            }

            if (!GuideMatchedOnly)
            {
                var initialSorted = SortChannels(filteredList).ToList();
                ApplyVisibleChannels(initialSorted, requestVersion, "08b: initial grid applied before deferred guide refresh");
                await Task.Yield();
            }
            else
            {
                Log("08b: guide matched filter active; deferring initial grid until guide refresh completes");
            }

            if (requestVersion != _filterRequestVersion)
            {
                Log($"08c: request {requestVersion} became stale before deferred guide refresh");
                return;
            }

            await RefreshGuideAndDeferredSectionsAsync(requestVersion, baseVisibleChannels, filteredList, nowUtc);
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
            var isFavorite = await _logicalCatalogStateService.ToggleFavoriteAsync(db, activeProfileId, FavoriteType.Channel, channelId);
            UpdateFavoriteState(target.LogicalContentKey, isFavorite);

            QueueApplyFilter();
        }

        public async Task RecordChannelLaunchAsync(int channelId)
        {
            if (channelId <= 0 || _activeProfileId <= 0)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _logicalCatalogStateService.RecordLiveChannelLaunchAsync(db, _activeProfileId, channelId);
            _preferences = await _browsePreferencesService.GetAsync(db, Domain, _activeProfileId);
            ApplyLastTunedState(_allChannels);
            var target = _allChannels.FirstOrDefault(channel => channel.Id == channelId);
            if (target != null)
            {
                var watchCount = GetChannelWatchCount(target.LogicalContentKey, target.Id);
                foreach (var channel in _allChannels.Where(channel =>
                             string.Equals(channel.LogicalContentKey, target.LogicalContentKey, StringComparison.OrdinalIgnoreCase)))
                {
                    channel.WatchCount = watchCount;
                    channel.LastWatchedAtUtc = DateTime.UtcNow;
                }
            }
        }

        private async Task SavePreferencesAndRefreshAsync(bool rebuildCollections)
        {
            Log($"REFRESH: SavePreferencesAndRefreshAsync rebuildCollections={rebuildCollections}");
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _browsePreferencesService.SaveAsync(db, Domain, _activeProfileId, _preferences);

            if (rebuildCollections)
            {
                BuildSourceOptions();
                BuildCategoryManagerOptions();
                BuildVisibleCategories();
                EnsureSelectedCategory();
            }

            QueueApplyFilter();
        }

        private void UpdateFavoriteState(string logicalKey, bool isFavorite)
        {
            if (string.IsNullOrWhiteSpace(logicalKey))
            {
                return;
            }

            if (isFavorite)
            {
                _favoriteLogicalKeys.Add(logicalKey);
            }
            else
            {
                _favoriteLogicalKeys.Remove(logicalKey);
            }

            foreach (var channel in _allChannels.Where(item =>
                         string.Equals(item.LogicalContentKey, logicalKey, StringComparison.OrdinalIgnoreCase)))
            {
                channel.IsFavorite = isFavorite;
            }
        }

        private int GetChannelWatchCount(string logicalKey, int channelId)
        {
            if (!string.IsNullOrWhiteSpace(logicalKey) &&
                _preferences.LiveChannelWatchCountsByKey.TryGetValue(logicalKey, out var logicalCount))
            {
                return logicalCount;
            }

            return _preferences.LiveChannelWatchCounts.TryGetValue(channelId, out var legacyCount)
                ? legacyCount
                : 0;
        }

        private static DateTime? ResolveLastWatchedAtUtc(
            IReadOnlyDictionary<string, PlaybackProgress> progressByLogicalKey,
            IReadOnlyDictionary<int, PlaybackProgress> progressByChannelId,
            string logicalKey,
            int channelId)
        {
            if (!string.IsNullOrWhiteSpace(logicalKey) &&
                progressByLogicalKey.TryGetValue(logicalKey, out var logicalProgress))
            {
                return logicalProgress.LastWatched;
            }

            return progressByChannelId.TryGetValue(channelId, out var progress)
                ? progress.LastWatched
                : null;
        }

        private bool IsLastChannel(BrowserChannelViewModel channel)
        {
            return MatchesChannelReference(_preferences.LastChannel, channel) ||
                   channel.Id == _preferences.LastChannelId;
        }

        private bool IsRecentChannel(BrowserChannelViewModel channel)
        {
            return FindRecentRank(channel) >= 0;
        }

        private int FindRecentRank(BrowserChannelViewModel channel)
        {
            var logicalRank = _preferences.RecentChannels.FindIndex(reference => MatchesChannelReference(reference, channel));
            if (logicalRank >= 0)
            {
                return logicalRank;
            }

            return _preferences.RecentChannelIds.FindIndex(id => id == channel.Id);
        }

        private static bool MatchesChannelReference(BrowseChannelReference? reference, BrowserChannelViewModel channel)
        {
            if (reference == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(reference.LogicalKey) &&
                   !string.IsNullOrWhiteSpace(channel.LogicalContentKey) &&
                   string.Equals(reference.LogicalKey, channel.LogicalContentKey, StringComparison.OrdinalIgnoreCase);
        }

        private static BrowserChannelViewModel? ResolveChannelReference(
            BrowseChannelReference? reference,
            IReadOnlyDictionary<string, BrowserChannelViewModel> channelByLogicalKey)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.LogicalKey))
            {
                return null;
            }

            return channelByLogicalKey.TryGetValue(reference.LogicalKey, out var match)
                ? match
                : null;
        }

        private string ResolveInitialSortKey()
        {
            if (!_preferences.HasExplicitLiveSortPreference &&
                (string.IsNullOrWhiteSpace(_preferences.SortKey) ||
                 string.Equals(_preferences.SortKey, "name_asc", StringComparison.OrdinalIgnoreCase)))
            {
                return DefaultPrioritySortKey;
            }

            return string.IsNullOrWhiteSpace(_preferences.SortKey)
                ? DefaultPrioritySortKey
                : _preferences.SortKey;
        }

        private void NotifyBrowseResultChanged()
        {
            OnPropertyChanged(nameof(BrowseResultTitle));
            OnPropertyChanged(nameof(BrowseResultSubtitle));
            OnPropertyChanged(nameof(BrowseResultCountText));
            OnPropertyChanged(nameof(ClearRecentHistoryVisibility));
            OnPropertyChanged(nameof(BrowseClearRecentVisibility));
        }

        private void RegisterSpotlightSection(string key, string title, string subtitle)
        {
            var section = new LiveChannelSectionViewModel(key, title, subtitle);
            section.Channels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SpotlightVisibility));
            SpotlightSections.Add(section);
            _spotlightSectionMap[key] = section;
        }

        private void ClearSpotlightSections()
        {
            foreach (var section in SpotlightSections)
            {
                section.Channels.Clear();
            }

            OnPropertyChanged(nameof(SpotlightVisibility));
        }

        private List<BrowserChannelViewModel> BuildSourceScopedChannels()
        {
            var visibleSourceIds = SourceVisibilityOptions
                .Where(option => option.IsVisible)
                .Select(option => option.Id)
                .ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allChannels.Select(channel => channel.SourceProfileId).Distinct().ToHashSet();
            }

            var baseVisibleChannels = _allChannels
                .Where(channel => visibleSourceIds.Contains(channel.SourceProfileId))
                .Where(channel => !_browsePreferencesService.IsCategoryHidden(_preferences, channel.CategoryName))
                .Select(channel =>
                {
                    channel.DisplayCategoryName = ResolveEffectiveLiveDisplayCategoryName(channel.CategoryName, channel.Name);
                    channel.IsSportsChannel = ContentClassifier.IsSportsLikeChannel(channel.Name, channel.DisplayCategoryName);
                    channel.IsTurkishSportsChannel = ContentClassifier.IsTurkishSportsLikeChannel(channel.Name, channel.DisplayCategoryName);
                    return channel;
                })
                .ToList();

            if (SelectedSourceOption != null && SelectedSourceOption.Id != 0)
            {
                baseVisibleChannels = baseVisibleChannels
                    .Where(channel => channel.SourceProfileId == SelectedSourceOption.Id)
                    .ToList();
            }

            return baseVisibleChannels;
        }

        private bool MatchesCategorySelection(BrowserChannelViewModel channel, string filterKey)
        {
            return filterKey switch
            {
                SmartPriorityCategoryKey => IsPriorityCandidate(channel),
                SmartSportsCategoryKey => channel.IsSportsChannel,
                SmartTurkishSportsCategoryKey => channel.IsTurkishSportsChannel,
                SmartRecentCategoryKey => IsRecentChannel(channel),
                _ => string.Equals(
                    _browsePreferencesService.NormalizeCategoryKey(channel.DisplayCategoryName),
                    filterKey,
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        private async Task RefreshSpotlightSectionsAsync(
            AppDbContext db,
            ILiveGuideService guideService,
            IReadOnlyCollection<BrowserChannelViewModel> candidateChannels,
            DateTime nowUtc)
        {
            Log($"12: RefreshSpotlightSectionsAsync start with {candidateChannels.Count} candidates");
            ApplyLastTunedState(_allChannels);
            if (!ShouldRenderSpotlightSections(candidateChannels))
            {
                ClearSpotlightSections();
                return;
            }

            var sectionChannels = ComposeSpotlightSections(candidateChannels);
            var spotlightIds = sectionChannels
                .Values
                .SelectMany(channels => channels)
                .Select(channel => channel.Id)
                .Distinct()
                .ToList();

            if (spotlightIds.Count > 0)
            {
                var summaries = await guideService.GetGuideSummariesAsync(db, spotlightIds, nowUtc);
                foreach (var channel in sectionChannels.Values.SelectMany(channels => channels).DistinctBy(channel => channel.Id))
                {
                    summaries.TryGetValue(channel.Id, out var summary);
                    channel.ApplyGuideSummary(summary, nowUtc);
                }
            }

            ApplySpotlightSections(sectionChannels);
            Log($"13: RefreshSpotlightSectionsAsync rendered {SpotlightSections.Sum(section => section.Channels.Count)} shortcuts");
        }

        private void ResetSpotlightSections(IReadOnlyCollection<BrowserChannelViewModel> candidateChannels)
        {
            ApplyLastTunedState(_allChannels);
            if (!ShouldRenderSpotlightSections(candidateChannels))
            {
                ClearSpotlightSections();
                return;
            }

            ApplySpotlightSections(ComposeSpotlightSections(candidateChannels));
        }

        private bool ShouldRenderSpotlightSections(IReadOnlyCollection<BrowserChannelViewModel> candidateChannels)
        {
            return candidateChannels.Count > 0 && string.IsNullOrWhiteSpace(SearchQuery);
        }

        private Dictionary<string, List<BrowserChannelViewModel>> ComposeSpotlightSections(IReadOnlyCollection<BrowserChannelViewModel> candidateChannels)
        {
            var channelById = candidateChannels
                .GroupBy(channel => channel.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var channelByLogicalKey = candidateChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel.LogicalContentKey))
                .GroupBy(channel => channel.LogicalContentKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var usedIds = new HashSet<int>();

            foreach (var channel in candidateChannels)
            {
                channel.RemoveFromRecentVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }

            List<BrowserChannelViewModel> BuildOrdered(Func<BrowserChannelViewModel, bool> predicate, int take)
            {
                return candidateChannels
                    .Where(predicate)
                    .Where(channel => !usedIds.Contains(channel.Id))
                    .OrderByDescending(ComputePriorityScore)
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(take)
                    .ToList();
            }

            var sections = new Dictionary<string, List<BrowserChannelViewModel>>(StringComparer.OrdinalIgnoreCase);

            var lastTuned = ResolveChannelReference(_preferences.LastChannel, channelByLogicalKey)
                ?? (_preferences.LastChannelId > 0 && channelById.TryGetValue(_preferences.LastChannelId, out var fallbackLastTuned)
                    ? fallbackLastTuned
                    : null);
            if (lastTuned != null)
            {
                sections["last_tuned"] = new List<BrowserChannelViewModel> { lastTuned };
                usedIds.Add(lastTuned.Id);
            }
            else
            {
                sections["last_tuned"] = new List<BrowserChannelViewModel>();
            }

            sections["recent"] = _preferences.RecentChannels
                .Select(reference => ResolveChannelReference(reference, channelByLogicalKey))
                .Where(channel => channel != null && !IsLastChannel(channel!))
                .Cast<BrowserChannelViewModel>()
                .Concat(_preferences.RecentChannelIds
                    .Where(id => id != _preferences.LastChannelId)
                    .Where(channelById.ContainsKey)
                    .Select(id => channelById[id]))
                .Where(channel => usedIds.Add(channel.Id))
                .Take(6)
                .ToList();
            foreach (var channel in sections["recent"])
            {
                channel.RemoveFromRecentVisibility = Microsoft.UI.Xaml.Visibility.Visible;
            }

            sections["priority"] = BuildOrdered(IsPriorityCandidate, 8);
            foreach (var channel in sections["priority"])
            {
                usedIds.Add(channel.Id);
            }

            sections["turkish_sports"] = BuildOrdered(channel => channel.IsTurkishSportsChannel, 6);
            foreach (var channel in sections["turkish_sports"])
            {
                usedIds.Add(channel.Id);
            }

            sections["most_watched_sports"] = candidateChannels
                .Where(channel => channel.IsSportsChannel && channel.WatchCount > 0)
                .Where(channel => !usedIds.Contains(channel.Id))
                .OrderByDescending(channel => channel.WatchCount)
                .ThenByDescending(channel => channel.LastWatchedAtUtc)
                .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(6)
                .ToList();

            return sections;
        }

        private void ApplySpotlightSections(IReadOnlyDictionary<string, List<BrowserChannelViewModel>> sectionChannels)
        {
            foreach (var section in SpotlightSections)
            {
                section.Channels.Clear();
                if (!sectionChannels.TryGetValue(section.Key, out var channels))
                {
                    continue;
                }

                foreach (var channel in channels)
                {
                    section.Channels.Add(channel);
                }
            }

            OnPropertyChanged(nameof(SpotlightVisibility));
        }

        private void ApplyLastTunedState(IEnumerable<BrowserChannelViewModel> channels)
        {
            foreach (var channel in channels)
            {
                channel.IsLastTuned = IsLastChannel(channel);
                channel.LastTunedVisibility = channel.IsLastTuned
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        }

        private bool IsPriorityCandidate(BrowserChannelViewModel channel)
        {
            return channel.IsFavorite ||
                   channel.IsSportsChannel ||
                   channel.IsLastTuned ||
                   channel.WatchCount > 0 ||
                   IsRecentChannel(channel);
        }

        private int ComputePriorityScore(BrowserChannelViewModel channel)
        {
            var score = 0;

            if (IsLastChannel(channel))
            {
                score += 2600;
            }

            var recentRank = FindRecentRank(channel);
            if (recentRank >= 0)
            {
                score += Math.Max(0, 1700 - (recentRank * 120));
            }

            if (channel.IsFavorite)
            {
                score += 1200;
            }

            if (channel.IsTurkishSportsChannel)
            {
                score += 1050;
            }
            else if (channel.IsSportsChannel)
            {
                score += 760;
            }

            if (channel.HasMatchedGuide)
            {
                score += 260;
            }
            else if (channel.HasGuideData)
            {
                score += 90;
            }

            score += Math.Min(channel.WatchCount, 25) * 70;

            if (channel.LastWatchedAtUtc.HasValue)
            {
                var age = DateTime.UtcNow - channel.LastWatchedAtUtc.Value;
                if (age <= TimeSpan.FromHours(6))
                {
                    score += 220;
                }
                else if (age <= TimeSpan.FromDays(1))
                {
                    score += 140;
                }
                else if (age <= TimeSpan.FromDays(3))
                {
                    score += 80;
                }
            }

            return score;
        }

        private string ResolveBrowseResultSubtitle()
        {
            var filterKey = SelectedCategory?.FilterKey ?? string.Empty;
            return filterKey switch
            {
                SmartPriorityCategoryKey => "High-value sports, favorites, and repeat channels ordered for fast match watching.",
                SmartSportsCategoryKey => "Sports-led channels across the visible providers, still ordered by priority first.",
                SmartTurkishSportsCategoryKey => "Turkish sports coverage grouped for fast kickoff and match-day access.",
                SmartRecentCategoryKey => "Your recent live history, with the newest tune-ins closest to the top.",
                _ when string.IsNullOrWhiteSpace(filterKey) => "Every visible live channel, ordered priority-first with A-Z still available as a fallback.",
                _ when !string.IsNullOrWhiteSpace(SelectedCategory?.Description) => SelectedCategory!.Description,
                _ => $"Channels in {SelectedCategory?.Name ?? "this category"}, ordered to surface the best watch options first."
            };
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

            var targetSelection = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
            if (!ReferenceEquals(SelectedSourceOption, targetSelection))
            {
                var wasInitializing = _isInitializing;
                _isInitializing = true;
                try
                {
                    SelectedSourceOption = targetSelection;
                }
                finally
                {
                    _isInitializing = wasInitializing;
                }
            }
        }

        private void BuildVisibleCategories()
        {
            var visibleChannels = BuildSourceScopedChannels();

            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel
            {
                Id = 0,
                FilterKey = string.Empty,
                Name = "All channels",
                Description = "Every visible live channel",
                ItemCount = visibleChannels.Count,
                OrderIndex = -1
            });

            void AddSmartCategory(int id, string key, string name, string description, int count, int orderIndex)
            {
                if (count <= 0)
                {
                    return;
                }

                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = id,
                    FilterKey = key,
                    Name = name,
                    Description = description,
                    ItemCount = count,
                    OrderIndex = orderIndex,
                    IsSmartCategory = true
                });
            }

            AddSmartCategory(-1, SmartPriorityCategoryKey, "Priority watch", "High-value sports, favorites, and repeat channels", visibleChannels.Count(IsPriorityCandidate), 0);
            AddSmartCategory(-2, SmartSportsCategoryKey, "Sports", "All sports-led channels across providers", visibleChannels.Count(channel => channel.IsSportsChannel), 1);
            AddSmartCategory(-3, SmartTurkishSportsCategoryKey, "Turkish sports", "Fast access to Turkish sports coverage", visibleChannels.Count(channel => channel.IsTurkishSportsChannel), 2);
            AddSmartCategory(-4, SmartRecentCategoryKey, "Recent", "Channels you've watched recently", visibleChannels.Count(IsRecentChannel), 3);

            var categories = visibleChannels
                .GroupBy(channel => channel.DisplayCategoryName)
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select((group, index) => new BrowserCategoryViewModel
                {
                    Id = index + 10,
                    FilterKey = _browsePreferencesService.NormalizeCategoryKey(group.Key),
                    Name = group.Key,
                    ItemCount = group.Count(),
                    OrderIndex = index + 10
                })
                .ToList();

            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            Log($"RAIL: BuildVisibleCategories completed with {Categories.Count} categories from {visibleChannels.Count} visible channels");
        }

        private void BuildCategoryManagerOptions()
        {
            var currentSelectionKey = SelectedManageCategory?.Key ?? string.Empty;
            ManageCategoryOptions.Clear();

            foreach (var category in _allRawCategories
                         .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var normalizedKey = _browsePreferencesService.NormalizeCategoryKey(category.Name);
                var autoDisplayName = _taxonomyService.ResolveLiveCategory(category.Name, string.Empty).DisplayCategoryName;
                var hasManualAlias = _preferences.CategoryRemaps.ContainsKey(normalizedKey);
                ManageCategoryOptions.Add(new BrowseCategoryManagerOptionViewModel(
                    normalizedKey,
                    category.Name,
                    ResolveEffectiveLiveDisplayCategoryName(category.Name, string.Empty),
                    autoDisplayName,
                    category.Count,
                    _preferences.HiddenCategoryKeys.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase),
                    hasManualAlias));
            }

            SelectedManageCategory = ManageCategoryOptions.FirstOrDefault(option => string.Equals(option.Key, currentSelectionKey, StringComparison.OrdinalIgnoreCase));
        }

        private string ResolveEffectiveLiveDisplayCategoryName(string? rawCategoryName, string? channelName)
        {
            var autoDisplayCategory = _taxonomyService.ResolveLiveCategory(rawCategoryName, channelName).DisplayCategoryName;
            return _browsePreferencesService.GetEffectiveCategoryName(_preferences, rawCategoryName, autoDisplayCategory);
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
            return (SelectedSortOption?.Key ?? DefaultPrioritySortKey) switch
            {
                "name_desc" => channels.OrderByDescending(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                "name_asc" => channels.OrderBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                "favorites_first" => channels
                    .OrderByDescending(channel => channel.IsFavorite)
                    .ThenByDescending(channel => ComputePriorityScore(channel))
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                "guide_first" => channels
                    .OrderByDescending(channel => channel.HasMatchedGuide)
                    .ThenByDescending(channel => ComputePriorityScore(channel))
                    .ThenBy(channel => channel.CurrentProgramTitle == "No current listing")
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => channels
                    .OrderByDescending(channel => ComputePriorityScore(channel))
                    .ThenByDescending(channel => channel.IsSportsChannel)
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
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

            Log($"SEL: source visibility -> id={sourceOption.Id}, visible={isVisible}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: true);
        }

        private async Task RefreshGuideAndDeferredSectionsAsync(
            int requestVersion,
            IReadOnlyCollection<BrowserChannelViewModel> baseVisibleChannels,
            List<BrowserChannelViewModel> filteredList,
            DateTime nowUtc)
        {
            Log($"09: deferred guide refresh start request={requestVersion} channels={filteredList.Count}");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var guideService = scope.ServiceProvider.GetRequiredService<ILiveGuideService>();
                var summaries = await guideService.GetGuideSummariesAsync(
                    db,
                    filteredList.Select(channel => channel.Id).ToList(),
                    nowUtc);

                if (requestVersion != _filterRequestVersion)
                {
                    Log($"10: request {requestVersion} became stale during guide query");
                    return;
                }

                foreach (var channel in filteredList)
                {
                    summaries.TryGetValue(channel.Id, out var summary);
                    channel.ApplyGuideSummary(summary, nowUtc);
                }

                await RefreshDeferredSpotlightSectionsAsync(requestVersion, baseVisibleChannels);
                Log($"11: guide summaries applied for request={requestVersion}");
            }
            catch (Exception ex)
            {
                if (requestVersion != _filterRequestVersion)
                {
                    Log($"10: request {requestVersion} became stale after guide failure");
                    return;
                }

                foreach (var channel in filteredList)
                {
                    channel.ApplyGuideSummary(null, nowUtc);
                }

                ClearSpotlightSections();
                Log($"11: guide refresh failed for request={requestVersion}: {ex.GetType().Name}");
            }

            var finalList = GuideMatchedOnly
                ? filteredList.Where(channel => channel.HasMatchedGuide).ToList()
                : filteredList;
            finalList = SortChannels(finalList).ToList();

            ApplyVisibleChannels(finalList, requestVersion, "12: final grid applied after deferred guide refresh");
        }

        private async Task RefreshDeferredSpotlightSectionsAsync(
            int requestVersion,
            IReadOnlyCollection<BrowserChannelViewModel> baseVisibleChannels)
        {
            ApplyLastTunedState(_allChannels);

            if (!EnableDeferredSpotlightHelpers)
            {
                ClearSpotlightSections();
                Log($"10: spotlight helper build skipped for browser-first render request={requestVersion}");
                return;
            }

            await Task.Yield();
            if (requestVersion != _filterRequestVersion)
            {
                Log($"10: request {requestVersion} became stale before spotlight helper build");
                return;
            }

            ResetSpotlightSections(baseVisibleChannels);
            Log($"10: spotlight helper build completed for request={requestVersion}");
        }

        private void ApplyVisibleChannels(
            IReadOnlyList<BrowserChannelViewModel> visibleChannels,
            int requestVersion,
            string phaseLogMessage)
        {
            if (requestVersion != _filterRequestVersion)
            {
                Log($"{phaseLogMessage} skipped because request {requestVersion} is stale");
                return;
            }

            EnsureSelectedSourceOption();

            FilteredChannels.Clear();
            foreach (var channel in visibleChannels)
            {
                FilteredChannels.Add(channel);
            }

            HasAdvancedFilters = FavoritesOnly ||
                                 GuideMatchedOnly ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? DefaultPrioritySortKey, DefaultPrioritySortKey, StringComparison.OrdinalIgnoreCase);
            IsEmpty = FilteredChannels.Count == 0;
            NotifyBrowseResultChanged();
            Log($"{phaseLogMessage}; visible channels={FilteredChannels.Count}");
        }
    }
}
