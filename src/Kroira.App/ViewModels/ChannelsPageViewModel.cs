#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private const string SmartPriorityCategoryKey = "live.library.priority";
        private const string SmartSportsCategoryKey = "live.sports.all";
        private const string SmartTurkishSportsCategoryKey = "live.sports.turkish";
        private const string SmartRecentCategoryKey = "live.library.recent";
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
        private readonly ICatalogDiscoveryService _catalogDiscoveryService;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private readonly ISmartCategoryService _smartCategoryService;
        private readonly ILogicalCatalogStateService _logicalCatalogStateService;
        private readonly List<BrowserChannelViewModel> _allChannels = new();
        private readonly List<(int Id, string Name, int Count)> _allRawCategories = new();
        private readonly Dictionary<int, string> _sourceNames = new();
        private readonly Dictionary<int, SourceType> _sourceTypes = new();
        private readonly Dictionary<int, CatalogDiscoveryHealthBucket> _sourceHealthById = new();
        private readonly Dictionary<int, DateTime?> _sourceLastSyncById = new();
        private readonly Dictionary<int, string> _categoryNames = new();
        private readonly Dictionary<int, string> _liveSearchTextById = new();
        private SmartCategoryIndex<BrowserChannelViewModel> _liveCategoryIndex = SmartCategoryIndex<BrowserChannelViewModel>.Empty;
        private readonly Dictionary<string, LiveChannelSectionViewModel> _spotlightSectionMap = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _favoriteLogicalKeys = new(StringComparer.OrdinalIgnoreCase);
        private BrowsePreferences _preferences = new();
        private int _activeProfileId;
        private int _filterRequestVersion;
        private int _localizedTextVersion = -1;
        private int _lastPreDiscoveryCount;
        private bool _lastDiscoveryFacetFiltersActive;
        private bool _isInitializing;
        private bool _hasLoadedOnce;
        private ChannelsNavigationContext? _navigationContext;
        private Stopwatch? _activeLoadStopwatch;

        public BulkObservableCollection<BrowserChannelViewModel> FilteredChannels { get; } = new();
        public ObservableCollection<LiveChannelSectionViewModel> SpotlightSections { get; } = new();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new();
        public ObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySignalOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySourceTypeOptions { get; } = new();

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
        private BrowseFacetOptionViewModel? _selectedDiscoverySignalOption;

        [ObservableProperty]
        private BrowseFacetOptionViewModel? _selectedDiscoverySourceTypeOption;

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

        [ObservableProperty]
        private string _discoverySummaryText = LocalizedStrings.Get("Live_DiscoverySummary_Default");

        [ObservableProperty]
        private string _emptyStateTitle = LocalizedStrings.Get("Live_Empty_NoChannels_Title");

        [ObservableProperty]
        private string _emptyStateMessage = LocalizedStrings.Get("Live_Empty_NoChannels_Message");

        public bool HasManageCategorySelection => SelectedManageCategory != null;
        public string BrowseResultTitle => SelectedCategory?.Name ?? LocalizedStrings.Get("Live_Browse_AllChannels");
        public bool HasLoadedOnce => _hasLoadedOnce;
        public string BrowseResultSubtitle => ResolveBrowseResultSubtitle();
        public string BrowseResultCountText => FilteredChannels.Count == 1
            ? LocalizedStrings.Get("Browse_ChannelCount_One")
            : LocalizedStrings.Format("Browse_ChannelCount_Many", FilteredChannels.Count);
        public Microsoft.UI.Xaml.Visibility DiscoverySignalVisibility => DiscoverySignalOptions.Count > 2
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility DiscoverySourceTypeVisibility => DiscoverySourceTypeOptions.Count > 2
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
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
            QueueApplyFilter("search-query-changed");
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            UpdateCategorySelectionState(value?.FilterKey ?? string.Empty);
            if (_isInitializing)
            {
                NotifyBrowseResultChanged();
                return;
            }

            _preferences.SelectedCategoryKey = value?.FilterKey ?? string.Empty;
            Log($"SEL: category -> {value?.FilterKey ?? "(all)"} ({value?.Name ?? "All channels"})");
            NotifyBrowseResultChanged();
            QueueApplyFilter("selected-category-changed");
            _ = SavePreferencesOnlyAsync("selected-category-changed");
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

        partial void OnSelectedDiscoverySignalOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.DiscoverySignalKey = value?.Key ?? string.Empty;
            Log($"SEL: discovery signal -> {_preferences.DiscoverySignalKey}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
        }

        partial void OnSelectedDiscoverySourceTypeOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializing)
            {
                return;
            }

            _preferences.DiscoverySourceTypeKey = value?.Key ?? string.Empty;
            Log($"SEL: discovery source type -> {_preferences.DiscoverySourceTypeKey}");
            _ = SavePreferencesAndRefreshAsync(rebuildCollections: false);
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
            _catalogDiscoveryService = serviceProvider.GetRequiredService<ICatalogDiscoveryService>();
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
            _smartCategoryService = serviceProvider.GetRequiredService<ISmartCategoryService>();
            _logicalCatalogStateService = serviceProvider.GetRequiredService<ILogicalCatalogStateService>();
            RegisterLocalizedSpotlightSections();
            RebuildSortOptions();
            DiscoverySignalOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySignalVisibility));
            DiscoverySourceTypeOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySourceTypeVisibility));
        }

        public void SetNavigationContext(ChannelsNavigationContext? context)
        {
            _navigationContext = context?.Mode == ChannelsNavigationMode.Sports ? context : null;
            Log($"NAV: context set mode={_navigationContext?.Mode.ToString() ?? "Default"}");
        }

        public void RefreshNavigationContext(ChannelsNavigationContext? context)
        {
            RefreshLocalizedLabelsIfNeeded();
            SetNavigationContext(context);
            if (!_hasLoadedOnce || Categories.Count == 0)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                ApplyInitialFilterSelection();
            }
            finally
            {
                _isInitializing = false;
            }

            UpdateCategorySelectionState(SelectedCategory?.FilterKey ?? string.Empty);
            NotifyBrowseResultChanged();
            QueueApplyFilter("navigation-context");
        }

        [RelayCommand]
        public async Task LoadChannelsAsync()
        {
            RefreshLocalizedLabelsIfNeeded();
            var loadStopwatch = Stopwatch.StartNew();
            _activeLoadStopwatch = loadStopwatch;
            Log("01: LoadChannelsAsync entered");
            SurfaceState = SurfaceStateCopies.LiveTv.Create(SurfaceViewState.Loading);
            _allChannels.Clear();
            _allRawCategories.Clear();
            _sourceNames.Clear();
            _sourceTypes.Clear();
            _sourceHealthById.Clear();
            _sourceLastSyncById.Clear();
            _categoryNames.Clear();
            Categories.Clear();
            ClearSpotlightSections();
            ManageCategoryOptions.Clear();

            await Task.Yield();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var surfaceStateService = scope.ServiceProvider.GetRequiredService<ISurfaceStateService>();
                var sourceAvailability = await surfaceStateService.GetSourceAvailabilityAsync(db);
                if (sourceAvailability.SourceCount == 0)
                {
                    SourceOptions.Clear();
                    SourceVisibilityOptions.Clear();
                    ManageCategoryOptions.Clear();
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
                var healthStates = await db.SourceHealthReports
                    .AsNoTracking()
                    .OrderByDescending(report => report.EvaluatedAtUtc)
                    .Select(report => new
                    {
                        report.SourceProfileId,
                        report.HealthState
                    })
                    .ToListAsync();
                var sourceTypeById = sources.ToDictionary(source => source.Id, source => source.Type);
                foreach (var source in sources)
                {
                    _sourceNames[source.Id] = source.Name;
                    _sourceTypes[source.Id] = source.Type;
                    _sourceLastSyncById[source.Id] = source.LastSync;
                }

                foreach (var health in healthStates.GroupBy(item => item.SourceProfileId))
                {
                    _sourceHealthById[health.Key] = _catalogDiscoveryService.ResolveHealthBucket(health.First().HealthState);
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
                        SourceName = _sourceNames.TryGetValue(category.SourceProfileId, out var sourceName) ? sourceName : LocalizedStrings.Get("Browse_UnknownSource"),
                        SourceType = sourceTypeById.TryGetValue(category.SourceProfileId, out var sourceType) ? sourceType : SourceType.M3U,
                        RawName = channel.Name,
                        Name = cleanedChannelName,
                        CategoryName = category.Name,
                        DisplayCategoryName = displayCategory,
                        StreamUrl = channel.StreamUrl,
                        LogoUrl = channel.LogoUrl ?? string.Empty,
                        HasGuideLink = !string.IsNullOrWhiteSpace(channel.EpgChannelId) || channel.EpgMatchConfidence > 0,
                        IsFavorite = _favoriteLogicalKeys.Contains(logicalKey),
                        IsSportsChannel = ContentClassifier.IsSportsLikeChannel(cleanedChannelName, displayCategory),
                        IsTurkishSportsChannel = ContentClassifier.IsTurkishSportsLikeChannel(cleanedChannelName, displayCategory),
                        WatchCount = watchCount,
                        LastWatchedAtUtc = lastWatchedAtUtc,
                        SourceLastSyncUtc = _sourceLastSyncById.TryGetValue(category.SourceProfileId, out var lastSyncUtc) ? lastSyncUtc : null,
                        SourceHealthBucket = _sourceHealthById.TryGetValue(category.SourceProfileId, out var healthBucket) ? healthBucket : CatalogDiscoveryHealthBucket.Unknown,
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
                Log("04: built source collections");

                _isInitializing = true;
                try
                {
                    ApplyInitialFilterSelectionWithoutCategories();
                }
                finally
                {
                    _isInitializing = false;
                }

                Log("05: initialized selected filters");
                QueueApplyFilter("load-channels");
                SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, _allChannels.Count, SurfaceStateCopies.LiveTv);
                _hasLoadedOnce = true;
                OnPropertyChanged(nameof(HasLoadedOnce));
                await Task.Yield();
                await BuildVisibleCategoriesAsync();
                Log("06a: built category collections");
                _isInitializing = true;
                try
                {
                    ApplyInitialFilterSelection();
                }
                finally
                {
                    _isInitializing = false;
                }

                UpdateCategorySelectionState(SelectedCategory?.FilterKey ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(SelectedCategory?.FilterKey))
                {
                    QueueApplyFilter("load-category-ready");
                }

                Log("06: queued ApplyFilter");
            }
            catch (Exception ex)
            {
                Log($"LOAD FAILED: {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.LiveTv, ex);
            }
            finally
            {
                Log($"load complete fullMs={loadStopwatch.ElapsedMilliseconds} channels={_allChannels.Count} visible={FilteredChannels.Count}");
                _activeLoadStopwatch = null;
            }
        }

        private void ApplyInitialFilterSelectionWithoutCategories()
        {
            FavoritesOnly = _preferences.FavoritesOnly;
            GuideMatchedOnly = _preferences.GuideMatchedOnly;
            SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, ResolveInitialSortKey(), StringComparison.OrdinalIgnoreCase))
                ?? SortOptions.First();
            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _preferences.SelectedSourceId)
                ?? SourceOptions.FirstOrDefault();
            SelectedCategory = null;
        }

        private void ApplyInitialFilterSelection()
        {
            if (_navigationContext?.Mode == ChannelsNavigationMode.Sports &&
                TryResolveSportsCategoryKey(out var sportsCategoryKey))
            {
                SearchQuery = string.Empty;
                FavoritesOnly = false;
                GuideMatchedOnly = false;
                SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, DefaultPrioritySortKey, StringComparison.OrdinalIgnoreCase))
                    ?? SortOptions.First();
                SelectedSourceOption = SourceOptions.FirstOrDefault();
                SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, sportsCategoryKey, StringComparison.OrdinalIgnoreCase))
                    ?? Categories.FirstOrDefault();

                Log($"NAV: sports focus applied category={SelectedCategory?.FilterKey ?? "(all)"} count={SelectedCategory?.ItemCount ?? 0}");
                return;
            }

            if (_navigationContext?.Mode == ChannelsNavigationMode.Sports)
            {
                Log("NAV: sports focus requested but no sports category exists; falling back to normal channel state");
            }

            FavoritesOnly = _preferences.FavoritesOnly;
            GuideMatchedOnly = _preferences.GuideMatchedOnly;
            SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, ResolveInitialSortKey(), StringComparison.OrdinalIgnoreCase))
                ?? SortOptions.First();
            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _preferences.SelectedSourceId)
                ?? SourceOptions.FirstOrDefault();
            SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, _preferences.SelectedCategoryKey, StringComparison.OrdinalIgnoreCase))
                ?? Categories.FirstOrDefault();
        }

        private bool TryResolveSportsCategoryKey(out string categoryKey)
        {
            var smartSports = Categories.FirstOrDefault(category =>
                string.Equals(category.FilterKey, SmartSportsCategoryKey, StringComparison.OrdinalIgnoreCase));
            if (smartSports != null)
            {
                categoryKey = smartSports.FilterKey;
                return true;
            }

            var categoryMatch = Categories
                .Where(category => !category.IsSmartCategory)
                .FirstOrDefault(category =>
                    ContentClassifier.IsSportsLikeLabel(category.Name) ||
                    ContentClassifier.IsSportsLikeLabel(category.Description));

            if (categoryMatch != null)
            {
                categoryKey = categoryMatch.FilterKey;
                return true;
            }

            categoryKey = string.Empty;
            return false;
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

        private void QueueApplyFilter(string reason = "direct")
        {
            _ = ApplyFilterAsync(System.Threading.Interlocked.Increment(ref _filterRequestVersion), reason);
        }

        private async Task ApplyFilterAsync(int requestVersion, string reason)
        {
            var totalStopwatch = Stopwatch.StartNew();
            Log($"PERF apply_start request={requestVersion} reason={reason} selectedKey={SelectedCategory?.FilterKey ?? "<all>"} search='{SearchQuery}' source={SelectedSourceOption?.Id ?? 0}");
            var selectedCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            var categoryStopwatch = Stopwatch.StartNew();
            var baseVisibleChannels = GetLiveCategoryCandidates(selectedCategoryKey).ToList();
            Log($"PERF category_candidates request={requestVersion} reason={reason} count={baseVisibleChannels.Count} ms={categoryStopwatch.ElapsedMilliseconds}");

            var filtered = baseVisibleChannels.AsEnumerable();

            var composeStopwatch = Stopwatch.StartNew();

            if (FavoritesOnly)
            {
                filtered = filtered.Where(channel => channel.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var normalizedQuery = _smartCategoryService.NormalizeKey(SearchQuery);
                filtered = filtered.Where(channel => MatchesLiveSearch(channel, normalizedQuery));
            }

            var nowUtc = DateTime.UtcNow;
            var preDiscoveryList = filtered.ToList();
            Log($"PERF filter_compose request={requestVersion} reason={reason} count={preDiscoveryList.Count} ms={composeStopwatch.ElapsedMilliseconds}");
            var discoveryStopwatch = Stopwatch.StartNew();
            var discoveryProjection = BuildDiscoveryProjection(preDiscoveryList, nowUtc);
            RefreshDiscoveryOptions(discoveryProjection);
            var filteredList = preDiscoveryList
                .Where(channel => discoveryProjection.MatchingKeys.Contains(channel.LogicalContentKey))
                .ToList();
            Log($"PERF discovery request={requestVersion} reason={reason} count={filteredList.Count} ms={discoveryStopwatch.ElapsedMilliseconds}");
            DiscoverySummaryText = discoveryProjection.SummaryText;
            _lastPreDiscoveryCount = preDiscoveryList.Count;
            _lastDiscoveryFacetFiltersActive = discoveryProjection.HasActiveFacetFilters;
            Log($"08: filtered down to {filteredList.Count} channels before guide load");

            if (requestVersion != _filterRequestVersion)
            {
                Log($"08a: request {requestVersion} became stale before initial grid apply");
                return;
            }

            var shouldRefreshGuide = ShouldRefreshGuideDuringApply(reason);
            if (!GuideMatchedOnly)
            {
                var sortStopwatch = Stopwatch.StartNew();
                var initialSorted = SortChannels(filteredList).ToList();
                Log($"PERF sort_initial request={requestVersion} reason={reason} count={initialSorted.Count} ms={sortStopwatch.ElapsedMilliseconds}");
                ApplyVisibleChannels(initialSorted, requestVersion, "08b: initial grid applied before deferred guide refresh");
                await Task.Yield();
            }
            else if (shouldRefreshGuide)
            {
                Log("08b: guide matched filter active; deferring initial grid until guide refresh completes");
            }
            else
            {
                var cachedGuideMatches = filteredList.Where(channel => channel.HasMatchedGuide).ToList();
                var sortStopwatch = Stopwatch.StartNew();
                var sortedCachedGuideMatches = SortChannels(cachedGuideMatches).ToList();
                Log($"PERF sort_cached_guide request={requestVersion} reason={reason} count={sortedCachedGuideMatches.Count} ms={sortStopwatch.ElapsedMilliseconds}");
                ApplyVisibleChannels(sortedCachedGuideMatches, requestVersion, "08b: cached guide-matched grid applied");
                await Task.Yield();
            }

            if (requestVersion != _filterRequestVersion)
            {
                Log($"08c: request {requestVersion} became stale before deferred guide refresh");
                return;
            }

            if (shouldRefreshGuide)
            {
                await RefreshGuideAndDeferredSectionsAsync(requestVersion, baseVisibleChannels, filteredList, nowUtc);
            }
            else
            {
                Log($"PERF guide_refresh_skipped request={requestVersion} reason={reason}");
            }

            Log($"PERF apply_end request={requestVersion} reason={reason} total_ms={totalStopwatch.ElapsedMilliseconds}");
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
            BuildVisibleCategories();
            EnsureSelectedCategory();

            QueueApplyFilter("toggle-favorite");
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

            BuildVisibleCategories();
            EnsureSelectedCategory();
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

            QueueApplyFilter(rebuildCollections ? "save-preferences-rebuild" : "save-preferences");
        }

        private async Task SavePreferencesOnlyAsync(string reason)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await _browsePreferencesService.SaveAsync(db, Domain, _activeProfileId, _preferences);
                Log($"PERF preferences_saved reason={reason} ms={stopwatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                Log($"preferences save skipped reason={reason} error={ex.Message}");
            }
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

        private CatalogDiscoveryProjection BuildDiscoveryProjection(
            IReadOnlyList<BrowserChannelViewModel> channels,
            DateTime nowUtc)
        {
            var records = channels
                .Select(channel => new CatalogDiscoveryRecord
                {
                    Key = channel.LogicalContentKey,
                    Domain = CatalogDiscoveryDomain.Live,
                    SourceProfileIds = [channel.SourceProfileId],
                    SourceTypes = [channel.SourceType],
                    IsFavorite = channel.IsFavorite,
                    HasGuide = channel.HasGuideLink,
                    HasCatchup = channel.SupportsCatchup,
                    HealthBucket = channel.SourceHealthBucket,
                    LastSyncUtc = channel.SourceLastSyncUtc,
                    LastInteractionUtc = channel.LastWatchedAtUtc
                })
                .ToList();

            return _catalogDiscoveryService.BuildProjection(
                CatalogDiscoveryDomain.Live,
                records,
                new CatalogDiscoverySelection
                {
                    SignalKey = SelectedDiscoverySignalOption?.Key ?? _preferences.DiscoverySignalKey,
                    SourceTypeKey = SelectedDiscoverySourceTypeOption?.Key ?? _preferences.DiscoverySourceTypeKey
                },
                nowUtc);
        }

        private void RefreshDiscoveryOptions(CatalogDiscoveryProjection projection)
        {
            _preferences.DiscoverySignalKey = projection.EffectiveSelection.SignalKey;
            _preferences.DiscoverySourceTypeKey = projection.EffectiveSelection.SourceTypeKey;
            _preferences.DiscoveryLanguageKey = string.Empty;
            _preferences.DiscoveryTagKey = string.Empty;

            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                DiscoverySignalOptions.ReplaceAll(projection.SignalOptions.Select(ToBrowseFacetOption));
                DiscoverySourceTypeOptions.ReplaceAll(projection.SourceTypeOptions.Select(ToBrowseFacetOption));

                SelectedDiscoverySignalOption = DiscoverySignalOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.SignalKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoverySignalOptions.FirstOrDefault();
                SelectedDiscoverySourceTypeOption = DiscoverySourceTypeOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.SourceTypeKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoverySourceTypeOptions.FirstOrDefault();
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private static BrowseFacetOptionViewModel ToBrowseFacetOption(CatalogDiscoveryFacetOption option)
        {
            return new BrowseFacetOptionViewModel(option.Key, option.Label, option.ItemCount);
        }

        private void UpdateEmptyState(int baseResultCount, bool hasDiscoveryFacetFilters)
        {
            if (!IsEmpty)
            {
                EmptyStateTitle = string.Empty;
                EmptyStateMessage = string.Empty;
                return;
            }

            var hasNarrowingFilters = hasDiscoveryFacetFilters ||
                                      FavoritesOnly ||
                                      GuideMatchedOnly ||
                                      (SelectedSourceOption?.Id ?? 0) != 0 ||
                                      !string.IsNullOrWhiteSpace(SearchQuery) ||
                                      !string.IsNullOrWhiteSpace(SelectedCategory?.FilterKey);
            if (hasNarrowingFilters)
            {
                EmptyStateTitle = LocalizedStrings.Get("Browse_Empty_NoMatches_Title");
                EmptyStateMessage = LocalizedStrings.Get("Live_Empty_NoMatches_Message");
                return;
            }

            EmptyStateTitle = baseResultCount == 0
                ? LocalizedStrings.Get("Live_Empty_NoChannels_Title")
                : LocalizedStrings.Get("Live_Empty_BrowserEmpty_Title");
            EmptyStateMessage = LocalizedStrings.Get("Live_Empty_BrowserEmpty_Message");
        }

        public void RefreshLocalizedLabelsIfNeeded()
        {
            if (_localizedTextVersion == LocalizedStrings.Version)
            {
                return;
            }

            _localizedTextVersion = LocalizedStrings.Version;
            DiscoverySummaryText = LocalizedStrings.Get("Live_DiscoverySummary_Default");
            RebuildSortOptions();
            RefreshSpotlightSectionText();
            if (_hasLoadedOnce)
            {
                BuildSourceOptions();
                RefreshCategoryText();
                BuildCategoryManagerOptions();
                UpdateEmptyState(_lastPreDiscoveryCount, _lastDiscoveryFacetFiltersActive);
            }

            NotifyBrowseResultChanged();
        }

        private void RebuildSortOptions()
        {
            var selectedKey = SelectedSortOption?.Key ?? ResolveInitialSortKey();
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                SortOptions.Clear();
                foreach (var option in BrowseLocalization.CreateSortOptions(BrowseLocalization.LiveSortOptions))
                {
                    SortOptions.Add(option);
                }

                SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                    ?? SortOptions.FirstOrDefault();
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private void RegisterLocalizedSpotlightSections()
        {
            RegisterSpotlightSection("last_tuned", LocalizedStrings.Get("Live_Spotlight_LastTuned_Title"), LocalizedStrings.Get("Live_Spotlight_LastTuned_Subtitle"));
            RegisterSpotlightSection("recent", LocalizedStrings.Get("Live_Spotlight_Recent_Title"), LocalizedStrings.Get("Live_Spotlight_Recent_Subtitle"));
            RegisterSpotlightSection("priority", LocalizedStrings.Get("Live_Spotlight_Priority_Title"), LocalizedStrings.Get("Live_Spotlight_Priority_Subtitle"));
            RegisterSpotlightSection("turkish_sports", LocalizedStrings.Get("Live_Spotlight_TurkishSports_Title"), LocalizedStrings.Get("Live_Spotlight_TurkishSports_Subtitle"));
            RegisterSpotlightSection("most_watched_sports", LocalizedStrings.Get("Live_Spotlight_MostWatchedSports_Title"), LocalizedStrings.Get("Live_Spotlight_MostWatchedSports_Subtitle"));
        }

        private void RefreshSpotlightSectionText()
        {
            UpdateSpotlightSectionText("last_tuned", LocalizedStrings.Get("Live_Spotlight_LastTuned_Title"), LocalizedStrings.Get("Live_Spotlight_LastTuned_Subtitle"));
            UpdateSpotlightSectionText("recent", LocalizedStrings.Get("Live_Spotlight_Recent_Title"), LocalizedStrings.Get("Live_Spotlight_Recent_Subtitle"));
            UpdateSpotlightSectionText("priority", LocalizedStrings.Get("Live_Spotlight_Priority_Title"), LocalizedStrings.Get("Live_Spotlight_Priority_Subtitle"));
            UpdateSpotlightSectionText("turkish_sports", LocalizedStrings.Get("Live_Spotlight_TurkishSports_Title"), LocalizedStrings.Get("Live_Spotlight_TurkishSports_Subtitle"));
            UpdateSpotlightSectionText("most_watched_sports", LocalizedStrings.Get("Live_Spotlight_MostWatchedSports_Title"), LocalizedStrings.Get("Live_Spotlight_MostWatchedSports_Subtitle"));
        }

        private void UpdateSpotlightSectionText(string key, string title, string subtitle)
        {
            if (_spotlightSectionMap.TryGetValue(key, out var section))
            {
                section.UpdateText(title, subtitle);
            }
        }

        private void RefreshCategoryText()
        {
            foreach (var category in Categories)
            {
                if (category.IsSmartCategory &&
                    _smartCategoryService.GetDefinition(category.SmartCategoryId) is { } definition)
                {
                    category.UpdateText(
                        BrowseLocalization.SmartCategoryName(definition),
                        BrowseLocalization.SmartCategoryDescription(definition),
                        BrowseLocalization.SmartCategorySection(definition));
                }
                else if (category.IsOriginalProviderGroup)
                {
                    category.UpdateText(
                        category.Name,
                        category.Description,
                        BrowseLocalization.OriginalProviderGroups);
                }
            }

            ApplyCategorySectionHeaders(Categories);
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

        private List<BrowserChannelViewModel> BuildSourceScopedChannels(bool refreshDerivedFields = false)
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
                .ToList();

            if (refreshDerivedFields)
            {
                RefreshLiveDerivedFields(baseVisibleChannels);
            }

            if (SelectedSourceOption != null && SelectedSourceOption.Id != 0)
            {
                baseVisibleChannels = baseVisibleChannels
                    .Where(channel => channel.SourceProfileId == SelectedSourceOption.Id)
                    .ToList();
            }

            return baseVisibleChannels;
        }

        private void RefreshLiveDerivedFields(IEnumerable<BrowserChannelViewModel> channels)
        {
            foreach (var channel in channels)
            {
                channel.DisplayCategoryName = ResolveEffectiveLiveDisplayCategoryName(channel.CategoryName, channel.Name);
                channel.IsSportsChannel = ContentClassifier.IsSportsLikeChannel(channel.Name, channel.DisplayCategoryName);
                channel.IsTurkishSportsChannel = ContentClassifier.IsTurkishSportsLikeChannel(channel.Name, channel.DisplayCategoryName);
            }
        }

        private IReadOnlyList<BrowserChannelViewModel> GetLiveCategoryCandidates(string selectedCategoryKey)
        {
            if (_liveCategoryIndex.AllItems.Count == 0)
            {
                return BuildSourceScopedChannels(refreshDerivedFields: true);
            }

            return _liveCategoryIndex.GetItems(selectedCategoryKey);
        }

        private bool MatchesLiveSearch(BrowserChannelViewModel channel, string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return true;
            }

            if (_liveSearchTextById.TryGetValue(channel.Id, out var searchText))
            {
                return searchText.Contains(normalizedQuery, StringComparison.Ordinal);
            }

            return _smartCategoryService.NormalizeKey($"{channel.Name} {channel.RawName} {channel.DisplayCategoryName} {channel.CategoryName} {channel.SourceName}")
                .Contains(normalizedQuery, StringComparison.Ordinal);
        }

        private bool MatchesCategorySelection(BrowserChannelViewModel channel, string filterKey)
        {
            if (_smartCategoryService.TryParseOriginalProviderGroupKey(filterKey, out var providerGroupKey))
            {
                return string.Equals(
                    _smartCategoryService.NormalizeKey(channel.CategoryName),
                    providerGroupKey,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (_smartCategoryService.IsSmartCategoryKey(filterKey))
            {
                return _smartCategoryService.Matches(filterKey, BuildLiveSmartCategoryContext(channel));
            }

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

        private SmartCategoryItemContext BuildLiveSmartCategoryContext(BrowserChannelViewModel channel)
        {
            var normalizedSearchText = _liveSearchTextById.TryGetValue(channel.Id, out var searchText)
                ? searchText
                : _smartCategoryService.NormalizeKey(
                    $"{channel.Name} {channel.RawName} {channel.DisplayCategoryName} {channel.CategoryName} {channel.SourceName}");

            return new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Live,
                Title = channel.Name,
                RawTitle = channel.RawName,
                ProviderGroupName = channel.CategoryName,
                DisplayCategoryName = channel.DisplayCategoryName,
                SourceName = channel.SourceName,
                SourceSummary = channel.SourceName,
                TvgId = string.Empty,
                TvgName = channel.Name,
                LogoUrl = channel.LogoUrl,
                EpgCurrentTitle = channel.CurrentProgramTitle,
                EpgNextTitle = channel.NextProgramTitle,
                NormalizedSearchText = normalizedSearchText,
                SourceLastSyncUtc = channel.SourceLastSyncUtc,
                IsFavorite = channel.IsFavorite,
                IsRecentlyWatched = IsRecentChannel(channel),
                HasGuideLink = channel.HasGuideLink,
                HasGuideData = channel.HasGuideData,
                HasMatchedGuide = channel.HasMatchedGuide,
                IsPrimary = true
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
            var baseText = filterKey switch
            {
                SmartPriorityCategoryKey => LocalizedStrings.Get("Live_Browse_Subtitle_Priority"),
                SmartSportsCategoryKey => LocalizedStrings.Get("Live_Browse_Subtitle_Sports"),
                SmartTurkishSportsCategoryKey => LocalizedStrings.Get("Live_Browse_Subtitle_TurkishSports"),
                SmartRecentCategoryKey => LocalizedStrings.Get("Live_Browse_Subtitle_Recent"),
                _ when string.IsNullOrWhiteSpace(filterKey) => LocalizedStrings.Get("Live_Browse_Subtitle_All"),
                _ when !string.IsNullOrWhiteSpace(SelectedCategory?.Description) => SelectedCategory!.Description,
                _ => LocalizedStrings.Format("Live_Browse_Subtitle_Category", SelectedCategory?.Name ?? LocalizedStrings.Get("Browse_ThisCategory"))
            };

            return HasDiscoveryFilters()
                ? $"{baseText} {DiscoverySummaryText}"
                : baseText;
        }

        private void BuildSourceOptions()
        {
            var existingSelection = SelectedSourceOption?.Id ?? _preferences.SelectedSourceId;
            var sourceOptions = new List<BrowseSourceFilterOptionViewModel>
            {
                new BrowseSourceFilterOptionViewModel(0, BrowseLocalization.AllProviders, _allChannels.Count)
            };
            var sourceVisibilityOptions = new List<BrowseSourceVisibilityViewModel>();

            foreach (var group in _allChannels
                         .GroupBy(channel => channel.SourceProfileId)
                         .Select(group => new
                         {
                             Id = group.Key,
                             Name = _sourceNames.TryGetValue(group.Key, out var name) ? name : BrowseLocalization.SourceFallback(group.Key),
                             Count = group.Count()
                         })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_preferences.HiddenSourceIds.Contains(group.Id);
                sourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                sourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(
                    group.Id,
                    group.Name,
                    BrowseLocalization.ChannelCount(group.Count),
                    isVisible,
                    OnSourceVisibilityChanged));
            }

            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                var sourcePatch = SourceOptions.PatchToMatch(
                    sourceOptions,
                    static (existing, incoming) => existing.Id == incoming.Id,
                    static (existing, incoming) => existing.UpdateFrom(incoming));
                var visibilityPatch = SourceVisibilityOptions.PatchToMatch(
                    sourceVisibilityOptions,
                    static (existing, incoming) => existing.Id == incoming.Id,
                    static (existing, incoming) => existing.UpdateFrom(incoming));

                var targetSelection = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
                if (!ReferenceEquals(SelectedSourceOption, targetSelection))
                {
                    SelectedSourceOption = targetSelection;
                }

                Log(
                    $"SEL: source options patched reused={sourcePatch.ReusedCount} inserted={sourcePatch.InsertedCount} removed={sourcePatch.RemovedCount}; visibility_reused={visibilityPatch.ReusedCount} visibility_inserted={visibilityPatch.InsertedCount} visibility_removed={visibilityPatch.RemovedCount}");
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private async Task BuildVisibleCategoriesAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var visibleChannels = BuildSourceScopedChannels(refreshDerivedFields: true);
            RebuildLiveSearchText(visibleChannels);
            var definitions = _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Live).ToList();
            var indexStopwatch = Stopwatch.StartNew();
            var categoryIndex = await Task.Run(() => SmartCategoryIndexBuilder.Build(
                visibleChannels,
                definitions,
                BuildLiveSmartCategoryContext,
                channel => new[] { channel.CategoryName },
                _smartCategoryService));
            Log($"PERF smart_index media=live items={visibleChannels.Count} contexts={categoryIndex.ContextBuildCount} keys={categoryIndex.ItemsByCategoryKey.Count} ms={indexStopwatch.ElapsedMilliseconds} offThread=True");
            ApplyVisibleCategories(visibleChannels, categoryIndex, stopwatch);
        }

        private void BuildVisibleCategories()
        {
            var stopwatch = Stopwatch.StartNew();
            var visibleChannels = BuildSourceScopedChannels(refreshDerivedFields: true);
            RebuildLiveSearchText(visibleChannels);
            var indexStopwatch = Stopwatch.StartNew();
            var categoryIndex = SmartCategoryIndexBuilder.Build(
                visibleChannels,
                _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Live),
                BuildLiveSmartCategoryContext,
                channel => new[] { channel.CategoryName },
                _smartCategoryService);
            Log($"PERF smart_index media=live items={visibleChannels.Count} contexts={categoryIndex.ContextBuildCount} keys={categoryIndex.ItemsByCategoryKey.Count} ms={indexStopwatch.ElapsedMilliseconds} offThread=False");
            ApplyVisibleCategories(visibleChannels, categoryIndex, stopwatch);
        }

        private void RebuildLiveSearchText(IReadOnlyList<BrowserChannelViewModel> visibleChannels)
        {
            _liveSearchTextById.Clear();
            foreach (var channel in visibleChannels)
            {
                _liveSearchTextById[channel.Id] = _smartCategoryService.NormalizeKey(
                    $"{channel.Name} {channel.RawName} {channel.DisplayCategoryName} {channel.CategoryName} {channel.SourceName}");
            }
        }

        private void ApplyVisibleCategories(
            IReadOnlyList<BrowserChannelViewModel> visibleChannels,
            SmartCategoryIndex<BrowserChannelViewModel> categoryIndex,
            Stopwatch stopwatch)
        {
            _liveCategoryIndex = categoryIndex;
            var categoryItems = new List<BrowserCategoryViewModel>();
            var categoryId = 0;

            foreach (var definition in _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Live))
            {
                var count = definition.IsAllCategory
                    ? categoryIndex.GetCount(string.Empty)
                    : categoryIndex.GetCount(definition.Id);
                if (count <= 0 && !definition.AlwaysShow)
                {
                    continue;
                }

                categoryItems.Add(new BrowserCategoryViewModel
                {
                    Id = categoryId++,
                    FilterKey = definition.IsAllCategory ? string.Empty : definition.Id,
                    SmartCategoryId = definition.Id,
                    Name = BrowseLocalization.SmartCategoryName(definition),
                    Description = BrowseLocalization.SmartCategoryDescription(definition),
                    SectionName = BrowseLocalization.SmartCategorySection(definition),
                    ItemCount = count,
                    OrderIndex = definition.SortPriority,
                    IsSmartCategory = true,
                    IconGlyph = definition.IconGlyph
                });
            }

            var providerCategories = categoryIndex.OriginalProviderGroups
                .Select((group, index) => new BrowserCategoryViewModel
                {
                    Id = categoryId++,
                    FilterKey = group.Key,
                    Name = group.Name,
                    Description = ResolveEffectiveLiveDisplayCategoryName(group.Name, string.Empty),
                    SectionName = BrowseLocalization.OriginalProviderGroups,
                    ItemCount = group.Count,
                    OrderIndex = 100_000 + index,
                    IsOriginalProviderGroup = true,
                    IconGlyph = "\uE8B3"
                })
                .ToList();

            categoryItems.AddRange(providerCategories);
            ApplyCategorySectionHeaders(categoryItems);

            Categories.Clear();
            foreach (var category in categoryItems)
            {
                Categories.Add(category);
            }

            Log($"PERF categories_built media=live categories={Categories.Count} items={visibleChannels.Count} ms={stopwatch.ElapsedMilliseconds}");
        }

        private static void ApplyCategorySectionHeaders(IReadOnlyList<BrowserCategoryViewModel> categories)
        {
            var previousSection = string.Empty;
            foreach (var category in categories)
            {
                var visibility = string.IsNullOrWhiteSpace(category.SectionName) ||
                                 string.Equals(category.SectionName, previousSection, StringComparison.Ordinal)
                    ? Microsoft.UI.Xaml.Visibility.Collapsed
                    : Microsoft.UI.Xaml.Visibility.Visible;
                category.UpdateSectionHeaderVisibility(visibility);
                previousSection = category.SectionName;
            }
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

            UpdateCategorySelectionState(targetCategory?.FilterKey ?? string.Empty);
        }

        private void UpdateCategorySelectionState(string selectedKey)
        {
            foreach (var category in Categories)
            {
                category.IsSelected = string.Equals(category.FilterKey, selectedKey, StringComparison.OrdinalIgnoreCase);
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
                    .ThenBy(channel => string.IsNullOrWhiteSpace(channel.CurrentProgramTitle))
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => channels
                    .OrderByDescending(channel => ComputePriorityScore(channel))
                    .ThenByDescending(channel => channel.IsSportsChannel)
                    .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
            };
        }

        private static bool ShouldRefreshGuideDuringApply(string reason)
        {
            return reason.Contains("load", StringComparison.OrdinalIgnoreCase) ||
                   reason.Contains("guide", StringComparison.OrdinalIgnoreCase) ||
                   reason.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                   reason.Contains("rebuild", StringComparison.OrdinalIgnoreCase);
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
            var guideStopwatch = Stopwatch.StartNew();
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
                Log($"PERF guide_refresh request={requestVersion} channels={filteredList.Count} ms={guideStopwatch.ElapsedMilliseconds}");
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
            var sortStopwatch = Stopwatch.StartNew();
            finalList = SortChannels(finalList).ToList();
            Log($"PERF sort_final request={requestVersion} count={finalList.Count} ms={sortStopwatch.ElapsedMilliseconds}");

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

            var uiStopwatch = Stopwatch.StartNew();
            var patch = FilteredChannels.PatchToMatch(
                visibleChannels,
                static (existing, incoming) => existing.Id == incoming.Id);

            HasAdvancedFilters = FavoritesOnly ||
                                 GuideMatchedOnly ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 HasDiscoveryFilters() ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? DefaultPrioritySortKey, DefaultPrioritySortKey, StringComparison.OrdinalIgnoreCase);
            IsEmpty = FilteredChannels.Count == 0;
            UpdateEmptyState(_lastPreDiscoveryCount, _lastDiscoveryFacetFiltersActive);
            NotifyBrowseResultChanged();
            Log($"{phaseLogMessage}; visible channels={FilteredChannels.Count}; reused={patch.ReusedCount}; inserted={patch.InsertedCount}; removed={patch.RemovedCount}; moved={patch.MovedCount}");
            Log($"PERF ui_update media=live count={FilteredChannels.Count} reused={patch.ReusedCount} inserted={patch.InsertedCount} removed={patch.RemovedCount} moved={patch.MovedCount} ms={uiStopwatch.ElapsedMilliseconds}");
            if (phaseLogMessage.StartsWith("08b:", StringComparison.Ordinal))
            {
                Log($"first content ready ms={_activeLoadStopwatch?.ElapsedMilliseconds ?? -1} reason=load-channels visibleChannels={FilteredChannels.Count}");
            }
        }

        private bool HasDiscoveryFilters()
        {
            return !string.IsNullOrWhiteSpace(_preferences.DiscoverySignalKey) &&
                   !string.Equals(_preferences.DiscoverySignalKey, "all", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrWhiteSpace(_preferences.DiscoverySourceTypeKey) &&
                   !string.Equals(_preferences.DiscoverySourceTypeKey, "all", StringComparison.OrdinalIgnoreCase);
        }
    }
}
